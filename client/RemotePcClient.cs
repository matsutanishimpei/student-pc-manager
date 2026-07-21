using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Share.Models;
using Share.Security;

namespace client
{
    internal sealed class RemotePcClient
    {
        private readonly HttpClient _commandClient;
        private readonly HttpClient _monitoringClient;

        public RemotePcClient(HttpClient commandClient, HttpClient monitoringClient)
        {
            _commandClient = commandClient;
            _monitoringClient = monitoringClient;
        }

        public Task<CommandResponse> ExecuteCommandAsync(
            string address,
            string apiKey,
            string command,
            bool runInUserSession,
            CancellationToken cancellationToken = default)
        {
            var request = new CommandRequest
            {
                Command = command,
                RunInUserSession = runInUserSession
            };

            return SendJsonAsync<CommandRequest, CommandResponse>(
                _commandClient,
                HttpMethod.Post,
                BuildUri(address, "/api/exec"),
                apiKey,
                request,
                cancellationToken);
        }

        public async Task<string> UploadFileAsync(
            string address,
            string apiKey,
            string localPath,
            CancellationToken cancellationToken = default)
        {
            string contentHash;
            string fileNameHash;
            await using (var hashStream = File.OpenRead(localPath))
            {
                contentHash = await ApiSignature.ComputeHashAsync(hashStream, cancellationToken);
            }
            fileNameHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(localPath)))).ToLowerInvariant();

            using HttpResponseMessage response = await _commandClient.SendWithRetryAsync(() =>
            {
                var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(File.OpenRead(localPath));
                content.Add(streamContent, "file", Path.GetFileName(localPath));
                var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(address, "/api/upload")) { Content = content };
                request.AddApiSignature(apiKey, contentHash, fileNameHash);
                return Task.FromResult(request);
            }, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<UploadResult>(cancellationToken: cancellationToken);
            return result?.FilePath ?? string.Empty;
        }

        public async Task<string> GetActiveAppAsync(string address, string apiKey, CancellationToken cancellationToken = default)
        {
            var result = await SendAsync<ActiveAppResponse>(
                _monitoringClient,
                HttpMethod.Get,
                BuildUri(address, "/api/activeapp"),
                apiKey,
                cancellationToken);
            return result.ActiveApp;
        }

        public async Task<string> GetMachineNameAsync(string address, string apiKey, CancellationToken cancellationToken = default)
        {
            var result = await SendAsync<ServerInfoResponse>(
                _monitoringClient,
                HttpMethod.Get,
                BuildUri(address, "/api/info", forceDefaultPort: true),
                apiKey,
                cancellationToken);
            return result.MachineName;
        }

        public async Task<string> GetMacAddressAsync(string address, string apiKey, CancellationToken cancellationToken = default)
        {
            var result = await SendAsync<MacAddressResponse>(
                _monitoringClient,
                HttpMethod.Get,
                BuildUri(address, "/api/mac", forceDefaultPort: true),
                apiKey,
                cancellationToken);
            return result.MacAddress;
        }

        private static async Task<TResponse> SendJsonAsync<TRequest, TResponse>(
            HttpClient client,
            HttpMethod method,
            Uri uri,
            string apiKey,
            TRequest body,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await client.SendWithRetryAsync(async () =>
            {
                var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
                await request.AddApiSignatureAsync(apiKey, cancellationToken);
                return request;
            }, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return await ReadRequiredJsonAsync<TResponse>(response, cancellationToken);
        }

        private static async Task<TResponse> SendAsync<TResponse>(
            HttpClient client,
            HttpMethod method,
            Uri uri,
            string apiKey,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await client.SendWithRetryAsync(async () =>
            {
                var request = new HttpRequestMessage(method, uri);
                await request.AddApiSignatureAsync(apiKey, cancellationToken);
                return request;
            }, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return await ReadRequiredJsonAsync<TResponse>(response, cancellationToken);
        }

        private static async Task<T> ReadRequiredJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            T? result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            return result ?? throw new RemotePcException(response.StatusCode, "応答データが空です。");
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new RemotePcException(response.StatusCode, detail);
        }

        private static Uri BuildUri(string address, string path, bool forceDefaultPort = false)
        {
            string normalized = address.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "http://" + normalized;
            }

            var builder = new UriBuilder(normalized);
            if (forceDefaultPort)
            {
                builder.Port = 5000;
            }
            builder.Path = path;
            return builder.Uri;
        }
    }

    internal sealed class RemotePcException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public RemotePcException(HttpStatusCode statusCode, string detail)
            : base($"HTTP {(int)statusCode} ({statusCode}): {detail}")
        {
            StatusCode = statusCode;
        }
    }
}

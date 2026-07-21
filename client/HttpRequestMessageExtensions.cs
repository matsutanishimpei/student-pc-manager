using System;
using System.Net.Http;
using Share.Security;
using System.Threading;
using System.Threading.Tasks;

namespace client
{
    public static class HttpRequestMessageExtensions
    {
        public static async Task AddApiSignatureAsync(this HttpRequestMessage request, string apiKey, CancellationToken cancellationToken = default)
        {
            string contentHash = ApiSignature.EmptyContentHash;
            if (request.Content != null)
            {
                await using var stream = new System.IO.MemoryStream();
                await request.Content.CopyToAsync(stream, cancellationToken);
                stream.Position = 0;
                contentHash = await ApiSignature.ComputeHashAsync(stream, cancellationToken);
            }
            AddApiSignature(request, apiKey, contentHash);
        }

        public static void AddApiSignature(this HttpRequestMessage request, string apiKey, string contentHash, string? fileNameHash = null)
        {
            ApiKeyPolicy.Validate(apiKey);
            fileNameHash ??= ApiSignature.EmptyContentHash;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var method = request.Method.Method;
            var path = request.RequestUri?.LocalPath ?? "/";
            var nonce = Guid.NewGuid().ToString("N");

            var signature = ApiSignature.Generate(apiKey, timestamp, nonce, method, path, contentHash, fileNameHash);
            request.Headers.Add("X-API-TIMESTAMP", timestamp);
            request.Headers.Add("X-API-NONCE", nonce);
            request.Headers.Add("X-API-CONTENT-SHA256", contentHash);
            request.Headers.Add("X-API-FILENAME-SHA256", fileNameHash);
            request.Headers.Add("X-API-SIGNATURE", signature);
        }
    }
}

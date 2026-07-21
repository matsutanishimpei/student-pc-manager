using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Share.Security;
using System.IO;
using System.Security.Cryptography;

namespace Server.Middlewares
{
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string? _expectedApiKey;
        private const string ApiTimestampHeaderName = "X-API-TIMESTAMP";
        private const string ApiSignatureHeaderName = "X-API-SIGNATURE";
        private const string ApiNonceHeaderName = "X-API-NONCE";
        private const string ApiContentHashHeaderName = "X-API-CONTENT-SHA256";
        private const string ApiFileNameHashHeaderName = "X-API-FILENAME-SHA256";
        private readonly ApiNonceStore _nonceStore;

        public ApiKeyAuthMiddleware(RequestDelegate next, ServerApiKeyProvider apiKeyProvider, ApiNonceStore nonceStore)
        {
            _next = next;
            _expectedApiKey = apiKeyProvider.ApiKey;
            _nonceStore = nonceStore;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                if (string.IsNullOrWhiteSpace(_expectedApiKey))
                {
                    await WriteUnauthorizedAsync(context, "API authentication is not configured on the server.");
                    return;
                }

                if (!context.Request.Headers.TryGetValue(ApiTimestampHeaderName, out var extractedTimestamp) ||
                    !context.Request.Headers.TryGetValue(ApiSignatureHeaderName, out var extractedSignature) ||
                    !context.Request.Headers.TryGetValue(ApiNonceHeaderName, out var extractedNonce) ||
                    !context.Request.Headers.TryGetValue(ApiContentHashHeaderName, out var extractedContentHash) ||
                    !context.Request.Headers.TryGetValue(ApiFileNameHashHeaderName, out var extractedFileNameHash))
                {
                    await WriteUnauthorizedAsync(context, "API signature headers are missing.");
                    return;
                }

                string timestamp = extractedTimestamp.ToString();
                string signature = extractedSignature.ToString();
                string nonce = extractedNonce.ToString();
                string contentHash = extractedContentHash.ToString().ToLowerInvariant();
                string fileNameHash = extractedFileNameHash.ToString().ToLowerInvariant();
                string method = context.Request.Method;
                string path = context.Request.Path.Value ?? "/";

                if (!Guid.TryParseExact(nonce, "N", out _) ||
                    contentHash.Length != 64 ||
                    !contentHash.All(Uri.IsHexDigit) ||
                    fileNameHash.Length != 64 ||
                    !fileNameHash.All(Uri.IsHexDigit))
                {
                    await WriteUnauthorizedAsync(context, "The API signature headers are malformed.");
                    return;
                }

                if (!ApiSignature.Verify(_expectedApiKey, timestamp, nonce, method, path, contentHash, fileNameHash, signature))
                {
                    await WriteUnauthorizedAsync(context, "The API signature is invalid or expired.");
                    return;
                }

                if (!path.Equals("/api/upload", StringComparison.OrdinalIgnoreCase))
                {
                    context.Request.EnableBuffering();
                    string actualContentHash = await ApiSignature.ComputeHashAsync(context.Request.Body, context.RequestAborted);
                    context.Request.Body.Position = 0;
                    if (!CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.ASCII.GetBytes(actualContentHash),
                        System.Text.Encoding.ASCII.GetBytes(contentHash)))
                    {
                        await WriteUnauthorizedAsync(context, "The request content hash is invalid.");
                        return;
                    }
                }

                if (!long.TryParse(timestamp, out long requestTime) || !_nonceStore.TryUse(nonce, requestTime + 300))
                {
                    await WriteUnauthorizedAsync(context, "The request nonce has already been used.");
                    return;
                }
            }
            await _next(context);
        }

        private static Task WriteUnauthorizedAsync(HttpContext context, string detail)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = detail
            });
        }
    }
}

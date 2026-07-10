using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Share.Security;

namespace Server.Middlewares
{
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string? _expectedApiKey;
        private const string ApiTimestampHeaderName = "X-API-TIMESTAMP";
        private const string ApiSignatureHeaderName = "X-API-SIGNATURE";

        public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _expectedApiKey = configuration["ApiKey"];
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                if (string.IsNullOrWhiteSpace(_expectedApiKey))
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    await context.Response.WriteAsync("Unauthorized: API Key is not configured on the server.");
                    return;
                }

                if (!context.Request.Headers.TryGetValue(ApiTimestampHeaderName, out var extractedTimestamp) ||
                    !context.Request.Headers.TryGetValue(ApiSignatureHeaderName, out var extractedSignature))
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    await context.Response.WriteAsync("Unauthorized: Missing API signature headers.");
                    return;
                }

                string timestamp = extractedTimestamp.ToString();
                string signature = extractedSignature.ToString();
                string method = context.Request.Method;
                string path = context.Request.Path.Value ?? "/";

                if (!ApiSignature.Verify(_expectedApiKey, timestamp, method, path, signature))
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    await context.Response.WriteAsync("Unauthorized: Invalid API signature or timestamp expired.");
                    return;
                }
            }
            await _next(context);
        }
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace Server.Middlewares
{
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string? _expectedApiKey;
        private const string ApiKeyHeaderName = "X-API-KEY";

        public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _expectedApiKey = configuration["ApiKey"];
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                if (string.IsNullOrWhiteSpace(_expectedApiKey) ||
                    !context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey) ||
                    extractedApiKey != _expectedApiKey)
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    await context.Response.WriteAsync("Unauthorized: Invalid or missing API Key.");
                    return;
                }
            }
            await _next(context);
        }
    }
}

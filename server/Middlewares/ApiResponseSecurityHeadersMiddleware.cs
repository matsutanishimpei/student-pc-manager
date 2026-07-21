using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Server.Middlewares
{
    public sealed class ApiResponseSecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiResponseSecurityHeadersMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    return Task.CompletedTask;
                });
            }

            await _next(context);
        }
    }
}

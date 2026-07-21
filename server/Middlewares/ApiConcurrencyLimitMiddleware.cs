using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Middlewares
{
    public sealed class ApiConcurrencyLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly SemaphoreSlim _semaphore;

        public ApiConcurrencyLimitMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            int maxConcurrency = Math.Max(1, configuration.GetValue<int?>("MaxConcurrentApiRequests") ?? 10);
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await _next(context);
                return;
            }

            if (!await _semaphore.WaitAsync(0, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "1";
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                    Detail = "Too many API requests are running. Retry shortly."
                }, context.RequestAborted);
                return;
            }

            try
            {
                await _next(context);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}

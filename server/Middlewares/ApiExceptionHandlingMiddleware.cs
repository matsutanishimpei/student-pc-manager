using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Server.Services;
using System;
using System.Threading.Tasks;

namespace Server.Middlewares
{
    public sealed class ApiExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiExceptionHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client disconnected; there is no response left to send.
            }
            catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
            {
                Log.Write($"[Unhandled API Error] {ex}");

                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Internal Server Error",
                    Detail = "The request could not be completed."
                });
            }
        }
    }
}

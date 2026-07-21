using Microsoft.AspNetCore.Http;
using Server.Middlewares;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Tests;

public class ApiExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task UnhandledApiException_ReturnsGenericProblemDetails()
    {
        var middleware = new ApiExceptionHandlingMiddleware(_ =>
            throw new InvalidOperationException("sensitive internal detail"));
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/exec";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        string json = document.RootElement.ToString();
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("The request could not be completed", json);
        Assert.DoesNotContain("sensitive internal detail", json);
    }
}

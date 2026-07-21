using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Server.Middlewares;
using System.IO;
using Xunit;

namespace Tests;

public class ApiConcurrencyLimitMiddlewareTests
{
    [Fact]
    public async Task ApiRequest_OverConfiguredLimit_Returns429()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MaxConcurrentApiRequests"] = "2"
            })
            .Build();
        var releaseRequests = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredRequests = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int enteredCount = 0;

        var middleware = new ApiConcurrencyLimitMiddleware(async _ =>
        {
            if (Interlocked.Increment(ref enteredCount) == 2)
            {
                enteredRequests.SetResult();
            }
            await releaseRequests.Task;
        }, configuration);

        var first = InvokeAsync(middleware, "/api/exec");
        var second = InvokeAsync(middleware, "/api/info");
        await enteredRequests.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rejectedContext = new DefaultHttpContext();
        rejectedContext.Request.Path = "/api/processes";
        rejectedContext.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(rejectedContext);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejectedContext.Response.StatusCode);
        Assert.Equal("1", rejectedContext.Response.Headers.RetryAfter);

        releaseRequests.SetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task NonApiRequest_DoesNotUseLimit()
    {
        var configuration = new ConfigurationBuilder().Build();
        bool nextCalled = false;
        var middleware = new ApiConcurrencyLimitMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configuration);
        var context = new DefaultHttpContext();
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static Task InvokeAsync(ApiConcurrencyLimitMiddleware middleware, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return middleware.InvokeAsync(context);
    }
}

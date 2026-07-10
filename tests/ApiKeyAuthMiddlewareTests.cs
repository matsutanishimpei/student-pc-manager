using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Server.Middlewares;
using Xunit;

namespace Tests;

public class ApiKeyAuthMiddlewareTests
{
    private const string ValidApiKey = "test-api-key-12345";

    private static IConfiguration BuildConfig(string? apiKey = null)
    {
        var dict = new Dictionary<string, string?>();
        if (apiKey != null)
            dict["ApiKey"] = apiKey;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    // Helper to run middleware and check results
    private static async Task<(int statusCode, bool nextCalled)> RunMiddleware(
        string? configApiKey, string requestPath, string? headerApiKey)
    {
        var state = new TestState();
        var config = BuildConfig(configApiKey);
        var middleware = new ApiKeyAuthMiddleware(
            next: _ => { state.NextCalled = true; return Task.CompletedTask; },
            configuration: config);

        var context = new DefaultHttpContext();
        context.Request.Path = requestPath;
        if (headerApiKey != null)
            context.Request.Headers["X-API-KEY"] = headerApiKey;

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, state.NextCalled);
    }

    private class TestState { public bool NextCalled { get; set; } }

    // --- ★★★ 認証が正しく動作する基本テスト ---

    [Fact]
    public async Task ValidApiKey_PassesThrough()
    {
        var (statusCode, nextCalled) = await RunMiddleware(ValidApiKey, "/api/exec", ValidApiKey);

        Assert.True(nextCalled, "Next delegate should have been called");
        Assert.Equal(200, statusCode);
    }

    [Fact]
    public async Task MissingApiKey_Returns401()
    {
        var (statusCode, nextCalled) = await RunMiddleware(ValidApiKey, "/api/exec", headerApiKey: null);

        Assert.False(nextCalled, "Next delegate should NOT have been called");
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task InvalidApiKey_Returns401()
    {
        var (statusCode, nextCalled) = await RunMiddleware(ValidApiKey, "/api/exec", "wrong-key");

        Assert.False(nextCalled);
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task NonApiPath_SkipsAuth()
    {
        // Paths not starting with /api should bypass authentication
        var (statusCode, nextCalled) = await RunMiddleware(ValidApiKey, "/health", headerApiKey: null);

        Assert.True(nextCalled, "Non-API paths should skip authentication");
        Assert.Equal(200, statusCode);
    }

    [Fact]
    public async Task RootPath_SkipsAuth()
    {
        var (statusCode, nextCalled) = await RunMiddleware(ValidApiKey, "/", headerApiKey: null);

        Assert.True(nextCalled);
        Assert.Equal(200, statusCode);
    }

    [Fact]
    public async Task MissingConfigApiKey_Returns401_EvenWithKeyInHeader()
    {
        // When no ApiKey is configured, any request to /api should be rejected
        var (statusCode, nextCalled) = await RunMiddleware(
            configApiKey: null,
            requestPath: "/api/info",
            headerApiKey: "some-key");

        Assert.False(nextCalled, "Should NOT authenticate when config is missing ApiKey");
        Assert.Equal(401, statusCode);
    }

    [Theory]
    [InlineData("/api/exec")]
    [InlineData("/api/screenshot")]
    [InlineData("/api/processes")]
    [InlineData("/api/info")]
    [InlineData("/api/mac")]
    [InlineData("/api/upload")]
    [InlineData("/api/activeapp")]
    public async Task AllApiEndpoints_RequireAuth(string path)
    {
        var (statusCode, nextCalled) = await RunMiddleware(ValidApiKey, path, headerApiKey: null);

        Assert.False(nextCalled, $"Path {path} should require authentication");
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task ApiKey_IsCaseSensitive()
    {
        var (statusCode, nextCalled) = await RunMiddleware(ValidApiKey, "/api/exec", ValidApiKey.ToUpper());

        Assert.False(nextCalled, "Auth should fail when API key case does not match");
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task EmptyConfigApiKey_Returns401_EvenWithEmptyHeader()
    {
        var (statusCode, nextCalled) = await RunMiddleware("", "/api/exec", "");

        Assert.False(nextCalled, "Auth should fail when configured ApiKey is empty");
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task WhitespaceConfigApiKey_Returns401_EvenWithWhitespaceHeader()
    {
        var (statusCode, nextCalled) = await RunMiddleware("   ", "/api/exec", "   ");

        Assert.False(nextCalled, "Auth should fail when configured ApiKey is whitespace only");
        Assert.Equal(401, statusCode);
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Server.Middlewares;
using Share.Security;
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
        string? configApiKey, string requestPath, string method, string? headerTimestamp, string? headerSignature)
    {
        var state = new TestState();
        var config = BuildConfig(configApiKey);
        var middleware = new ApiKeyAuthMiddleware(
            next: _ => { state.NextCalled = true; return Task.CompletedTask; },
            configuration: config);

        var context = new DefaultHttpContext();
        context.Request.Path = requestPath;
        context.Request.Method = method;
        
        if (headerTimestamp != null)
            context.Request.Headers["X-API-TIMESTAMP"] = headerTimestamp;
        if (headerSignature != null)
            context.Request.Headers["X-API-SIGNATURE"] = headerSignature;

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, state.NextCalled);
    }

    private static async Task<(int statusCode, bool nextCalled)> RunMiddlewareWithValidSignature(
        string? configApiKey, string requestPath, string method, string apiKeyToSign, long timeOffsetSeconds = 0)
    {
        string timestamp = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + timeOffsetSeconds).ToString();
        string signature = ApiSignature.Generate(apiKeyToSign, timestamp, method, requestPath);
        return await RunMiddleware(configApiKey, requestPath, method, timestamp, signature);
    }

    private class TestState { public bool NextCalled { get; set; } }

    // --- ★★★ 認証が正しく動作する基本テスト ---

    [Fact]
    public async Task ValidSignature_PassesThrough()
    {
        var (statusCode, nextCalled) = await RunMiddlewareWithValidSignature(
            ValidApiKey, "/api/exec", "POST", ValidApiKey);

        Assert.True(nextCalled, "Next delegate should have been called");
        Assert.Equal(200, statusCode);
    }

    [Fact]
    public async Task MissingHeaders_Returns401()
    {
        var (statusCode, nextCalled) = await RunMiddleware(
            ValidApiKey, "/api/exec", "POST", headerTimestamp: null, headerSignature: null);

        Assert.False(nextCalled, "Next delegate should NOT have been called");
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task InvalidSignature_Returns401()
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var (statusCode, nextCalled) = await RunMiddleware(
            ValidApiKey, "/api/exec", "POST", timestamp, "invalid-signature-value-here");

        Assert.False(nextCalled);
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task NonApiPath_SkipsAuth()
    {
        // Paths not starting with /api should bypass authentication
        var (statusCode, nextCalled) = await RunMiddleware(
            ValidApiKey, "/health", "GET", headerTimestamp: null, headerSignature: null);

        Assert.True(nextCalled, "Non-API paths should skip authentication");
        Assert.Equal(200, statusCode);
    }

    [Fact]
    public async Task RootPath_SkipsAuth()
    {
        var (statusCode, nextCalled) = await RunMiddleware(
            ValidApiKey, "/", "GET", headerTimestamp: null, headerSignature: null);

        Assert.True(nextCalled);
        Assert.Equal(200, statusCode);
    }

    [Fact]
    public async Task MissingConfigApiKey_Returns401_EvenWithValidSignature()
    {
        // When no ApiKey is configured, any request to /api should be rejected
        var (statusCode, nextCalled) = await RunMiddlewareWithValidSignature(
            configApiKey: null,
            requestPath: "/api/info",
            method: "GET",
            apiKeyToSign: ValidApiKey);

        Assert.False(nextCalled, "Should NOT authenticate when config is missing ApiKey");
        Assert.Equal(401, statusCode);
    }

    [Theory]
    [InlineData("/api/exec", "POST")]
    [InlineData("/api/screenshot", "GET")]
    [InlineData("/api/processes", "GET")]
    [InlineData("/api/info", "GET")]
    [InlineData("/api/mac", "GET")]
    [InlineData("/api/upload", "POST")]
    [InlineData("/api/activeapp", "GET")]
    public async Task AllApiEndpoints_RequireAuth(string path, string method)
    {
        var (statusCode, nextCalled) = await RunMiddleware(
            ValidApiKey, path, method, headerTimestamp: null, headerSignature: null);

        Assert.False(nextCalled, $"Path {path} should require authentication");
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task ApiKey_IsCaseSensitive_ForSignature()
    {
        var (statusCode, nextCalled) = await RunMiddlewareWithValidSignature(
            ValidApiKey, "/api/exec", "POST", ValidApiKey.ToUpper());

        Assert.False(nextCalled, "Auth should fail when API key case does not match during signing");
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task EmptyConfigApiKey_Returns401_EvenWithEmptyKeySignature()
    {
        var (statusCode, nextCalled) = await RunMiddlewareWithValidSignature(
            "", "/api/exec", "POST", "");

        Assert.False(nextCalled, "Auth should fail when configured ApiKey is empty");
        Assert.Equal(401, statusCode);
    }

    // --- ★★★ 新しいセキュリティ機能（タイムスタンプと改ざん）のテスト ---

    [Fact]
    public async Task ExpiredTimestamp_WithinFutureOrPastLimit_Returns401()
    {
        // 5分 (300秒) を超える過去のタイムスタンプ
        var (statusCodePast, nextCalledPast) = await RunMiddlewareWithValidSignature(
            ValidApiKey, "/api/exec", "POST", ValidApiKey, timeOffsetSeconds: -301);

        // 5分 (300秒) を超える未来のタイムスタンプ
        var (statusCodeFuture, nextCalledFuture) = await RunMiddlewareWithValidSignature(
            ValidApiKey, "/api/exec", "POST", ValidApiKey, timeOffsetSeconds: 301);

        Assert.False(nextCalledPast, "Should reject requests older than 5 minutes");
        Assert.Equal(401, statusCodePast);

        Assert.False(nextCalledFuture, "Should reject requests newer than 5 minutes into the future");
        Assert.Equal(401, statusCodeFuture);
    }

    [Fact]
    public async Task ValidTimestamp_JustWithinLimit_PassesThrough()
    {
        // 4分50秒 (290秒) 過去のタイムスタンプ
        var (statusCodePast, nextCalledPast) = await RunMiddlewareWithValidSignature(
            ValidApiKey, "/api/exec", "POST", ValidApiKey, timeOffsetSeconds: -290);

        Assert.True(nextCalledPast, "Should allow requests within the 5-minute window");
        Assert.Equal(200, statusCodePast);
    }

    [Fact]
    public async Task PathTampering_Returns401()
    {
        // /api/exec のパスで署名を作成
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string signature = ApiSignature.Generate(ValidApiKey, timestamp, "POST", "/api/exec");

        // しかしリクエスト自体は別のエンドポイント (/api/info) に対して送信された場合、改ざんとみなされるべき
        var (statusCode, nextCalled) = await RunMiddleware(
            ValidApiKey, "/api/info", "POST", timestamp, signature);

        Assert.False(nextCalled, "Should reject signature if the request path has been tampered with");
        Assert.Equal(401, statusCode);
    }

    [Fact]
    public async Task MethodTampering_Returns401()
    {
        // GET で署名を作成
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string signature = ApiSignature.Generate(ValidApiKey, timestamp, "GET", "/api/exec");

        // POST でリクエストが送信された場合
        var (statusCode, nextCalled) = await RunMiddleware(
            ValidApiKey, "/api/exec", "POST", timestamp, signature);

        Assert.False(nextCalled, "Should reject signature if the request HTTP method has been tampered with");
        Assert.Equal(401, statusCode);
    }
}

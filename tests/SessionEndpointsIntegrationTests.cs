using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Server.Endpoints;
using Server.Middlewares;
using Server.Services;
using Share.Models;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tests;

/// <summary>
/// A mock IInteractiveTaskExecutor for integration tests that avoids
/// real PowerShell/Pipe/CreateProcessAsUser calls.
/// </summary>
public class MockTaskExecutor : IInteractiveTaskExecutor
{
    public CommandResponse? NextCommandResponse { get; set; }
    public byte[]? NextScreenshotBytes { get; set; }
    public string NextProcessesJson { get; set; } = "[]";
    public string NextActiveApp { get; set; } = "";

    public Task<CommandResponse> ExecuteCommandAsync(string command, bool runInUserSession)
    {
        return Task.FromResult(NextCommandResponse ?? new CommandResponse
        {
            ExitCode = 0,
            Stdout = $"Mock output for: {command}"
        });
    }

    public Task<byte[]?> GetScreenshotAsync()
    {
        return Task.FromResult(NextScreenshotBytes);
    }

    public Task<string> GetProcessesJsonAsync()
    {
        return Task.FromResult(NextProcessesJson);
    }

    public Task<string> GetActiveAppAsync()
    {
        return Task.FromResult(NextActiveApp);
    }
}

/// <summary>
/// Custom WebApplicationFactory that sets up a minimal test server
/// with the API key middleware and session endpoints, but with a mock executor.
/// </summary>
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    public MockTaskExecutor MockExecutor { get; } = new();
    public string TestApiKey { get; } = "test-integration-key";
    private string? _testUploadDir;
    public string TestUploadDir => _testUploadDir ?? throw new InvalidOperationException("Upload directory not initialized");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _testUploadDir = Path.Combine(Path.GetTempPath(), "sendCMD_test_uploads_" + Guid.NewGuid().ToString("N"));

        builder.ConfigureServices(services =>
        {
            // Remove the real executor registration and replace with mock
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IInteractiveTaskExecutor));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddSingleton<IInteractiveTaskExecutor>(MockExecutor);
        });

        builder.UseSetting("ApiKey", TestApiKey);
        builder.UseSetting("UploadDirectory", _testUploadDir);

        // Prevent the real HelperLifecycleService from running
        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _testUploadDir != null && Directory.Exists(_testUploadDir))
        {
            try
            {
                Directory.Delete(_testUploadDir, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}

public class ApiSignatureHandler : DelegatingHandler
{
    private readonly string _apiKey;

    public ApiSignatureHandler(string apiKey)
    {
        _apiKey = apiKey;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var method = request.Method.Method;
        var path = request.RequestUri?.LocalPath ?? "/";

        var signature = Share.Security.ApiSignature.Generate(_apiKey, timestamp, method, path);
        request.Headers.Add("X-API-TIMESTAMP", timestamp);
        request.Headers.Add("X-API-SIGNATURE", signature);

        return base.SendAsync(request, cancellationToken);
    }
}

public class SessionEndpointsIntegrationTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;

    public SessionEndpointsIntegrationTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateDefaultClient(new ApiSignatureHandler(factory.TestApiKey));
    }

    private HttpClient CreateUnauthenticatedClient()
    {
        return _factory.CreateClient();
    }

    // --- ヘルスチェック ---

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var client = CreateUnauthenticatedClient(); // No auth needed for /
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal("sendCMD Server is running.", body);
    }

    // --- 認証テスト ---

    [Fact]
    public async Task ExecEndpoint_NoApiKey_Returns401()
    {
        var client = CreateUnauthenticatedClient();
        var content = JsonContent.Create(new { Command = "echo hello" });
        var response = await client.PostAsync("/api/exec", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InfoEndpoint_NoApiKey_Returns401()
    {
        var client = CreateUnauthenticatedClient();
        var response = await client.GetAsync("/api/info");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- /api/exec ---

    [Fact]
    public async Task ExecEndpoint_EmptyCommand_ReturnsBadRequest()
    {
        var content = JsonContent.Create(new { Command = "", RunInUserSession = false });
        var response = await _client.PostAsync("/api/exec", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExecEndpoint_WhitespaceCommand_ReturnsBadRequest()
    {
        var content = JsonContent.Create(new { Command = "   ", RunInUserSession = false });
        var response = await _client.PostAsync("/api/exec", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExecEndpoint_ValidCommand_ReturnsOk()
    {
        _factory.MockExecutor.NextCommandResponse = new CommandResponse
        {
            ExitCode = 0,
            Stdout = "hello world"
        };

        var content = JsonContent.Create(new { Command = "echo hello", RunInUserSession = false });
        var response = await _client.PostAsync("/api/exec", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CommandResponse>();
        Assert.NotNull(result);
        Assert.Equal(0, result!.ExitCode);
        Assert.Equal("hello world", result.Stdout);
    }

    // --- /api/info ---

    [Fact]
    public async Task InfoEndpoint_ReturnsMachineName()
    {
        var response = await _client.GetAsync("/api/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<ServerInfoResponse>();
        Assert.NotNull(json);
        Assert.Equal(Environment.MachineName, json!.MachineName);
    }

    // --- /api/screenshot ---

    [Fact]
    public async Task ScreenshotEndpoint_NoData_Returns504()
    {
        _factory.MockExecutor.NextScreenshotBytes = null;

        var response = await _client.GetAsync("/api/screenshot");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task ScreenshotEndpoint_WithData_ReturnsJpeg()
    {
        // Create a minimal fake JPEG (just the header magic bytes for testing)
        _factory.MockExecutor.NextScreenshotBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };

        var response = await _client.GetAsync("/api/screenshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    // --- /api/processes ---

    [Fact]
    public async Task ProcessesEndpoint_ReturnsJson()
    {
        _factory.MockExecutor.NextProcessesJson = "[{\"ProcessName\":\"chrome\",\"Id\":1,\"MainWindowTitle\":\"Google\"}]";

        var response = await _client.GetAsync("/api/processes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("chrome", body);
    }

    [Fact]
    public async Task ProcessesEndpoint_EmptyList_ReturnsEmptyArray()
    {
        _factory.MockExecutor.NextProcessesJson = "[]";

        var response = await _client.GetAsync("/api/processes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", body);
    }

    // --- /api/activeapp ---

    [Fact]
    public async Task ActiveAppEndpoint_ReturnsResult()
    {
        _factory.MockExecutor.NextActiveApp = "Google Chrome, Notepad";

        var response = await _client.GetAsync("/api/activeapp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Google Chrome, Notepad", body);
    }

    // --- /api/upload ---

    [Fact]
    public async Task UploadEndpoint_NonMultipartForm_ReturnsBadRequest()
    {
        var content = new StringContent("not-multipart");
        var response = await _client.PostAsync("/api/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadEndpoint_NoFile_ReturnsBadRequest()
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("some-data"), "not-the-required-file-param");
        var response = await _client.PostAsync("/api/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadEndpoint_ValidFile_SavesFileAndReturnsOk()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake file content"));
        content.Add(fileContent, "file", "test_file.txt");

        var response = await _client.PostAsync("/api/upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.NotNull(result);
        Assert.True(File.Exists(result!.FilePath));
        Assert.Equal("test_file.txt", Path.GetFileName(result.FilePath));

        // Content check
        string fileText = await File.ReadAllTextAsync(result.FilePath);
        Assert.Equal("fake file content", fileText);
    }

    // --- /api/mac ---

    [Fact]
    public async Task MacEndpoint_ReturnsOkOrNotFound()
    {
        var response = await _client.GetAsync("/api/mac");
        
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound,
            $"Expected OK or NotFound, but got {response.StatusCode}");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadFromJsonAsync<MacAddressResponse>();
            Assert.NotNull(json);
            Assert.False(string.IsNullOrWhiteSpace(json!.MacAddress));
            Assert.Matches(@"^[0-9A-FA-f]{2}(-[0-9A-FA-f]{2}){5}$", json.MacAddress);
        }
    }

    private class UploadResponse
    {
        public string FilePath { get; set; } = "";
    }
}

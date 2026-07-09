using Share.Models;
using System.Text.Json;
using Xunit;

namespace Tests;

public class ModelsTests
{
    // --- CommandRequest ---

    [Fact]
    public void CommandRequest_DefaultValues()
    {
        var request = new CommandRequest();

        Assert.Equal(string.Empty, request.Command);
        Assert.False(request.RunInUserSession);
    }

    [Fact]
    public void CommandRequest_SetValues()
    {
        var request = new CommandRequest
        {
            Command = "Get-Process",
            RunInUserSession = true
        };

        Assert.Equal("Get-Process", request.Command);
        Assert.True(request.RunInUserSession);
    }

    [Fact]
    public void CommandRequest_SerializeDeserialize()
    {
        var original = new CommandRequest
        {
            Command = "echo hello",
            RunInUserSession = true
        };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<CommandRequest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Command, deserialized!.Command);
        Assert.Equal(original.RunInUserSession, deserialized.RunInUserSession);
    }

    // --- CommandResponse ---

    [Fact]
    public void CommandResponse_DefaultValues()
    {
        var response = new CommandResponse();

        Assert.Equal(0, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Equal(string.Empty, response.Stderr);
    }

    [Fact]
    public void CommandResponse_SerializeDeserialize()
    {
        var original = new CommandResponse
        {
            ExitCode = 1,
            Stdout = "some output",
            Stderr = "some error"
        };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<CommandResponse>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ExitCode, deserialized!.ExitCode);
        Assert.Equal(original.Stdout, deserialized.Stdout);
        Assert.Equal(original.Stderr, deserialized.Stderr);
    }

    [Fact]
    public void CommandResponse_ErrorCase()
    {
        var response = new CommandResponse
        {
            ExitCode = -1,
            Stderr = "Access denied"
        };

        Assert.Equal(-1, response.ExitCode);
        Assert.Equal("Access denied", response.Stderr);
        Assert.Equal(string.Empty, response.Stdout);
    }

    // --- ServerInfoResponse ---

    [Fact]
    public void ServerInfoResponse_DefaultValues()
    {
        var info = new ServerInfoResponse();
        Assert.Equal(string.Empty, info.MachineName);
    }

    [Fact]
    public void ServerInfoResponse_SerializeDeserialize()
    {
        var original = new ServerInfoResponse { MachineName = "PC-STUDENT-01" };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ServerInfoResponse>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("PC-STUDENT-01", deserialized!.MachineName);
    }

    // --- MacAddressResponse ---

    [Fact]
    public void MacAddressResponse_DefaultValues()
    {
        var mac = new MacAddressResponse();
        Assert.Equal(string.Empty, mac.MacAddress);
    }

    [Fact]
    public void MacAddressResponse_SerializeDeserialize()
    {
        var original = new MacAddressResponse { MacAddress = "AA-BB-CC-DD-EE-FF" };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<MacAddressResponse>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("AA-BB-CC-DD-EE-FF", deserialized!.MacAddress);
    }

    // --- JSON互換性テスト: クライアントが送るJSONをサーバーが正しく読めるか ---

    [Fact]
    public void CommandRequest_DeserializeFromClientFormat()
    {
        // Simulates the JSON that the WPF client sends
        string clientJson = """{"Command":"taskkill /f /t /pid 1234","RunInUserSession":true}""";

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var request = JsonSerializer.Deserialize<CommandRequest>(clientJson, options);

        Assert.NotNull(request);
        Assert.Equal("taskkill /f /t /pid 1234", request!.Command);
        Assert.True(request.RunInUserSession);
    }

    [Fact]
    public void CommandRequest_DeserializeWithMissingFields()
    {
        // Client sends minimal JSON (only Command)
        string minimalJson = """{"Command":"echo hello"}""";

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var request = JsonSerializer.Deserialize<CommandRequest>(minimalJson, options);

        Assert.NotNull(request);
        Assert.Equal("echo hello", request!.Command);
        Assert.False(request.RunInUserSession); // default
    }
}

using Server.Services;
using Xunit;

namespace Tests;

public class AdaptiveSessionExecutorTests
{
    [Fact]
    public void CreateCommandAuditEntry_DoesNotContainCommandText()
    {
        const string sensitiveCommand = "Connect-Service -Password super-secret-value";

        string entry = AdaptiveSessionExecutor.CreateCommandAuditEntry(sensitiveCommand, true);

        Assert.DoesNotContain(sensitiveCommand, entry);
        Assert.DoesNotContain("super-secret-value", entry);
        Assert.Contains($"Length: {sensitiveCommand.Length}", entry);
        Assert.Contains("Fingerprint:", entry);
    }

    [Fact]
    public async Task ExecutePowerShellAsync_CapturesStandardOutputAndError()
    {
        var result = await AdaptiveSessionExecutor.ExecutePowerShellAsync(
            "Write-Output 'stdout-value'; [Console]::Error.WriteLine('stderr-value')",
            TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("stdout-value", result.Stdout);
        Assert.Contains("stderr-value", result.Stderr);
    }

    [Fact]
    public async Task ExecutePowerShellAsync_StopsAfterTimeout()
    {
        var startedAt = DateTime.UtcNow;

        var result = await AdaptiveSessionExecutor.ExecutePowerShellAsync(
            "Start-Sleep -Seconds 10",
            TimeSpan.FromMilliseconds(200));

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timed out", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecutePowerShellAsync_ClientCancellationStopsProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AdaptiveSessionExecutor.ExecutePowerShellAsync(
                "Start-Sleep -Seconds 10",
                TimeSpan.FromSeconds(30),
                cancellation.Token));
    }
}

using Microsoft.Extensions.Configuration;
using Server;
using System.IO;
using Xunit;

namespace Tests;

public sealed class ServerApiKeyStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sendcmd-key-{Guid.NewGuid():N}");

    [Fact]
    public void SaveProtectedApiKey_RemovesPlaintextAndCanBeDecrypted()
    {
        Directory.CreateDirectory(_directory);
        string settingsPath = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(settingsPath, "{\"ApiKey\":\"\",\"ProtectedApiKey\":\"\"}");
        const string apiKey = "shared-classroom-key-123";

        ServerApiKeyStore.SaveProtectedApiKey(settingsPath, apiKey, restrictFileAccess: false);

        string json = File.ReadAllText(settingsPath);
        Assert.DoesNotContain(apiKey, json);
        var configuration = new ConfigurationBuilder().AddJsonFile(settingsPath).Build();
        Assert.Equal(apiKey, ServerApiKeyStore.LoadOrMigrate(configuration, _directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

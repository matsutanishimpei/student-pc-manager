using Server.Middlewares;
using System.IO;
using Xunit;

namespace Tests;

public sealed class ApiNonceStorePersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sendcmd-nonce-{Guid.NewGuid():N}");

    [Fact]
    public void UsedNonce_RemainsRejectedAfterStoreRecreation()
    {
        string path = Path.Combine(_directory, "nonce-cache.json");
        var firstStore = new ApiNonceStore(path, restrictFileAccess: false);
        string nonce = Guid.NewGuid().ToString("N");
        long expiry = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();

        Assert.True(firstStore.TryUse(nonce, expiry));

        var recreatedStore = new ApiNonceStore(path, restrictFileAccess: false);
        Assert.False(recreatedStore.TryUse(nonce, expiry));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

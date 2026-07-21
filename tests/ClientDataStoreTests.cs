using System;
using System.IO;
using System.Text;
using client;
using Xunit;

namespace Tests
{
    public sealed class ClientDataStoreTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sendcmd-tests-{Guid.NewGuid():N}");

        [Fact]
        public void PcList_RoundTrip_PreservesJapaneseTextAsUtf8()
        {
            Directory.CreateDirectory(_directory);
            var store = new ClientDataStore(_directory);
            var items = new[]
            {
                new PcItem
                {
                    IpAddress = "192.168.1.10:5000",
                    MachineName = "教室PC-01",
                    StudentName = "山田太郎",
                    Group = "1組"
                }
            };

            store.SavePcList(items);
            var loaded = store.LoadPcList();

            var item = Assert.Single(loaded!);
            Assert.Equal("教室PC-01", item.MachineName);
            Assert.Equal("山田太郎", item.StudentName);
            string json = File.ReadAllText(Path.Combine(_directory, "pcs.json"), Encoding.UTF8);
            Assert.Contains("山田太郎", json);
        }

        [Fact]
        public void PcList_EmptyList_RoundTripsAsEmptyList()
        {
            Directory.CreateDirectory(_directory);
            var store = new ClientDataStore(_directory);

            store.SavePcList(Array.Empty<PcItem>());

            Assert.Empty(store.LoadPcList()!);
        }

        [Fact]
        public void Config_RoundTrip_PreservesApiKey()
        {
            Directory.CreateDirectory(_directory);
            var store = new ClientDataStore(_directory);

            store.SaveConfig(new ClientConfig { ApiKey = "classroom-key" });

            Assert.Equal("classroom-key", store.LoadConfig()!.ApiKey);
            string persisted = File.ReadAllText(Path.Combine(_directory, "config.json"), Encoding.UTF8);
            Assert.DoesNotContain("classroom-key", persisted);
            Assert.Contains("ProtectedApiKey", persisted);
        }

        [Fact]
        public void Config_LegacyPlaintext_IsMigratedToProtectedValue()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "config.json"), "{\"ApiKey\":\"legacy-classroom-key\"}", Encoding.UTF8);
            var store = new ClientDataStore(_directory);

            Assert.Equal("legacy-classroom-key", store.LoadConfig()!.ApiKey);

            string migrated = File.ReadAllText(Path.Combine(_directory, "config.json"), Encoding.UTF8);
            Assert.DoesNotContain("legacy-classroom-key", migrated);
            Assert.Contains("ProtectedApiKey", migrated);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
    }
}

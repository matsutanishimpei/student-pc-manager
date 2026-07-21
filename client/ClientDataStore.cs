using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Security.Cryptography;
using Share.Security;

namespace client
{
    internal sealed class ClientDataStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private readonly string _pcListPath;
        private readonly string _configPath;

        public ClientDataStore(string baseDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
            _pcListPath = Path.Combine(baseDirectory, "pcs.json");
            _configPath = Path.Combine(baseDirectory, "config.json");
        }

        public IReadOnlyList<PcItem>? LoadPcList()
        {
            if (!File.Exists(_pcListPath))
            {
                return null;
            }

            string json = File.ReadAllText(_pcListPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<PcItem>>(json) ?? new List<PcItem>();
        }

        public void SavePcList(IEnumerable<PcItem> pcList)
        {
            ArgumentNullException.ThrowIfNull(pcList);
            WriteJson(_pcListPath, pcList);
        }

        public ClientConfig? LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                return null;
            }

            string json = File.ReadAllText(_configPath, Encoding.UTF8);
            var persisted = JsonSerializer.Deserialize<PersistedClientConfig>(json);
            if (persisted == null) return null;

            if (!string.IsNullOrEmpty(persisted.ProtectedApiKey))
            {
                byte[] encrypted = Convert.FromBase64String(persisted.ProtectedApiKey);
                byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return new ClientConfig { ApiKey = Encoding.UTF8.GetString(clear) };
            }

            var migrated = new ClientConfig { ApiKey = persisted.ApiKey ?? string.Empty };
            SaveConfig(migrated);
            return migrated;
        }

        public void SaveConfig(ClientConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            byte[] clear = Encoding.UTF8.GetBytes(config.ApiKey);
            byte[] encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            WriteJson(_configPath, new PersistedClientConfig
            {
                ProtectedApiKey = Convert.ToBase64String(encrypted)
            });
        }

        private static void WriteJson<T>(string path, T value)
        {
            string json = JsonSerializer.Serialize(value, JsonOptions);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
            if (Path.GetFileName(path).Equals("config.json", StringComparison.OrdinalIgnoreCase))
            {
                WindowsFileSecurity.RestrictToAdministratorsAndSystem(path, includeCurrentUser: true);
            }
        }

        private sealed class PersistedClientConfig
        {
            public string? ApiKey { get; set; }
            public string? ProtectedApiKey { get; set; }
        }
    }
}

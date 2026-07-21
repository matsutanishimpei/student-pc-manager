using Microsoft.Extensions.Configuration;
using Share.Security;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.Versioning;

namespace Server
{
    public sealed class ServerApiKeyProvider
    {
        public string ApiKey { get; }

        public ServerApiKeyProvider(string apiKey) => ApiKey = apiKey;
    }

    internal static class ServerApiKeyStore
    {
        [SupportedOSPlatform("windows")]
        public static string LoadOrMigrate(IConfiguration configuration, string contentRootPath)
        {
            string? protectedApiKey = configuration["ProtectedApiKey"];
            if (!string.IsNullOrWhiteSpace(protectedApiKey))
            {
                byte[] encrypted = Convert.FromBase64String(protectedApiKey);
                byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(clear);
            }

            string apiKey = configuration["ApiKey"] ?? string.Empty;
            ApiKeyPolicy.Validate(apiKey, configuration.GetValue<int?>("MinimumApiKeyLength") ?? ApiKeyPolicy.DefaultMinimumLength);
            SaveProtectedApiKey(Path.Combine(contentRootPath, "appsettings.json"), apiKey);
            return apiKey;
        }

        [SupportedOSPlatform("windows")]
        public static void SaveProtectedApiKey(string appSettingsPath, string apiKey, bool restrictFileAccess = true)
        {
            ApiKeyPolicy.Validate(apiKey);
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.LocalMachine);
            JsonObject root = JsonNode.Parse(File.ReadAllText(appSettingsPath, Encoding.UTF8))?.AsObject()
                ?? throw new InvalidDataException("appsettings.json is invalid.");
            root["ApiKey"] = string.Empty;
            root["ProtectedApiKey"] = Convert.ToBase64String(encrypted);

            string temporaryPath = appSettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporaryPath, appSettingsPath, overwrite: true);
            if (restrictFileAccess)
            {
                WindowsFileSecurity.RestrictToAdministratorsAndSystem(appSettingsPath, includeCurrentUser: false);
            }
        }
    }
}

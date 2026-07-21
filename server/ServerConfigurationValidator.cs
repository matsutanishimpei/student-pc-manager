using Microsoft.Extensions.Configuration;
using System;

namespace Server
{
    internal static class ServerConfigurationValidator
    {
        public static void Validate(IConfiguration configuration)
        {
            RequirePositive(configuration, "MaxUploadBytes", 524288000);
            RequirePositive(configuration, "CommandTimeoutSeconds", 600);
            RequirePositive(configuration, "MaxConcurrentApiRequests", 10);

            long minimumFreeSpace = configuration.GetValue<long?>("MinimumFreeSpaceBytesAfterUpload") ?? 67108864;
            if (minimumFreeSpace < 0)
            {
                throw new InvalidOperationException("MinimumFreeSpaceBytesAfterUpload must not be negative.");
            }
        }

        private static void RequirePositive(IConfiguration configuration, string key, long defaultValue)
        {
            long value = configuration.GetValue<long?>(key) ?? defaultValue;
            if (value <= 0)
            {
                throw new InvalidOperationException($"{key} must be greater than zero.");
            }
        }
    }
}

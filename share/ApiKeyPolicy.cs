using System;

namespace Share.Security
{
    public static class ApiKeyPolicy
    {
        public const int DefaultMinimumLength = 16;

        public static void Validate(string? apiKey, int minimumLength = DefaultMinimumLength)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < minimumLength)
            {
                throw new ArgumentException($"API key must be at least {minimumLength} characters.", nameof(apiKey));
            }
        }
    }
}

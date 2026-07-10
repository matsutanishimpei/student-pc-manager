using System;
using System.Net.Http;
using Share.Security;

namespace client
{
    public static class HttpRequestMessageExtensions
    {
        public static void AddApiSignature(this HttpRequestMessage request, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Add("X-API-TIMESTAMP", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                request.Headers.Add("X-API-SIGNATURE", string.Empty);
                return;
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var method = request.Method.Method;
            var path = request.RequestUri?.LocalPath ?? "/";

            var signature = ApiSignature.Generate(apiKey, timestamp, method, path);
            request.Headers.Add("X-API-TIMESTAMP", timestamp);
            request.Headers.Add("X-API-SIGNATURE", signature);
        }
    }
}

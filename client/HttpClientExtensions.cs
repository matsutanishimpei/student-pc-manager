using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace client
{
    internal static class HttpClientExtensions
    {
        public static async Task<HttpResponseMessage> SendWithRetryAsync(
            this HttpClient client,
            Func<Task<HttpRequestMessage>> requestFactory,
            CancellationToken cancellationToken = default)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                using HttpRequestMessage request = await requestFactory();
                HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= maxAttempts)
                {
                    return response;
                }

                TimeSpan delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(250 * attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}

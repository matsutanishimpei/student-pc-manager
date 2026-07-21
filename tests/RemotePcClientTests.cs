using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using client;
using Xunit;

namespace Tests
{
    public class RemotePcClientTests
    {
        [Fact]
        public async Task GetMachineNameAsync_UsesDefaultPortAndSignedRequest()
        {
            HttpRequestMessage? capturedRequest = null;
            var handler = new StubHttpMessageHandler(request =>
            {
                capturedRequest = request;
                return JsonResponse("{\"machineName\":\"PC-01\"}");
            });
            var client = new HttpClient(handler);
            var remoteClient = new RemotePcClient(client, client);

            string machineName = await remoteClient.GetMachineNameAsync("192.168.1.20:6000", "test-secret-key-123");

            Assert.Equal("PC-01", machineName);
            Assert.Equal("http://192.168.1.20:5000/api/info", capturedRequest!.RequestUri!.ToString());
            Assert.True(capturedRequest.Headers.Contains("X-API-TIMESTAMP"));
            Assert.True(capturedRequest.Headers.Contains("X-API-SIGNATURE"));
        }

        [Fact]
        public async Task ExecuteCommandAsync_ErrorResponse_ThrowsDetailedException()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("invalid command", Encoding.UTF8)
            });
            var client = new HttpClient(handler);
            var remoteClient = new RemotePcClient(client, client);

            var exception = await Assert.ThrowsAsync<RemotePcException>(() =>
                remoteClient.ExecuteCommandAsync("localhost:5000", "test-secret-key-123", "bad", true));

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
            Assert.Contains("invalid command", exception.Message);
        }

        [Fact]
        public async Task GetMachineNameAsync_Retries429Response()
        {
            int attempts = 0;
            var handler = new StubHttpMessageHandler(_ =>
            {
                attempts++;
                return attempts < 3
                    ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    : JsonResponse("{\"machineName\":\"PC-02\"}");
            });
            var client = new HttpClient(handler);
            var remoteClient = new RemotePcClient(client, client);

            string result = await remoteClient.GetMachineNameAsync("localhost:5000", "test-secret-key-123");

            Assert.Equal("PC-02", result);
            Assert.Equal(3, attempts);
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_handler(request));
            }
        }
    }
}

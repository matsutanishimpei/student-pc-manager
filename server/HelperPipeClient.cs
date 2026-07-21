using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

public static class HelperPipeClient
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    public static async Task<byte[]?> SendCommandAsync(string command, int timeoutMs = 500, CancellationToken cancellationToken = default)
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            return null; // No active user session
        }

        string pipeName = $"sendCMD_helper_{sessionId}";

        try
        {
            using (var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                // Connect with a short timeout to fail fast if helper is not running
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeoutMs);
                await pipeClient.ConnectAsync(timeoutCts.Token);

                // Write command followed by a newline
                byte[] commandBytes = Encoding.UTF8.GetBytes(command + "\n");
                await pipeClient.WriteAsync(commandBytes, cancellationToken);
                await pipeClient.FlushAsync(cancellationToken);

                // Read response length (4 bytes)
                byte[] lengthBuffer = new byte[4];
                int bytesRead = 0;
                while (bytesRead < 4)
                {
                    int read = await pipeClient.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), cancellationToken);
                    if (read <= 0) return null; // Stream closed
                    bytesRead += read;
                }

                int length = BitConverter.ToInt32(lengthBuffer, 0);
                if (length <= 0 || length > 10 * 1024 * 1024) // Limit response to 10MB to avoid OOM
                {
                    return null;
                }

                // Read 'length' bytes
                byte[] dataBuffer = new byte[length];
                bytesRead = 0;
                while (bytesRead < length)
                {
                    int read = await pipeClient.ReadAsync(dataBuffer.AsMemory(bytesRead, length - bytesRead), cancellationToken);
                    if (read <= 0) return null; // Stream closed
                    bytesRead += read;
                }

                return dataBuffer;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Connect timeout, or pipe not existing, or connection lost
            return null;
        }
    }
}

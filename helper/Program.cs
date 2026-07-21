using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.AccessControl;
using System.Security.Principal;

namespace sendCMD_helper
{
    class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static async Task Main(string[] args)
        {
            int sessionId = Process.GetCurrentProcess().SessionId;
            string mutexName = $"Global\\sendCMD_helper_mutex_{sessionId}";

            _mutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                // Already running for this session
                return;
            }

            string pipeName = $"sendCMD_helper_{sessionId}";

            while (true)
            {
                try
                {
                    // Create named pipe server
                    var pipeSecurity = new PipeSecurity();
                    SecurityIdentifier? currentUser = WindowsIdentity.GetCurrent().User;
                    if (currentUser != null)
                    {
                        pipeSecurity.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
                    }
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));

                    using (var pipeServer = NamedPipeServerStreamAcl.Create(
                        pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        0,
                        0,
                        pipeSecurity))
                    {
                        // Wait for a client connection
                        await pipeServer.WaitForConnectionAsync();

                        // Process request
                        await HandleClientAsync(pipeServer);
                    }
                }
                catch (Exception)
                {
                    // Sleep to avoid tight loop on persistent errors
                    await Task.Delay(1000);
                }
            }
        }

        private static async Task HandleClientAsync(NamedPipeServerStream pipe)
        {
            try
            {
                using (var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                {
                    string? command = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(command)) return;

                    byte[] responseData;

                    switch (command.Trim().ToLower())
                    {
                        case "screenshot":
                            responseData = CaptureScreen();
                            break;
                        case "activeapp":
                            string activeApp = GetActiveApp();
                            responseData = Encoding.UTF8.GetBytes(activeApp);
                            break;
                        case "processes":
                            string processes = GetProcessesJson();
                            responseData = Encoding.UTF8.GetBytes(processes);
                            break;
                        default:
                            responseData = Encoding.UTF8.GetBytes("Error: Unknown command");
                            break;
                    }

                    // Send length (4 bytes) followed by data
                    byte[] lengthBytes = BitConverter.GetBytes(responseData.Length);
                    await pipe.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await pipe.WriteAsync(responseData, 0, responseData.Length);
                    await pipe.FlushAsync();
                }
            }
            catch (Exception)
            {
                // Connection closed or error occurred
            }
        }

        private static byte[] CaptureScreen()
        {
            try
            {
                var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bitmap.Size);
                    }
                    using (var ms = new MemoryStream())
                    {
                        var encoder = GetEncoder(ImageFormat.Jpeg);
                        if (encoder != null)
                        {
                            var encoderParameters = new EncoderParameters(1);
                            encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
                            bitmap.Save(ms, encoder, encoderParameters);
                        }
                        else
                        {
                            bitmap.Save(ms, ImageFormat.Jpeg);
                        }
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                return Encoding.UTF8.GetBytes($"[Screenshot Helper Error] {ex.Message}");
            }
        }

        private static ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        private static string GetActiveApp()
        {
            try
            {
                var titles = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                    .Select(p => p.MainWindowTitle);
                return string.Join(", ", titles);
            }
            catch (Exception ex)
            {
                return $"[ActiveApp Helper Error] {ex.Message}";
            }
        }

        private static string GetProcessesJson()
        {
            try
            {
                var list = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                    .Select(p => new { ProcessName = p.ProcessName, Id = p.Id, MainWindowTitle = p.MainWindowTitle })
                    .ToList();
                return System.Text.Json.JsonSerializer.Serialize(list);
            }
            catch (Exception)
            {
                return "[]";
            }
        }
    }
}

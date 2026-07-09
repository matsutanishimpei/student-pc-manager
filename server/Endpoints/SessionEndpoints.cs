using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Share.Models;
using Server.Services;
using System;
using System.IO;
using System.Linq;

namespace Server.Endpoints
{
    public static class SessionEndpoints
    {
        public static void MapSessionEndpoints(this WebApplication app)
        {
            // PowerShellコマンド実行API
            app.MapPost("/api/exec", async ([FromBody] CommandRequest request, [FromServices] IInteractiveTaskExecutor executor) =>
            {
                if (string.IsNullOrWhiteSpace(request.Command))
                {
                    return Results.BadRequest(new CommandResponse { ExitCode = -1, Stderr = "Command is empty." });
                }

                var response = await executor.ExecuteCommandAsync(request.Command, request.RunInUserSession);
                return Results.Ok(response);
            });

            // ファイルアップロード（インストーラーの配布用）API
            app.MapPost("/api/upload", async (HttpRequest request, [FromServices] IConfiguration configuration) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest("Expected a multipart form content type.");
                }

                var form = await request.ReadFormAsync();
                var file = form.Files.GetFile("file");

                if (file == null || file.Length == 0)
                {
                    return Results.BadRequest("No file uploaded.");
                }

                string defaultUploadDir = "C:\\Users\\Public\\sendCMD_uploads";
                string uploadDir = configuration["UploadDirectory"] ?? defaultUploadDir;

                try
                {
                    if (!Directory.Exists(uploadDir))
                    {
                        Directory.CreateDirectory(uploadDir);
                    }

                    string filePath = Path.Combine(uploadDir, file.FileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    Log.Write($"[File Uploaded] Target Path: {filePath}, Size: {file.Length} bytes");

                    return Results.Ok(new { FilePath = filePath });
                }
                catch (Exception)
                {
                    return Results.StatusCode(500); // Internal Server Error
                }
            });

            // 画面キャプチャ取得 API
            app.MapGet("/api/screenshot", async ([FromServices] IInteractiveTaskExecutor executor) =>
            {
                byte[]? imageBytes = await executor.GetScreenshotAsync();
                if (imageBytes == null)
                {
                    return Results.StatusCode(504); // Gateway Timeout
                }
                return Results.File(imageBytes, "image/jpeg");
            });

            // 稼働中のアクティブアプリ一覧取得 API
            app.MapGet("/api/activeapp", async ([FromServices] IInteractiveTaskExecutor executor) =>
            {
                string result = await executor.GetActiveAppAsync();
                return Results.Ok(new { ActiveApp = result });
            });

            // プロセス一覧取得 API
            app.MapGet("/api/processes", async ([FromServices] IInteractiveTaskExecutor executor) =>
            {
                string result = await executor.GetProcessesJsonAsync();
                return Results.Content(result, "application/json");
            });

            // PC情報取得API
            app.MapGet("/api/info", () => Results.Ok(new ServerInfoResponse { MachineName = Environment.MachineName }));

            // MACアドレス取得API
            app.MapGet("/api/mac", () =>
            {
                try
                {
                    var mac = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                        .Where(nic => nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ||
                                      nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                        .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                        .Select(nic => nic.GetPhysicalAddress().ToString())
                        .FirstOrDefault(address => !string.IsNullOrEmpty(address));

                    if (string.IsNullOrEmpty(mac))
                    {
                        return Results.NotFound("Physical MAC address not found.");
                    }

                    string formattedMac = string.Join("-", System.Linq.Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
                    return Results.Ok(new MacAddressResponse { MacAddress = formattedMac });
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message);
                }
            });
        }
    }
}

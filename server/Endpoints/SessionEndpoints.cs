using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Share.Models;
using Server.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Share.Security;

namespace Server.Endpoints
{
    public static class SessionEndpoints
    {
        private const int MaxCommandLength = 32768;
        private const long DefaultMaxUploadBytes = 524288000;

        public static void MapSessionEndpoints(this WebApplication app)
        {
            // PowerShellコマンド実行API
            app.MapPost("/api/exec", async ([FromBody] CommandRequest request, [FromServices] IInteractiveTaskExecutor executor, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Command))
                {
                    return Results.Problem("Command is empty.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid command");
                }

                if (request.Command.Length > MaxCommandLength)
                {
                    return Results.Problem($"Command is too long. Max length is {MaxCommandLength} characters.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid command");
                }

                var response = await executor.ExecuteCommandAsync(request.Command, request.RunInUserSession, cancellationToken);
                return Results.Ok(response);
            });

            // ファイルアップロード（インストーラーの配布用）API
            app.MapPost("/api/upload", async (HttpRequest request, [FromServices] IConfiguration configuration) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.Problem("Expected multipart form data.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
                }

                long maxUploadBytes = configuration.GetValue<long?>("MaxUploadBytes") ?? DefaultMaxUploadBytes;
                string defaultUploadDir = "C:\\Users\\Public\\sendCMD_uploads";
                string uploadDir = configuration["UploadDirectory"] ?? defaultUploadDir;
                string? temporaryPath = null;
                try
                {
                    var mediaType = MediaTypeHeaderValue.Parse(request.ContentType);
                    string? boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
                    if (string.IsNullOrWhiteSpace(boundary))
                    {
                        return Results.Problem("The multipart boundary is missing.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
                    }

                    string uploadRoot = Path.GetFullPath(uploadDir);
                    Directory.CreateDirectory(uploadRoot);
                    long minimumFreeSpace = Math.Max(0, configuration.GetValue<long?>("MinimumFreeSpaceBytesAfterUpload") ?? 67108864);
                    long estimatedFileBytes = Math.Min(request.ContentLength ?? maxUploadBytes, maxUploadBytes);
                    if (!HasSufficientDiskSpace(uploadRoot, estimatedFileBytes, minimumFreeSpace))
                    {
                        Log.Write($"[File Upload Rejected] Insufficient free space. Estimated size: {estimatedFileBytes} bytes");
                        return Results.Problem("There is not enough free disk space to store the file.", statusCode: StatusCodes.Status507InsufficientStorage, title: "Upload rejected");
                    }

                    var reader = new MultipartReader(boundary, request.Body);
                    MultipartSection? section;
                    string? filePath = null;
                    long fileLength = 0;
                    string? actualContentHash = null;
                    string? actualFileNameHash = null;
                    while ((section = await reader.ReadNextSectionAsync(request.HttpContext.RequestAborted)) != null)
                    {
                        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                            !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, "file", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (filePath != null)
                        {
                            return Results.Problem("Only one file can be uploaded at a time.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
                        }

                        string submittedFileName = HeaderUtilities.RemoveQuotes(
                            disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? string.Empty;
                        actualFileNameHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(submittedFileName))).ToLowerInvariant();
                        string? storedFileName = CreateSafeStoredFileName(submittedFileName);
                        if (storedFileName == null)
                        {
                            return Results.Problem("The uploaded file name is invalid.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
                        }

                        filePath = Path.GetFullPath(Path.Combine(uploadRoot, storedFileName));
                        if (!filePath.StartsWith(EnsureTrailingSeparator(uploadRoot), StringComparison.OrdinalIgnoreCase))
                        {
                            return Results.Problem("The uploaded file path is invalid.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
                        }

                        temporaryPath = Path.Combine(uploadRoot, $".{storedFileName}.{Guid.NewGuid():N}.uploading");
                        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        await using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        byte[] buffer = new byte[81920];
                        int bytesRead;
                        while ((bytesRead = await section.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
                        {
                            fileLength += bytesRead;
                            if (fileLength > maxUploadBytes)
                            {
                                return Results.Problem($"File is too large. Max upload size is {maxUploadBytes} bytes.", statusCode: StatusCodes.Status413PayloadTooLarge, title: "Upload rejected");
                            }
                            hasher.AppendData(buffer, 0, bytesRead);
                            await stream.WriteAsync(buffer.AsMemory(0, bytesRead), request.HttpContext.RequestAborted);
                        }
                        await stream.FlushAsync(request.HttpContext.RequestAborted);
                        actualContentHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    }

                    if (filePath == null || fileLength == 0 || actualContentHash == null)
                    {
                        return Results.Problem("No file was uploaded.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
                    }

                    string expectedContentHash = request.Headers["X-API-CONTENT-SHA256"].ToString().ToLowerInvariant();
                    if (!CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.ASCII.GetBytes(actualContentHash),
                        System.Text.Encoding.ASCII.GetBytes(expectedContentHash)))
                    {
                        return Results.Problem("The uploaded file content hash is invalid.", statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");
                    }
                    string expectedFileNameHash = request.Headers["X-API-FILENAME-SHA256"].ToString().ToLowerInvariant();
                    if (actualFileNameHash == null || !CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.ASCII.GetBytes(actualFileNameHash),
                        System.Text.Encoding.ASCII.GetBytes(expectedFileNameHash)))
                    {
                        return Results.Problem("The uploaded file name signature is invalid.", statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");
                    }

                    File.Move(temporaryPath!, filePath);
                    temporaryPath = null;

                    Log.Write($"[File Uploaded] Target Path: {filePath}, Size: {fileLength} bytes");

                    return Results.Ok(new { FilePath = filePath });
                }
                catch (Exception ex)
                {
                    Log.Write($"[File Upload Error] {ex}");
                    return Results.Problem("The file could not be stored.", statusCode: StatusCodes.Status500InternalServerError, title: "Upload failed");
                }
                finally
                {
                    if (temporaryPath != null)
                    {
                        try { File.Delete(temporaryPath); }
                        catch (Exception cleanupEx) { Log.Write($"[File Upload Cleanup Error] {cleanupEx.Message}"); }
                    }
                }
            });

            // 画面キャプチャ取得 API
            app.MapGet("/api/screenshot", async ([FromServices] IInteractiveTaskExecutor executor, CancellationToken cancellationToken) =>
            {
                byte[]? imageBytes = await executor.GetScreenshotAsync(cancellationToken);
                if (imageBytes == null)
                {
                    return Results.Problem("The screenshot operation timed out.", statusCode: StatusCodes.Status504GatewayTimeout, title: "Screenshot unavailable");
                }
                return Results.File(imageBytes, "image/jpeg");
            });

            // 稼働中のアクティブアプリ一覧取得 API
            app.MapGet("/api/activeapp", async ([FromServices] IInteractiveTaskExecutor executor, CancellationToken cancellationToken) =>
            {
                string result = await executor.GetActiveAppAsync(cancellationToken);
                return Results.Ok(new { ActiveApp = result });
            });

            // プロセス一覧取得 API
            app.MapGet("/api/processes", async ([FromServices] IInteractiveTaskExecutor executor, CancellationToken cancellationToken) =>
            {
                string result = await executor.GetProcessesJsonAsync(cancellationToken);
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
                        return Results.Problem("A physical MAC address was not found.", statusCode: StatusCodes.Status404NotFound, title: "System information unavailable");
                    }

                    string formattedMac = string.Join("-", System.Linq.Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
                    return Results.Ok(new MacAddressResponse { MacAddress = formattedMac });
                }
                catch (Exception ex)
                {
                    Log.Write($"[MAC Address Error] {ex}");
                    return Results.Problem("The MAC address could not be retrieved.", statusCode: StatusCodes.Status500InternalServerError, title: "System information unavailable");
                }
            });
        }

        internal static string? CreateSafeStoredFileName(string submittedFileName)
        {
            string originalName = Path.GetFileName(submittedFileName);
            if (string.IsNullOrWhiteSpace(originalName))
            {
                return null;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var safeChars = originalName
                .Select(c => invalidChars.Contains(c) || char.IsControl(c) ? '_' : c)
                .ToArray();

            string cleanName = new string(safeChars).Trim().Trim('.');
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                return null;
            }

            string extension = Path.GetExtension(cleanName);
            if (extension.Length > 16)
            {
                extension = extension.Substring(0, 16);
            }

            string nameWithoutExtension = Path.GetFileNameWithoutExtension(cleanName);
            if (nameWithoutExtension.Length > 80)
            {
                nameWithoutExtension = nameWithoutExtension.Substring(0, 80);
            }

            string uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            return $"{nameWithoutExtension}_{uniqueSuffix}{extension}";
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        internal static bool HasSufficientDiskSpace(string directoryPath, long incomingFileBytes, long minimumFreeSpaceBytes)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(directoryPath));
                if (string.IsNullOrEmpty(root))
                {
                    return true;
                }

                long requiredBytes = checked(incomingFileBytes + minimumFreeSpaceBytes);
                return new DriveInfo(root).AvailableFreeSpace >= requiredBytes;
            }
            catch (Exception ex)
            {
                // Some network or virtual paths cannot report capacity. Preserve existing behavior in that case.
                Log.Write($"[File Upload Space Check Warning] {ex.Message}");
                return true;
            }
        }
    }
}

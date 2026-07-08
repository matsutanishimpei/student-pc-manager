using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Share.Models;
using System;
using System.IO;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var options = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
};
var builder = WebApplication.CreateBuilder(options);

// Windows サービスとしての有効期間を構成する
builder.Host.UseWindowsService();

// Kestrel Webサーバーのポートを設定 (すべてのIPアドレスの5000番ポートで待ち受け)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
    // 最大リクエストボディサイズを500MBに設定 (デフォルトは30MB)
    options.Limits.MaxRequestBodySize = 524288000; 
});

// マルチパートフォーム（ファイルアップロード）の上限を500MBに設定
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000;
});

var app = builder.Build();

// APIキー認証
const string ApiKeyHeaderName = "X-API-KEY";
string expectedApiKey = app.Configuration["ApiKey"] ?? "5c3e7f41-0f73-455b-b9d9-482470724653";

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey) || 
            extractedApiKey != expectedApiKey)
        {
            context.Response.StatusCode = 401; // Unauthorized
            await context.Response.WriteAsync("Unauthorized: Invalid or missing API Key.");
            return;
        }
    }
    await next();
});

// ヘルスチェック用エンドポイント
app.MapGet("/", () => "sendCMD Server is running.");

// PowerShellコマンド実行API
app.MapPost("/api/exec", async ([FromBody] CommandRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Command))
    {
        return Results.BadRequest(new CommandResponse { ExitCode = -1, Stderr = "Command is empty." });
    }

    if (request.RunInUserSession)
    {
        // 対話型ユーザーセッションとして実行する
        byte[]? bytes = await ExecutePowerShellInUserSessionAsync(request.Command, "txt");
        if (bytes == null)
        {
            return Results.Ok(new CommandResponse 
            { 
                ExitCode = -1, 
                Stderr = "Failed to execute PowerShell command in interactive user session. (Check logs at C:\\Users\\Public\\sendCMD_server_log.txt)" 
            });
        }
        string output = System.Text.Encoding.UTF8.GetString(bytes).Trim();
        return Results.Ok(new CommandResponse 
        { 
            ExitCode = 0, 
            Stdout = output 
        });
    }
    else
    {
        // 通常通り SYSTEM アカウント権限で実行する
        var response = ExecutePowerShell(request.Command);
        return Results.Ok(response);
    }
});

// ファイルアップロード（インストーラーの配布用）API
app.MapPost("/api/upload", async (HttpRequest request) =>
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

    // アップロードされたファイルの保存先ディレクトリの決定
    string defaultUploadDir = "C:\\Users\\Public\\sendCMD_uploads";
    string uploadDir = app.Configuration["UploadDirectory"] ?? defaultUploadDir;
    
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

        return Results.Ok(new { FilePath = filePath });
    }
    catch (Exception)
    {
        return Results.StatusCode(500); // Internal Server Error
    }
});

// 画面キャプチャ取得 API (Session 0 隔離対策としてスケジュールタスク経由で対話型セッションにて実行)
app.MapGet("/api/screenshot", async () =>
{
    string psCommand = "try { " +
                       "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; " +
                       "$s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; " +
                       "$b = New-Object System.Drawing.Bitmap $s.Width, $s.Height; " +
                       "$g = [System.Drawing.Graphics]::FromImage($b); " +
                       "$g.CopyFromScreen($s.X, $s.Y, 0, 0, $b.Size); " +
                       "$b.Save('{OUT_FILE}', [System.Drawing.Imaging.ImageFormat]::Jpeg); " +
                       "$g.Dispose(); $b.Dispose();" +
                       "} catch {" +
                       "\"[Screenshot Error] $_\" | Out-File -FilePath 'C:\\Users\\Public\\sendCMD_US_error_log.txt' -Append -Encoding utf8" +
                       "}";

    byte[]? imageBytes = await ExecutePowerShellInUserSessionAsync(psCommand, "jpg");
    if (imageBytes == null)
    {
        return Results.StatusCode(504); // Gateway Timeout (ユーザーがログインしていない等)
    }
    return Results.File(imageBytes, "image/jpeg");
});

// 稼働中のアクティブアプリ一覧取得 API (対話型セッション内で実行)
app.MapGet("/api/activeapp", async () =>
{
    string script = "(Get-Process | Where-Object { $_.MainWindowTitle } | Select-Object -ExpandProperty MainWindowTitle) -join ', '";
    byte[]? bytes = await ExecutePowerShellInUserSessionAsync(script, "txt");
    string result = bytes != null ? System.Text.Encoding.UTF8.GetString(bytes).Trim() : string.Empty;
    return Results.Ok(new { ActiveApp = result });
});

// プロセス一覧取得 API (対話型セッション内で実行)
app.MapGet("/api/processes", async () =>
{
    string script = "$p = Get-Process | Where-Object { $_.MainWindowTitle } | Select-Object ProcessName, Id, MainWindowTitle; if ($p) { ConvertTo-Json @($p) -Compress } else { '[]' }";
    byte[]? bytes = await ExecutePowerShellInUserSessionAsync(script, "txt");
    string result = bytes != null ? System.Text.Encoding.UTF8.GetString(bytes).Trim() : "[]";
    if (string.IsNullOrEmpty(result))
    {
        result = "[]";
    }
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

app.Run();

// ログ出力用ヘルパー
static void WriteLog(string message)
{
    try
    {
        string logPath = "C:\\Users\\Public\\sendCMD_server_log.txt";
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
    }
    catch {}
}

// PowerShell コマンド実行ヘルパー
static CommandResponse ExecutePowerShell(string command)
{
    var result = new CommandResponse();
    try
    {
        using var process = new Process();
        process.StartInfo.FileName = "powershell.exe";
        // 標準入力からコマンドを受け取って実行する形式（クォーテーションのパースエラー対策）
        process.StartInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -";
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();

        using (var sw = process.StandardInput)
        {
            if (sw.BaseStream.CanWrite)
            {
                sw.WriteLine(command);
            }
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        process.WaitForExit();

        result.ExitCode = process.ExitCode;
        result.Stdout = stdout;
        result.Stderr = stderr;
    }
    catch (Exception ex)
    {
        result.ExitCode = -1;
        result.Stderr = ex.Message;
    }
    return result;
}


// 対話型ユーザーのコンソールセッション内でPowerShellスクリプトを実行するヘルパー (スクリプトファイル生成方式)
static async Task<byte[]?> ExecutePowerShellInUserSessionAsync(string psCommand, string fileExtension)
{
    int sessionId = 0;
    try
    {
        sessionId = Process.GetCurrentProcess().SessionId;
    }
    catch {}

    // 1. すでに非Session 0 (対話型セッション) で稼働しているなら、タスクスケジューラを経由せず直接PowerShellを実行する
    if (sessionId != 0)
    {
        try
        {
            string tempOutFile = Path.Combine("C:\\Users\\Public", "sendCMD_direct_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "." + fileExtension);
            string directScript;
            if (fileExtension == "png")
            {
                directScript = psCommand.Replace("{OUT_FILE}", tempOutFile.Replace("\\", "\\\\"));
            }
            else
            {
                directScript = $"$r = $({psCommand}); Out-File -FilePath '{tempOutFile}' -InputObject $r -Encoding utf8";
            }

            var resp = ExecutePowerShell(directScript);
            if (resp.ExitCode == 0 && File.Exists(tempOutFile))
            {
                byte[] bytes = await File.ReadAllBytesAsync(tempOutFile);
                try { File.Delete(tempOutFile); } catch {}
                return bytes;
            }
            else
            {
                WriteLog($"[Direct Execution Error] ExitCode: {resp.ExitCode}, Stderr: {resp.Stderr}");
                return null;
            }
        }
        catch (Exception ex)
        {
            WriteLog($"[Direct Execution Exception] {ex.Message}");
            return null;
        }
    }

    // 2. Session 0 (Windowsサービス) の場合は、CreateProcessAsUser で対話型デスクトップ上で実行する
    string taskId = "sendCMD_US_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    string scriptFile = Path.Combine("C:\\Users\\Public", taskId + ".ps1");
    string outputFile = Path.Combine("C:\\Users\\Public", taskId + "." + fileExtension);

    string fullScriptContent;
    if (fileExtension == "png")
    {
        // .ps1 ファイル内のシングルクォート文字列ではバックスラッシュはリテラル扱い
        fullScriptContent = psCommand.Replace("{OUT_FILE}", outputFile);
    }
    else
    {
        fullScriptContent = $"$ErrorActionPreference = 'Continue'; $r = & {{ {psCommand} }} 2>&1 | Out-String; Out-File -FilePath '{outputFile}' -InputObject $r -Encoding utf8";
    }

    try
    {
        // 1. スクリプトファイルを書き込み (UTF-8)
        await File.WriteAllTextAsync(scriptFile, fullScriptContent, System.Text.Encoding.UTF8);

        // 2. CreateProcessAsUser で対話型デスクトップ上の PowerShell を起動
        string cmdLine = $"powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptFile}\"";
        var (success, error) = InteractiveProcessHelper.RunInUserSession(cmdLine, 15000);

        // 3. スクリプトファイルのクリーンアップ
        try { File.Delete(scriptFile); } catch {}

        if (!success)
        {
            WriteLog($"[CreateProcessAsUser FAILED] {error}");
            return null;
        }

        // 4. 出力ファイルの存在とサイズを確認
        if (!File.Exists(outputFile) || new FileInfo(outputFile).Length == 0)
        {
            WriteLog($"[Output File Missing] {outputFile} was not created or is empty after CreateProcessAsUser");
            return null;
        }

        // 5. 出力データの読み込みとファイル削除
        byte[] resultBytes = await File.ReadAllBytesAsync(outputFile);
        try { File.Delete(outputFile); } catch {}
        return resultBytes;
    }
    catch (Exception ex)
    {
        WriteLog($"[Session 0 Execution Exception] {ex.Message}");
        try { File.Delete(scriptFile); } catch {}
        return null;
    }
}

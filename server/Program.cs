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
app.MapPost("/api/exec", ([FromBody] CommandRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Command))
    {
        return Results.BadRequest(new CommandResponse { ExitCode = -1, Stderr = "Command is empty." });
    }

    var response = ExecutePowerShell(request.Command);
    return Results.Ok(response);
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
    string defaultUploadDir = Path.Combine(Path.GetTempPath(), "sendCMD_uploads");
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
    string psCommand = "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; " +
                       "$s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; " +
                       "$b = New-Object System.Drawing.Bitmap $s.Width, $s.Height; " +
                       "$g = [System.Drawing.Graphics]::FromImage($b); " +
                       "$g.CopyFromScreen($s.X, $s.Y, 0, 0, $b.Size); " +
                       "$b.Save('{OUT_FILE}', [System.Drawing.Imaging.ImageFormat]::Png); " +
                       "$g.Dispose(); $b.Dispose();";

    byte[]? imageBytes = await ExecutePowerShellInUserSessionAsync(psCommand, "png");
    if (imageBytes == null)
    {
        return Results.StatusCode(504); // Gateway Timeout (ユーザーがログインしていない等)
    }
    return Results.File(imageBytes, "image/png");
});

// 稼働中のアクティブアプリ一覧取得 API (対話型セッション内で実行)
app.MapGet("/api/activeapp", async () =>
{
    string script = "$csv = tasklist /v /fo csv | ConvertFrom-Csv; " +
                   "$titles = foreach ($row in $csv) { " +
                       "$props = $row.PSObject.Properties | Select-Object -ExpandProperty Name; " +
                       "if ($props.Count -ge 9) { " +
                           "$win = $row.($props[8]); " +
                           "if ($win -and $win -ne 'N/A' -and $win -ne 'クラスなし') { $win } " +
                       "} " +
                   "}; " +
                   "if ($titles) { $titles -join ', ' } else { '' }";
    byte[]? bytes = await ExecutePowerShellInUserSessionAsync(script, "txt");
    string result = bytes != null ? System.Text.Encoding.UTF8.GetString(bytes).Trim() : string.Empty;
    return Results.Ok(new { ActiveApp = result });
});

// プロセス一覧取得 API (対話型セッション内で実行)
app.MapGet("/api/processes", async () =>
{
    string script = "$csv = tasklist /v /fo csv | ConvertFrom-Csv; " +
                   "$res = foreach ($row in $csv) { " +
                       "$props = $row.PSObject.Properties | Select-Object -ExpandProperty Name; " +
                       "if ($props.Count -ge 9) { " +
                           "$win = $row.($props[8]); " +
                           "if ($win -and $win -ne 'N/A' -and $win -ne 'クラスなし') { " +
                               "[PSCustomObject]@{ " +
                                   "ProcessName = $row.($props[0]); " +
                                   "Id = [int]$row.($props[1]); " +
                                   "MainWindowTitle = $win " +
                               "} " +
                           "} " +
                       "} " +
                   "}; " +
                   "if ($res) { ConvertTo-Json @($res) -Compress } else { '[]' }";
    byte[]? bytes = await ExecutePowerShellInUserSessionAsync(script, "txt");
    string result = bytes != null ? System.Text.Encoding.UTF8.GetString(bytes).Trim() : "[]";
    if (string.IsNullOrEmpty(result))
    {
        result = "[]";
    }
    return Results.Content(result, "application/json");
});

app.Run();

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

// 外部コマンド実行用ヘルパー (schtasks.exe等)
static void ExecuteCommand(string fileName, string arguments)
{
    try
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        process.WaitForExit();
    }
    catch {}
}

// 対話型ユーザーのコンソールセッション内でPowerShellスクリプトを実行するヘルパー (スクリプトファイル生成方式)
static async Task<byte[]?> ExecutePowerShellInUserSessionAsync(string psCommand, string fileExtension)
{
    string taskId = "sendCMD_US_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    string scriptFile = Path.Combine("C:\\Users\\Public", taskId + ".ps1");
    string outputFile = Path.Combine("C:\\Users\\Public", taskId + "." + fileExtension);
    
    string fullScriptContent;
    if (fileExtension == "png")
    {
        fullScriptContent = psCommand.Replace("{OUT_FILE}", outputFile.Replace("\\", "\\\\"));
    }
    else
    {
        fullScriptContent = $"$r = {psCommand}; Out-File -FilePath '{outputFile}' -InputObject $r -Encoding utf8";
    }

    try
    {
        // 1. スクリプトファイルを書き込み (UTF-8)
        await File.WriteAllTextAsync(scriptFile, fullScriptContent, System.Text.Encoding.UTF8);

        // 2. スケジュールタスクの作成 (引数エスケープの問題を避けるため、ファイルを指定して実行)
        string createArgs = $"/create /tn \"{taskId}\" /tr \"powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \\\"{scriptFile}\\\"\" /sc ONCE /st 00:00 /ru INTERACTIVE /f";
        ExecuteCommand("schtasks.exe", createArgs);

        // 3. タスクの実行
        ExecuteCommand("schtasks.exe", $"/run /tn \"{taskId}\"");

        // 4. 出力ファイルの生成待ち (最大10秒)
        bool fileCreated = false;
        for (int i = 0; i < 50; i++)
        {
            if (File.Exists(outputFile))
            {
                fileCreated = true;
                break;
            }
            await Task.Delay(200);
        }

        // 5. クリーンアップ
        ExecuteCommand("schtasks.exe", $"/delete /tn \"{taskId}\" /f");
        try { File.Delete(scriptFile); } catch {}

        if (!fileCreated)
        {
            return null;
        }

        // 6. 出力データの読み込みとファイル削除
        byte[] resultBytes = await File.ReadAllBytesAsync(outputFile);
        try { File.Delete(outputFile); } catch {}
        return resultBytes;
    }
    catch
    {
        return null;
    }
}

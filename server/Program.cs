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

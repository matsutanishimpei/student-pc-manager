using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Server.Endpoints;
using Server.Middlewares;
using Server.Services;
using Share.Security;
using Server;
using System;

var options = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
};
var builder = WebApplication.CreateBuilder(options);
ServerConfigurationValidator.Validate(builder.Configuration);
long maxUploadBytes = builder.Configuration.GetValue<long?>("MaxUploadBytes") ?? 524288000;

// Windows サービスとしての有効期間を構成する
builder.Host.UseWindowsService();

// Kestrel Webサーバーのポートを設定 (すべてのIPアドレスの5000番ポートで待ち受け)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
    // 最大リクエストボディサイズを500MBに設定 (デフォルトは30MB)
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});

// マルチパートフォーム（ファイルアップロード）の上限を500MBに設定
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});

// DIコンテナにサービスを登録
builder.Services.AddSingleton<IInteractiveTaskExecutor, AdaptiveSessionExecutor>();
builder.Services.AddSingleton(sp =>
{
    var environment = sp.GetRequiredService<IHostEnvironment>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    if (environment.IsEnvironment("Testing")) return new ApiNonceStore();
    string nonceCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "sendCMD", "nonce-cache.json");
    return new ApiNonceStore(nonceCachePath);
});
builder.Services.AddSingleton(sp =>
{
    var environment = sp.GetRequiredService<IHostEnvironment>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    if (environment.IsEnvironment("Testing"))
    {
        return new ServerApiKeyProvider(configuration["ApiKey"] ?? string.Empty);
    }
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Server API key protection requires Windows.");
    string apiKey = ServerApiKeyStore.LoadOrMigrate(configuration, environment.ContentRootPath);
    int minimumLength = Math.Max(1, configuration.GetValue<int?>("MinimumApiKeyLength") ?? ApiKeyPolicy.DefaultMinimumLength);
    ApiKeyPolicy.Validate(apiKey, minimumLength);
    return new ServerApiKeyProvider(apiKey);
});

var app = builder.Build();
_ = app.Services.GetRequiredService<ServerApiKeyProvider>();

// APIキー認証ミドルウェアの適用
app.UseMiddleware<ApiExceptionHandlingMiddleware>();
app.UseMiddleware<ApiResponseSecurityHeadersMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseMiddleware<ApiConcurrencyLimitMiddleware>();

// ヘルスチェック用エンドポイント
app.MapGet("/", () => "sendCMD Server is running.");

// セッションエンドポイントの登録
app.MapSessionEndpoints();

// 常駐ヘルパープロセスの登録と初回起動
HelperLifecycleService.RegisterAndStartHelper();

app.Run();

// WebApplicationFactory<Program> がテストプロジェクトからアクセスするために必要
public partial class Program { }

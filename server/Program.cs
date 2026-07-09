using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Server.Endpoints;
using Server.Middlewares;
using Server.Services;
using System;

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

// DIコンテナにサービスを登録
builder.Services.AddSingleton<IInteractiveTaskExecutor, AdaptiveSessionExecutor>();

var app = builder.Build();

// APIキー認証ミドルウェアの適用
app.UseMiddleware<ApiKeyAuthMiddleware>();

// ヘルスチェック用エンドポイント
app.MapGet("/", () => "sendCMD Server is running.");

// セッションエンドポイントの登録
app.MapSessionEndpoints();

// 常駐ヘルパープロセスの登録と初回起動
HelperLifecycleService.RegisterAndStartHelper();

app.Run();

// WebApplicationFactory<Program> がテストプロジェクトからアクセスするために必要
public partial class Program { }

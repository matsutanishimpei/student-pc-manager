# sendCMD 開発ガイド

この文書は、保守時に現在の安全性、文字コード互換性、モジュール分割を崩さないためのルールをまとめたものです。

## 開発環境

- Windows 10/11またはWindows Server
- .NET 8 SDK
- WPFをビルドできるWindows環境

変更前後に次を実行してください。

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --no-build --configuration Release
```

## 責務の分け方

- `MainWindow.xaml.cs`: 画面イベントと表示状態の調整だけを担当します。
- `RemotePcClient.cs`: 署名付きHTTP通信、タイムアウト、応答処理を担当します。
- `ClientDataStore.cs`: `pcs.json`と`config.json`の読み書きを担当します。
- `OperationLogger.cs`: クライアント操作ログを担当します。
- `StudentCsvProcessor.cs`: CSV構文と文字コード判定を担当します。
- `WakeOnLanService.cs`: Wake on LANを担当します。
- `server/Endpoints/`: API入力検証と応答を担当します。
- `server/Services/`: SYSTEMまたはログインユーザーでの処理実行を担当します。
- `share/`: クライアントとサーバーの共通契約だけを置きます。

画面から直接HTTP通信やファイル保存を増やさず、対応する担当クラスへ追加してください。

## 認証と秘密情報

- リポジトリ内の`server/appsettings.json`の`ApiKey`は空文字列を維持します。
- 新しいAPIも`/api`配下に置き、`AddApiSignature(apiKey)`で署名してください。
- 署名はAPIキー、UTCタイムスタンプ、HTTPメソッド、パスから生成します。
- APIキーをヘッダー、URL、ログ、例外メッセージへ平文で出力しないでください。
- 認証時刻の許容差は5分です。変更する場合はテストと両マニュアルを同時に更新してください。
- `build-release.ps1`は配布物へ初期キーを注入します。公開環境では教室ごとのキーへ変更する運用を維持してください。

## 入力とファイルの安全性

- PowerShellコマンドの32,768文字上限を維持してください。
- アップロード上限は`MaxUploadBytes`から取得し、通信層とフォーム処理の両方に適用します。
- 利用者が送ったファイル名を保存パスへ直接使用しないでください。
- `Path.GetFileName`、不正文字の置換、一意な接尾辞、保存先配下の検証、`FileMode.CreateNew`を維持してください。
- SYSTEMとログインユーザーの双方が使う一時ファイルは、用途を限定して`C:\Users\Public\`配下へ置きます。

## 文字コード

- 通常のソース、Markdown、JSON、PowerShellはUTF-8で保存します。`.editorconfig`を優先してください。
- `install.bat`と`uninstall.bat`はCP932です。UTF-8へ変換したり、`chcp 65001`を追加したりしないでください。
- JSONと操作ログはBOMなしUTF-8で保存します。
- CSV読み込みはUTF-8 BOM付き、UTF-16 LE/BE、厳密なUTF-8、CP932フォールバックの順序を維持します。
- CSV出力はExcel互換のためCP932です。変更する場合は利用者向けの選択肢と移行手順を用意してください。
- 文字化けの確認には日本語のPC名、学生名、カンマ、二重引用符を含むテストデータを使います。

## Windowsセッション

- サーバーはSYSTEMのSession 0で動作します。
- 画面、ウィンドウタイトル、MSIX/APPXなどログインユーザー依存の処理は、既存の対話セッション実行機構を使います。
- UIスレッドのWPFコントロールをバックグラウンド処理から直接参照しないでください。値を先に取得してから処理へ渡します。

## テスト

- 共通処理や保存処理を変更したら、対応する単体テストを追加します。
- APIを変更したら`SessionEndpointsIntegrationTests`と認証テストを実行します。
- CSVを変更したらUTF-8、UTF-16、CP932、引用符付きフィールドを確認します。
- ファイル配布を変更したらサイズ超過、空ファイル、不正ファイル名、重複名、保存先逸脱を確認します。
- CIはWPF対応のため`windows-latest`を維持します。

## 文書更新

利用者に見える挙動、設定値、制限、保存場所、認証、文字コード、リリース手順を変えた場合は、同じ変更内で該当文書と`CHANGELOG.md`を更新してください。

## リリース

1. `client/MainWindow.xaml`のタイトルと表示バージョンを更新します。
2. `CHANGELOG.md`へ変更内容と運用上の注意を追記します。
3. Release構成でビルドと全テストを実行します。
4. 変更を`main`へ取り込みます。
5. `v2.1.2`のようなタグを対象コミットへ付け、`origin`へpushします。
6. GitHub Actionsがテスト、配布物作成、ZIP添付、下書きRelease作成を行います。
7. 下書きの説明と添付物を確認してから公開します。

タグを削除・付け直してリリースを上書きする運用は避け、新しい修正版タグを作成してください。

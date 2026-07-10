# sendCMD 開発・貢献ガイドライン

本プロジェクトに機能追加や修正を行う際は、以下の実装ルールおよび制約事項を必ず遵守してください。

---

## 1. セキュリティと認証キーの管理ルール
*   **ソースコード内の APIKey は常に空にする**
    *   セキュリティ上のハードコード防止のため、リポジトリに配置する `server/appsettings.json` および `client/MainWindow.xaml.cs` (ClientConfig) の `ApiKey` の初期値は必ず空文字列（`""`）に保ってください。
*   **リリースビルド時の動的注入**
    *   配布パッケージにデフォルトのAPIキーを注入する処理は、すべて `build-release.ps1` 内で自動化されています。ビルド成果物をローカルで手動作成する際は、直接ファイルを書き換えるのではなく必ずこのビルドスクリプトを実行してください。
*   **認証方式（HMAC-SHA256 署名）**
    *   API通信時には `X-API-TIMESTAMP` と `X-API-SIGNATURE` を用いた署名検証が行われます。クライアント側で新規APIを追加・呼び出す際は、必ず `request.AddApiSignature(apiKey)` 拡張メソッドを使用してください。

## 2. Windows サービスおよびセッション管理の制約 (SYSTEM権限)
*   **Session 0 隔離の回避**
    *   生徒PC側サービス（`server.exe`）は SYSTEM アカウント（Session 0）で動作するため、画面キャプチャの取得やウィンドウタイトルの取得などの対話型操作を直接実行できません。
    *   これらのGUI依存操作を追加する際は、直接実行せず必ず `InteractiveProcessHelper.RunInUserSession` を使用して、ログインユーザーのセッション（Session 1 等）でプロセスを実行するように実装してください。
*   **ファイルパスとアクセス権限**
    *   SYSTEM アカウントと通常ユーザーの双方が読み書きできるようにするため、一時ファイル、出力バッファ、スクリプトファイルなどは必ず `C:\Users\Public\` 配下に作成してください（`Path.GetTempPath()` は SYSTEM環境下で `C:\Windows\Temp` を指すため使用禁止）。

## 3. 文字コードとバッチファイル (Shift_JIS の厳守)
*   **セットアップ用バッチファイルのエンコーディング**
    *   `install.bat` および `uninstall.bat` は、日本語版 Windows のコマンドプロンプトで文字化けしないよう、必ず **`Shift_JIS (CP932)`** エンコードで保存してください。
    *   バッチファイル内で `chcp 65001`（UTF-8への変更）を実行しないようにしてください。

## 4. 自動テストおよび CI/CD (GitHub Actions)
*   **テストの追加要件**
    *   共有ロジックやクライアント側ロジックを追加した際は、`tests/` プロジェクトに対応する xUnit テストを作成してください。
    *   APIに影響を与える変更を行った場合は、`SessionEndpointsIntegrationTests.cs` の結合テストを実行し、認証エラーが発生しないか確認してください。
*   **CI実行環境の制約 (Windowsランナーの維持)**
    *   教員アプリは WPF (`net8.0-windows` / `UseWPF`) を使用しているため、GitHub Actions のランナーは Linux (`ubuntu-latest`) ではビルドエラーになります。`.github/workflows/ci-cd.yml` のジョブは必ず **`runs-on: windows-latest`** を維持してください。

## 5. リリースの手順
本プロジェクトの公式リリースパッケージは、タグプッシュ時に GitHub Actions によって自動生成（下書き保存）されます。
1.  リリースする際は、事前に `client/MainWindow.xaml` にハードコードされているバージョン情報（タイトルバーおよび表示ラベル）を新しいバージョン（例: `v2.1.1`）に書き換えてコミット・プッシュします。
2.  対象のコミットに対して `git tag v2.1.1` などのバージョンタグを作成します。
3.  タグをリモートにプッシュします (`git push origin v2.1.1`)。
4.  自動的にビルドが走り、GitHub の Releases ページに `client` / `server` の個別 zip アセットが添付された「下書きリリース」が生成されます。内容を確認し、問題なければ公開してください。

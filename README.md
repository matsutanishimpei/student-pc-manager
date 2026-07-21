# sendCMD - 実習室PC管理システム

`sendCMD` は、教員用Windowsアプリから実習室内の生徒PCをまとめて管理するためのシステムです。PowerShellコマンドの遠隔実行、ファイル配布と無人インストール、稼働監視、画面確認、プロセス終了、Wake on LAN、名簿CSV連携に対応します。

利用範囲は、管理者が運用する信頼済みの教室内ネットワークを想定しています。インターネットへ直接公開しないでください。

## 主な機能

- 複数PCへのPowerShellコマンド一括実行
- MSI、MSIX、APPX、EXEなどの配布とサイレント実行
- 生徒PCのオンライン状態、利用中アプリ、画面、プロセスの確認
- 選択したプロセスの遠隔終了
- PC名による一括登録、IP範囲スキャン、Wake on LAN
- 学生名とPCのCSVインポート・エクスポート
- 本文ハッシュとワンタイムnonceを含むHMAC-SHA256署名によるAPI認証
- UTF-8、UTF-16、CP932のCSV読み込みと、Excel向けCP932出力

## システム構成

```text
student-pc-manager/
├── sendCMD.sln
├── build-release.ps1       # Windows向け配布物の作成
├── share/                  # 通信モデルと署名処理の共有コード
├── server/                 # 生徒PC上で動くWindowsサービス
│   ├── Program.cs          # サービス、通信、設定の起動処理
│   ├── Endpoints/          # コマンド、配布、監視API
│   ├── Middlewares/        # 署名認証
│   ├── Services/           # 実行セッションと常駐処理
│   ├── appsettings.json    # APIキー、保存先、上限値
│   ├── install-secure.ps1
│   ├── configure-api-key.ps1
│   └── uninstall.bat
├── helper/                 # ログインユーザー側の補助プロセス
├── client/                 # 教員PC用WPFアプリ
│   ├── MainWindow.xaml     # メイン画面
│   ├── MainWindow.xaml.cs  # 画面操作の調整
│   ├── RemotePcClient.cs   # 署名付き通信
│   ├── ClientDataStore.cs  # 設定とPC一覧の安全な保存
│   ├── OperationLogger.cs  # UTF-8操作ログ
│   ├── StudentCsvProcessor.cs # CSVと文字コード処理
│   ├── WakeOnLanService.cs
│   └── ViewModels.cs
├── tests/                  # xUnit自動テスト
└── docs/                   # 利用・導入・内部仕様書
```

## クイックスタート

### 配布物を作る

Windows上でリポジトリのルートから次を実行します。

```powershell
.\build-release.ps1
```

成功すると、次の自己完結型ファイルが作成されます。.NETランタイムの別途導入は不要です。

- `publish/client/`: 教員PC用アプリ
- `publish/server/`: 生徒PC用サービス、補助プロセス、導入バッチ

### 生徒PCへ導入する

1. BitLocker To Goで暗号化した管理者用USBへ`publish/server/`一式と、共有キーだけを書いた`api-key.provision`を置きます。
2. 各生徒PCでUSB内の`server\install-next-pc.cmd`をダブルクリックし、管理者確認を許可します。キーはPCへコピーされず、直ちにそのPC用のDPAPI暗号文として保存されます。
3. 最終PCでは`server\install-last-pc.cmd`をダブルクリックし、USB上のキーファイルを自動削除します。
4. 再インストールでは暗号化済み設定が保持されるため、通常はUSBキーが不要です。
5. 教員PCで `publish/client/client.exe` を起動し、同じAPIキーを入力します。
6. 代表PC1台で疎通確認してから対象を広げます。

Active Directory、WinRM、証明書はこの標準導入手順では使用しません。詳しくは[`docs/Deployment_Runbook.md`](docs/Deployment_Runbook.md)を参照してください。

詳細は[生徒PCへの導入手順](docs/Server_Manual.md)と[教員アプリ操作手順](docs/Client_Manual.md)を参照してください。

## セキュリティと制限

- HTTPSは、証明書の発行・更新・失効管理が現在の運用要件に収まらないため、意図的に採用していません。HMACは要求を認証しますが通信を暗号化しないため、信頼済みの閉じた教室LANだけで使用してください。
- 全生徒PCで1つの共有APIキーを使う設計を意図的に維持しています。1台またはUSBから漏えいした場合は、教員クライアントと全生徒PCのキー交換が必要です。
- APIキーそのものは通信で送らず、時刻、nonce、HTTPメソッド、通信先、本文ハッシュ、アップロード元ファイル名ハッシュを含むHMAC-SHA256署名を毎回送ります。同一nonceの再利用は再起動後も拒否されます。
- 生徒PCの共有APIキーはPC単位のDPAPIで暗号化され、設定・nonce履歴・サーバーログはAdministratorsとLocal Systemだけがアクセスできます。
- APIキーは16文字以上が必須です。クライアント設定ではWindowsユーザー単位に暗号化して保存されます。
- ユーザーセッション補助処理の名前付きパイプは、ログオンユーザーとLocal Systemだけに制限されます。
- 教員PCと生徒PCの時刻差が5分を超える要求は拒否されます。Windowsの時刻同期を有効にしてください。
- APIキーが未設定の生徒PCは、管理APIへの要求をすべて拒否します。
- PowerShellコマンドは最大32,768文字です。
- ファイル配布の初期上限は500 MiBです。`MaxUploadBytes` で変更できます。
- 配布ファイル名はサーバー側で無害化し、一意な名前で保存します。保存先外へのパス指定は拒否します。
- 通常のコマンドとインストーラーは強い権限で実行できます。対象PCとコマンドを確認してから実行してください。

これらの設計判断、受け入れるリスク、必須の代替対策は[セキュリティ設計判断](docs/Security_Design_Decisions.md)に記録しています。

## 文字コード

- C#、JSON、Markdownなどのテキストは`.editorconfig`によりUTF-8で統一します。
- `uninstall.bat`は、日本語版コマンドプロンプトとの互換性のためCP932を維持します。新規・再インストールには`install-secure.ps1`を使用します。
- 名簿CSVの読み込みは、UTF-8 BOM付き、UTF-16 LE/BE、UTF-8、CP932を自動判定します。
- 名簿CSVの出力は、Windows版Excelで直接開きやすいCP932です。
- 設定、PC一覧、クライアント操作ログはUTF-8で保存します。

## 運用開始前の確認

1. 代表PC1台で、接続、コマンド実行、監視、ファイル配布を確認します。
2. 日本語を含むCSVの入出力と、アプリ再起動後の設定復元を確認します。
3. 実運用に近い台数で一括操作し、教室ネットワークの負荷を確認します。
4. 実際に配る最大サイズに近いファイルで、所要時間と空き容量を確認します。
5. 一部PCが停止中でも、他のPCの処理と画面操作が継続することを確認します。
6. 生徒PCのログと教員PCの操作ログを確認できることを確認します。

## 開発とテスト

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --no-build --configuration Release
```

開発時のルールは[CONTRIBUTING.md](CONTRIBUTING.md)、ファイル配布の詳細は[ファイル配布仕様](docs/file_distribution_spec.md)、変更履歴は[CHANGELOG.md](CHANGELOG.md)を参照してください。

## 自動テストとリリース

- `main`へのpushと、`main`向けのPull Requestで、Windows上のビルドと全テストを実行します。
- `v*`形式のタグをpushすると、テスト成功後にclient/serverのZIPを作成します。
- ZIPを添付したGitHub Releaseは下書きとして作成されます。内容を確認してから手動で公開します。
- 通常のブランチpushだけではリリースは作成されません。


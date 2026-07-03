# sendCMD - 実習室PC管理用 PowerShell実行・配布システム

`sendCMD` は、教員用PC（クライアントアプリ）から実習室の生徒用PC（Windowsサービス）に対して、管理者権限（SYSTEM）でPowerShellコマンドを遠隔実行したり、ソフトウェアを配布してサイレントインストールしたりするためのシステムです。

---

## 1. フォルダ構成

本プロジェクトは C# (.NET 8) を使用したモノレポ（単一リポジトリ）構成になっています。

```text
d:\dev\sendCMD\
├── sendCMD.sln         # プロジェクト全体を管理するソリューションファイル
├── build-release.ps1   # リリースビルド自動化スクリプト
│
├── share/              # [共有ライブラリ]
│   └── Models.cs       # 通信で使用するデータ（リクエスト/レスポンス）の共通定義
│
├── server/             # [生徒PC用：Windowsサービス（WebAPIサーバー内蔵）]
│   ├── Program.cs      # APIの起動、PowerShell実行、ファイル保存等の主要処理
│   ├── appsettings.json# APIキーやファイル保存先などの設定ファイル
│   ├── install.bat     # サービスの一括自動セットアップスクリプト
│   └── uninstall.bat   # サービスのアンインストール用スクリプト
│
├── client/             # [教員PC用：デスクトップ管理画面アプリ]
│   ├── MainWindow.xaml # ダークテーマの操作画面（WPF）
│   └── MainWindow.xaml.cs# IPリスト一括生成、非同期での並列コマンド送信、ファイルアップロード処理
│
├── docs/               # [手順書・マニュアル]
│   ├── Client_Manual.md# 教員PC側の操作マニュアル（コマンド実行、ファイル配布）
│   └── Server_Manual.md# 生徒PC側のサービス導入マニュアル
│
└── publish/            # [ビルド成果物（ビルド後に生成されます）]
    ├── client/         # 教員PC用スタンドアロンEXE (client.exe)
    └── server/         # 生徒PC用スタンドアロンEXE (server.exe, install.bat, uninstall.bat)
```

---

## 2. クイックスタート (ビルド方法)

開発環境で実行用のファイルをコンパイル（ビルド）するには、以下の手順を行います。

1.  PowerShellを開き、プロジェクトのルートディレクトリに移動します。
2.  以下のビルドスクリプトを実行します。
    ```powershell
    .\build-release.ps1
    ```
3.  ビルドに成功すると、ルート直下に `publish` フォルダが作成され、中に .NET ランタイム不要の独立した `.exe` ファイルが出力されます。

---

## 3. 各マニュアルへのリンク

詳細な導入・操作方法については、以下の手順書を参照してください。

*   **生徒用PCへのセットアップ手順（管理者向け）:**
    [Server_Manual.md](file:///D:/dev/sendCMD/docs/Server_Manual.md)
    *   *バッチファイルを用いた自動セットアップ手順、ポート開放、サービスの管理方法について記述されています。*
*   **教員用PCでの操作手順（教員向け）:**
    [Client_Manual.md](file:///D:/dev/sendCMD/docs/Client_Manual.md)
    *   *PC名を用いた一括自動生成、コマンド実行、ファイルのサイレントインストール手順について記述されています。*

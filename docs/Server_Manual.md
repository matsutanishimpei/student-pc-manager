# sendCMD 導入手順書（生徒PC）

この文書は、生徒PCへ`sendCMD` Windowsサービスを導入し、教員PCから管理できる状態にする手順を説明します。

## 1. 前提条件

- 対応OS: 64ビット版Windows 10/11またはWindows Server
- 導入作業: ローカル管理者権限が必要
- 通信: 教員PCから生徒PCのTCP 5000番へ接続可能であること
- 時刻: 教員PCとの時刻差が5分以内であること
- 配置範囲: 信頼済みの教室内ネットワークのみ。インターネットへ直接公開しないこと

HTTPSを採用しないことと、全PCで1つの共有APIキーを使用することは意図的な設計判断です。HTTPは通信内容を暗号化しないため、信頼できないネットワークでは使用できません。判断の理由、受け入れるリスク、漏えい時の対応は[セキュリティ設計判断](Security_Design_Decisions.md)を参照してください。

## 2. 設定

APIキーは`appsettings.json`へ直接記入しません。管理者PowerShellから登録スクリプトへ対話入力するか、暗号化USB上の一時プロビジョニングファイルを指定します。

```json
{
  "ApiKey": "",
  "ProtectedApiKey": "DPAPI暗号文（登録スクリプトが設定）",
  "UploadDirectory": "C:\\Users\\Public\\sendCMD_uploads",
  "MaxUploadBytes": 524288000
}
```

- 共有APIキーは16文字以上が必須です。PC単位のDPAPIで暗号化され、AdministratorsとLocal Systemだけが設定ファイルへアクセスできます。
- `UploadDirectory`: 配布ファイルの一時保存先です。既定値は`C:\Users\Public\sendCMD_uploads`です。
- `MaxUploadBytes`: 1ファイルの上限バイト数です。既定値`524288000`は500 MiBです。
- `ExcludeProcesses`: 監視画面から除外するプロセス名です。

APIキーそのものは通信へ送られません。各要求にUTC時刻、ワンタイムnonce、HTTPメソッド、パス、本文ハッシュ、アップロード元ファイル名ハッシュを含むHMAC-SHA256署名を付けます。nonce履歴は短期間ディスクへ保持されます。

リリースパッケージには初期キーを含めません。

## 3. インストール

1. `server.exe`、`sendCMD_helper.exe`、`appsettings.json`、`install-secure.ps1`、`configure-api-key.ps1`が同じフォルダにあることを確認します。
2. 管理者PowerShellで`Set-ExecutionPolicy -Scope Process Bypass`を実行します。
3. `./install-secure.ps1`を実行し、共有APIキーを安全な対話入力画面へ入力します。USB運用では後述の`-ApiKeyFile`を使います。
4. 再インストール時は既存の暗号化キーが保持されます。変更も行う場合は`./install-secure.ps1 -ReconfigureApiKey`を使います。

### Active Directoryなしで複数台へ導入

BitLocker To Goで暗号化した管理者専用USBに、配布物一式と共有キーだけを1行で記載した`api-key.provision`を置きます。各PCでは`server\install-next-pc.cmd`をダブルクリックし、管理者確認を許可します。最終PCだけは`server\install-last-pc.cmd`を使用します。

```powershell
Set-ExecutionPolicy -Scope Process Bypass
./install-secure.ps1 -ApiKeyFile E:\api-key.provision -KeepProvisioningFile
```

キーはUSBから直接読み取られ、PCへ平文ファイルとしてコピーされません。PC上にはDPAPI暗号文だけが残ります。`-KeepProvisioningFile`は次のPCでも使うためUSB上のファイルを保持する指定です。

最終PCでは次のように保持指定を外し、登録成功後にUSB上のキーファイルを自動削除します。

```powershell
./install-secure.ps1 -ApiKeyFile E:\api-key.provision
```

USB紛失時にキーを読まれないようBitLocker To Goを必須とし、USBは作業中も管理者が携行してください。SSDやUSBでは通常削除だけで物理的な完全消去を保証できないため、暗号化が重要です。Active Directory、WinRM、証明書は不要です。

導入処理は次を行います。

- `C:\Program Files\sendCMD`へのファイル配置
- TCP 5000番の受信ファイアウォール規則追加
- `sendCMD` Windowsサービスの自動起動登録
- ログインユーザー用補助プロセスの登録
- サービスの開始

導入後は管理者用PowerShellで状態を確認します。

```powershell
sc.exe query sendCMD
Test-NetConnection localhost -Port 5000
```

ブラウザーまたはPowerShellから`http://localhost:5000/`へ接続し、`sendCMD Server is running.`が返れば待受処理は動いています。管理APIの確認には署名が必要です。

## 4. サービス管理

```powershell
sc.exe stop sendCMD
sc.exe start sendCMD
sc.exe query sendCMD
```

設定変更後は停止と開始を行ってください。完全に削除する場合は、配布物の`uninstall.bat`を管理者として実行します。

## 5. 保存と安全対策

- PowerShellコマンドは空入力を拒否し、最大32,768文字に制限します。
- アップロードは`MaxUploadBytes`を超えると拒否します。
- 受信したファイル名からフォルダー部分と不正文字を除去します。
- 保存名へ一意な接尾辞を付け、既存ファイルを上書きしません。
- 保存先の外へ出るパスは拒否します。
- 配布処理後の一時ファイルはクライアントが削除コマンドを実行します。異常終了時は残る場合があるため、定期的に保存先容量を確認してください。

`UploadDirectory`を変更する場合は、SYSTEMとログインユーザーの双方が必要なアクセスを行える場所を選んでください。

## 6. ログ

サーバーログは次へ記録されます。

```text
C:\Users\Public\sendCMD_server_log.txt
```

主な内容は、サービスと補助処理の起動、コマンド実行、実行セッション、アップロード先とサイズ、外部プロセスのエラーです。コマンド本文とAPIキーは記録せず、コマンドは長さと照合用フィンガープリントだけを記録します。ログが10MBに達すると`.1`へ自動ローテーションします。

APIキー未設定時は起動警告が残り、管理APIは認証失敗になります。

## 7. APIキーの変更

台数が少ない場合は各PCで次を行います。

1. 管理者PowerShellで`C:\Program Files\sendCMD\configure-api-key.ps1`を実行します。
2. 新しい共有キーを対話入力します。設定保存とサービス再起動は自動で行われます。
3. 教員アプリへ同じキーを入力します。
5. 代表PCで疎通を確認してから残りへ反映します。

一括更新する場合は、現在のキーで接続できる間にSYSTEM権限で設定変更スクリプトを配布します。全PCの結果を確認するまでは、教員アプリ側のキーを切り替えないでください。失敗したPCは個別対応が必要です。

設定更新スクリプトでは、JSONを構造として読み書きしてください。

## 8. トラブルシューティング

### サービスが起動しない

- `C:\Program Files\sendCMD`に必要ファイルが揃っているか確認します。
- `appsettings.json`が正しいJSONか確認します。
- Windowsイベントログとサーバーログを確認します。

### 教員PCから接続できない

- `sc.exe query sendCMD`でサービス状態を確認します。
- TCP 5000番の受信規則とネットワークプロファイルを確認します。
- 教員PCからPC名またはIPアドレスへ到達できるか確認します。

### 認証に失敗する

- 両側のAPIキーを確認します。前後の空白や大文字小文字も区別されます。
- APIキーが空でないことを確認します。
- Windowsの日時と時刻同期を確認します。許容差は5分です。
- 設定変更後にサービスを再起動したか確認します。

### ファイル配布に失敗する

- サイズが`MaxUploadBytes`以下か確認します。
- `UploadDirectory`の空き容量と権限を確認します。
- サーバーログでHTTP 413相当のサイズ超過や保存エラーを確認します。

## 9. 導入確認

1. サービスが自動起動になっていることを確認します。
2. TCP 5000番が教員PCから到達可能であることを確認します。
3. APIキー設定と時刻同期を確認します。
4. 教員アプリからPC情報、軽いコマンド、監視を確認します。
5. 小さなファイルを配布して、ログと後片付けを確認します。
6. PC再起動後も接続できることを確認します。
7. 本番台数と本番相当サイズで運用試験を行います。

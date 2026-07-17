# sendCMD 導入手順書（生徒PC）

この文書は、生徒PCへ`sendCMD` Windowsサービスを導入し、教員PCから管理できる状態にする手順を説明します。

## 1. 前提条件

- 対応OS: 64ビット版Windows 10/11またはWindows Server
- 導入作業: ローカル管理者権限が必要
- 通信: 教員PCから生徒PCのTCP 5000番へ接続可能であること
- 時刻: 教員PCとの時刻差が5分以内であること
- 配置範囲: 信頼済みの教室内ネットワークのみ。インターネットへ直接公開しないこと

## 2. 設定

配布された`server`フォルダの`appsettings.json`を編集します。

```json
{
  "ApiKey": "教室ごとの十分に長い共有キー",
  "UploadDirectory": "C:\\Users\\Public\\sendCMD_uploads",
  "MaxUploadBytes": 524288000
}
```

- `ApiKey`: 教員PCと共有する秘密情報です。空の場合、管理APIはすべて拒否されます。
- `UploadDirectory`: 配布ファイルの一時保存先です。既定値は`C:\Users\Public\sendCMD_uploads`です。
- `MaxUploadBytes`: 1ファイルの上限バイト数です。既定値`524288000`は500 MiBです。
- `ExcludeProcesses`: 監視画面から除外するプロセス名です。

APIキーそのものは通信へ送られません。各要求にUTC時刻、HTTPメソッド、パスを含むHMAC-SHA256署名を付けます。キーを変更した場合はサービス再起動が必要です。

リリースパッケージに初期キーが含まれている場合も、実運用前に教室ごとのキーへ変更してください。

## 3. インストール

1. `server.exe`、`sendCMD_helper.exe`、`appsettings.json`、`install.bat`が同じフォルダにあることを確認します。
2. `appsettings.json`を設定します。
3. `install.bat`を右クリックし、「管理者として実行」を選びます。
4. 完了メッセージを確認します。

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

主な内容は、サービスと補助処理の起動、コマンド実行、実行セッション、アップロード先とサイズ、外部プロセスのエラーです。定期監視の正常応答は、肥大化を防ぐため記録しません。APIキーは記録しません。

APIキー未設定時は起動警告が残り、管理APIは認証失敗になります。

## 7. APIキーの変更

台数が少ない場合は各PCで次を行います。

1. `C:\Program Files\sendCMD\appsettings.json`を管理者権限で開きます。
2. `ApiKey`を変更して保存します。
3. `sendCMD`サービスを再起動します。
4. 教員アプリへ同じキーを入力します。
5. 代表PCで疎通を確認してから残りへ反映します。

一括更新する場合は、現在のキーで接続できる間にSYSTEM権限で設定変更スクリプトを配布します。全PCの結果を確認するまでは、教員アプリ側のキーを切り替えないでください。失敗したPCは個別対応が必要です。

設定更新スクリプトでは、JSONを構造として読み書きしてください。

```powershell
$path = "C:\Program Files\sendCMD\appsettings.json"
$json = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
$json.ApiKey = "新しい教室用キー"
$json | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8
Start-Process powershell.exe `
    -ArgumentList "-NoProfile -Command 'Start-Sleep -Seconds 2; Restart-Service sendCMD -Force'" `
    -WindowStyle Hidden
```

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


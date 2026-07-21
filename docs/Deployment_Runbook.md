# sendCMD USB導入・再インストール運用手順書

この手順は、Active Directory、WinRM、証明書を使わず、管理者が暗号化USBを持って各生徒PCへ導入する運用を対象にします。全PCでは同じ共有APIキーを使用します。

HTTPSを採用しないことと全PC共通APIキーは、運用要件に基づく意図的な設計判断です。HTTP通信は暗号化されないため信頼済みの閉じた教室LANだけで使用し、共有キー漏えい時は全台で交換します。詳細は[セキュリティ設計判断](Security_Design_Decisions.md)を確認してください。

## 1. 事前準備

- 教員PCと各生徒PCでローカル管理者権限を使用できること
- 教員PCから生徒PCのTCP 5000番へ接続できること
- PC間の時刻差が5分以内であること
- 管理者専用USBをBitLocker To Goで暗号化していること
- 16文字以上、推測困難な共有APIキーを用意していること

USB紛失時の漏えいを防ぐ主な保護はBitLockerです。通常のファイル削除は、USBメモリ上のデータを物理的に完全消去する保証にはなりません。

## 2. USBの準備

1. リポジトリのルートで`build-release.ps1`を実行します。
2. `publish\server`フォルダーを暗号化USBへコピーします。
3. USB直下に`api-key.provision`を作り、共有APIキーだけをUTF-8の1行で記載します。
4. 先頭・末尾の空白や空行がないことを確認します。
5. 同じ共有APIキーを教員クライアントへ登録します。

配布用フォルダーの`appsettings.json`へ平文キーを書かないでください。キーをコマンドライン引数として直接指定することも避けます。

## 3. 各生徒PCへの初回導入

USBを挿し、USB内の`server\install-next-pc.cmd`をダブルクリックします。Windowsの管理者確認画面で「はい」を選びます。

コマンド画面には成功または失敗が表示され、キー入力は求められません。成功を確認して何かキーを押すと画面が閉じます。

PowerShellから実行する場合は、以下も使用できます。USBが`E:`の場合の例です。

```powershell
Set-ExecutionPolicy -Scope Process Bypass
E:\server\install-secure.ps1 `
  -ApiKeyFile E:\api-key.provision `
  -KeepProvisioningFile
```

この処理では、キーをUSBから直接読み、PC固有のDPAPI暗号文として`C:\Program Files\sendCMD\appsettings.json`へ保存します。設定ファイルはAdministratorsとLocal Systemだけがアクセスできます。平文キーファイルは生徒PCへコピーされません。

成功メッセージを確認してからUSBを取り外し、次のPCへ進みます。作業中、USBを生徒へ渡したり、無人の場所へ置いたりしないでください。

## 4. 最終PCとUSB上のキー削除

最終PCでは、USB内の`server\install-last-pc.cmd`をダブルクリックして管理者確認を許可します。登録成功後にUSB上のキーが自動削除されます。

PowerShellから実行する場合は`-KeepProvisioningFile`を外します。

```powershell
Set-ExecutionPolicy -Scope Process Bypass
E:\server\install-secure.ps1 -ApiKeyFile E:\api-key.provision
```

DPAPI保存と設定ACLの適用が成功した後、`api-key.provision`が自動削除されます。削除失敗が表示された場合はUSBを持ち帰り、管理者の管理下で削除してください。USBを廃棄・転用する場合はBitLockerの再初期化も行います。

## 5. 導入確認

各PCで次を確認します。

```powershell
Get-Service sendCMD
Test-NetConnection localhost -Port 5000
```

- `sendCMD`サービスが実行中である
- TCP 5000番が待受中である
- 教員クライアントからPC情報を取得できる
- 代表PCでコマンド実行と小さなファイル配布に成功する

## 6. 再インストール

同じPCへ同じキーのまま再インストールする場合、DPAPI暗号文は保持されるためUSB上のキーファイルは不要です。

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-secure.ps1
```

OS初期化、PC交換、設定消失時は初回導入と同じUSB手順が必要です。

## 7. 共有APIキーの変更

新しいキーを記載した暗号化USBを用意し、各PCで次を実行します。複数台の途中では`-KeepProvisioningFile`を付け、最終PCでは外します。

```powershell
.\install-secure.ps1 `
  -ApiKeyFile E:\api-key.provision `
  -KeepProvisioningFile `
  -ReconfigureApiKey
```

変更中は新旧キーのPCが混在するため、全台が完了するまで通常の一括操作を止めます。全PCの変更後、教員クライアントも新しいキーへ更新して疎通確認します。

## 8. 障害時の対応

- キーファイル形式エラー: 共有キーだけの1行にし、前後の空白を除き、UTF-8で保存します。
- 管理者権限エラー: PowerShellを「管理者として実行」します。
- 認証失敗: 教員PCと生徒PCが同じ共有キーであること、時刻差が5分以内であることを確認します。
- サービス起動失敗: `C:\Users\Public\sendCMD_server_log.txt`を管理者権限で確認します。
- USB紛失: 共有キーが漏えいした前提で新しいキーを作り、全生徒PCと教員クライアントでローテーションします。

## 9. 作業チェックリスト

- [ ] USBをBitLocker To Goで暗号化した
- [ ] キーは16文字以上で、USB上の専用ファイルだけに記載した
- [ ] `appsettings.json`へ平文キーを書いていない
- [ ] 各PCで成功表示とサービス起動を確認した
- [ ] 最終PCで`-KeepProvisioningFile`を外した
- [ ] USB上から`api-key.provision`が消えたことを確認した
- [ ] 教員クライアントから代表PCへ接続確認した
- [ ] USBを管理者の管理場所へ戻した

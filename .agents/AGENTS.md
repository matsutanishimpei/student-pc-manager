# sendCMD Project Guidelines (do not repeat mistakes)

## 1. Git Operations
- **Never** push commits, create branches, or open pull requests without explicit user approval. All Git actions must be prompted and confirmed by the USER.

## 2. Windows Service Session Handling
- The server runs as a Windows Service under the `SYSTEM` account (Session 0). GUI‑dependent operations (screen capture, querying window titles, etc.) **cannot** be performed directly in Session 0.
- Always execute such operations in the **interactive user session** using the `InteractiveProcessHelper.RunInUserSession` helper (CreateProcessAsUser) instead of `schtasks.exe`.

## 3. File Paths & Permissions
- Temporary files, logs, and script files **must** be placed in `C:\Users\Public\` where all users have write permissions.
- **Never** rely on `Path.GetTempPath()` when code may run under SYSTEM, as it resolves to `C:\Windows\Temp\` which standard users cannot write to.

## 4. Batch Files Encoding (Japanese Windows)
- Save `install.bat` and `uninstall.bat` in **Shift_JIS (CP932)** encoding.
- Do **not** add `chcp 65001` or set UTF‑8 code page inside these batch files.

## 5. PowerShell Multi‑Statement Assignments
- When assigning the result of a multi‑statement PowerShell block to a variable, wrap the block in `$()` to capture the full output, e.g.:
  ```powershell
  $result = $( $a = Get-Process; if ($a) { ConvertTo-Json $a } else { '[]' } )
  ```

## 6. Logging
- Use the `WriteLog(string message)` helper to record diagnostics to `C:\Users\Public\sendCMD_server_log.txt`.
- Include clear prefixes (e.g., `[CreateProcessAsUser FAILED]`) for easier troubleshooting.

## 7. Rebuilding & Reinstalling
- After any change that affects session handling, run the publish script (`dotnet publish ...`) and reinstall via `install.bat` **as Administrator**.
- Verify the `publish/server` directory contains the freshly built binaries before reinstalling.

---
*These rules are stored in `.agents/AGENTS.md` to ensure they are automatically applied to future work on the sendCMD project.*

# sendCMD Project Development Guidelines for AI Agents

When working on the **sendCMD** project (student-pc-manager), you must strictly follow these platform and architecture guidelines to avoid regression bugs related to Windows system administration, service isolation, and localized environments.

---

## 1. Windows Service Session 0 Isolation & Desktop Attachment
*   **The Barrier:** The server component runs as a Windows Service under the `SYSTEM` account (Session 0).
*   **The Constraint:** Session 0 cannot directly capture the screen (returns blank/black screen) or query active application window titles (e.g., `MainWindowTitle` from `Get-Process` will return empty/null due to desktop window handle isolation).
*   **The Solution:** You must bypass Session 0 by running GUI-dependent queries or capture routines inside a scheduled task under the active user session context:
    ```cmd
    schtasks.exe /create /tn "TaskName" /tr "powershell.exe ..." /sc ONCE /ru INTERACTIVE /f
    ```

---

## 2. Standard User (Non-Admin) Permissions & Temp Folders
*   **The Constraint:** The scheduled task registered with `/ru INTERACTIVE` runs in the context of the logged-in student.
*   **The Risk:** In typical educational/corporate environments, students are standard users without administrative rights. Standard users **cannot write** files to system-restricted directories like `C:\Windows\Temp\` or `C:\Program Files\`.
*   **The Solution:** Always write temporary output files (PNG captures, JSON processes, temporary scripts) to a public directory where **Everyone** has write permissions by default on Windows:
    *   **Use:** `C:\Users\Public\`
    *   *Do NOT use `Path.GetTempPath()` (which resolves to `C:\Windows\Temp\` when run under SYSTEM, causing permission denied errors when the interactive task attempts to write to it).*

---

## 3. Robust Task Execution via Script Files (Escaping Cautions)
*   **The Risk:** Passing complex PowerShell commands directly inside the `/tr` parameter of `schtasks /create` is extremely fragile because Windows command-line double-quote escaping (`\"`) often breaks during task registration.
*   **The Solution:**
    1. Write the PowerShell script content to a temporary file: `C:\Users\Public\script_[GUID].ps1`
    2. Register the task to execute that file using the `-File` parameter:
       ```cmd
       schtasks.exe /create /tn "TaskName" /tr "powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"C:\Users\Public\script_[GUID].ps1\"" /sc ONCE /ru INTERACTIVE /f
       ```
    3. Run the task, wait for the output file to appear, then delete both the task and the `.ps1` script file.

---

## 4. PowerShell Multi-Statement Assignment Syntax
*   **The Risk:** In PowerShell, when assigning a multi-statement sequence to a variable using a semicolon (e.g., `$r = statement1; statement2;`), only `statement1` gets assigned to `$r`. Subsequent statements are executed but their output is lost from the variable, leading to serialization/parsing errors (like `'P' is an invalid start of a value`).
*   **The Solution:** Wrap multi-statement sequences in a subexpression `$()` to ensure the entire execution block's output is assigned:
    ```powershell
    $r = $( $p = Get-Process | ...; if ($p) { ConvertTo-Json ... } else { '[]' } ); Out-File ...
    ```

---

## 5. Japanese Windows Console Encoding (Shift_JIS / CP932)
*   **The Constraint:** Japanese Windows Command Prompt (`cmd.exe`) uses Shift_JIS (Code Page 932) by default.
*   **The Risk:** If batch files (`.bat` or `.cmd`) containing Japanese characters are saved in UTF-8, executing them in cmd.exe will cause severe character corruption (mojibake).
*   **The Solution:**
    *   Save all `.bat` files (such as `install.bat` and `uninstall.bat`) in **Shift_JIS (CP932 / ANSI)** encoding.
    *   Do NOT use `chcp 65001` (UTF-8 code page redirection) in batch files, as it causes font display issues and rendering glitches on standard Japanese Windows console screens.

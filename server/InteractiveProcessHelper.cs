using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

/// <summary>
/// Windows Service (Session 0) からアクティブなユーザーセッションの
/// 対話型デスクトップ (winsta0\default) 上でプロセスを起動するヘルパー。
/// schtasks.exe では対話型デスクトップへのアタッチが不完全なため、
/// WTS API + CreateProcessAsUser を使用して確実にデスクトップアクセスを実現する。
/// </summary>
public static class InteractiveProcessHelper
{
    // --- Win32 API P/Invoke 宣言 ---

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes,
        int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken, string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(
        out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    // --- 構造体 ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars, dwFillAttribute;
        public int dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    // --- 定数 ---
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;
    private const uint WAIT_FAILED = 0xFFFFFFFF;

    /// <summary>
    /// アクティブなコンソールセッションの対話型デスクトップ (winsta0\default) 上で
    /// 指定されたコマンドラインを実行します。
    /// </summary>
    /// <param name="commandLine">実行するコマンドライン</param>
    /// <param name="timeoutMs">タイムアウト（ミリ秒）。デフォルト15秒。</param>
    /// <param name="wait">プロセスの完了を待機するかどうか。デフォルトは true。</param>
    /// <returns>成功可否とエラーメッセージのタプル</returns>
    public static (bool Success, string Error) RunInUserSession(string commandLine, uint timeoutMs = 15000, bool wait = true, CancellationToken cancellationToken = default)
    {
        IntPtr userToken = IntPtr.Zero;
        IntPtr dupToken = IntPtr.Zero;
        IntPtr envBlock = IntPtr.Zero;

        try
        {
            // 1. アクティブなコンソールセッションIDを取得
            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
                return (false, "No active console session found (sessionId=0xFFFFFFFF)");

            // 2. そのセッションのユーザートークンを取得
            if (!WTSQueryUserToken(sessionId, out userToken))
                return (false, $"WTSQueryUserToken failed for session {sessionId}: Win32Error={Marshal.GetLastWin32Error()}");

            // 3. トークンを複製（CreateProcessAsUser にはプライマリトークンが必要）
            if (!DuplicateTokenEx(userToken, MAXIMUM_ALLOWED, IntPtr.Zero,
                SecurityImpersonation, TokenPrimary, out dupToken))
                return (false, $"DuplicateTokenEx failed: Win32Error={Marshal.GetLastWin32Error()}");

            // 4. ユーザーの環境変数ブロックを作成
            CreateEnvironmentBlock(out envBlock, dupToken, false);

            // 5. STARTUPINFO を構成（対話型デスクトップを明示的に指定）
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "winsta0\\default";

            // 6. ユーザーのセッション上でプロセスを起動
            uint flags = CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW;
            if (!CreateProcessAsUser(dupToken, null, commandLine,
                IntPtr.Zero, IntPtr.Zero, false, flags,
                envBlock != IntPtr.Zero ? envBlock : IntPtr.Zero,
                null, ref si, out PROCESS_INFORMATION pi))
                return (false, $"CreateProcessAsUser failed: Win32Error={Marshal.GetLastWin32Error()}");

            // 7. プロセスの完了を待機
            if (wait)
            {
                var stopwatch = Stopwatch.StartNew();
                uint waitResult;
                do
                {
                    if (cancellationToken.IsCancellationRequested || stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        TerminateProcess(pi.hProcess, 1);
                        WaitForSingleObject(pi.hProcess, 2000);
                        CloseHandle(pi.hThread);
                        CloseHandle(pi.hProcess);
                        return (false, cancellationToken.IsCancellationRequested
                            ? "Process cancelled because the client disconnected"
                            : $"Process timed out after {timeoutMs} ms");
                    }
                    waitResult = WaitForSingleObject(pi.hProcess, 100);
                }
                while (waitResult == WAIT_TIMEOUT);

                if (waitResult == WAIT_TIMEOUT)
                {
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                    return (false, $"Process timed out after {timeoutMs} ms");
                }

                if (waitResult == WAIT_FAILED)
                {
                    int error = Marshal.GetLastWin32Error();
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                    return (false, $"WaitForSingleObject failed: Win32Error={error}");
                }

                if (waitResult != WAIT_OBJECT_0)
                {
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                    return (false, $"WaitForSingleObject returned unexpected status: 0x{waitResult:X8}");
                }
            }

            // 8. ハンドルをクリーンアップ
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
        finally
        {
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            if (dupToken != IntPtr.Zero) CloseHandle(dupToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }
}

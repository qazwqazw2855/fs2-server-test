$ErrorActionPreference = "Stop"

function Get-God2RepoRoot {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
}

function Write-God2AtomicJson {
    param(
        [Parameter(Mandatory = $true)] [object] $Value,
        [Parameter(Mandatory = $true)] [string] $Path
    )
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    $json = $Value | ConvertTo-Json -Depth 16
    $encoding = [Text.UTF8Encoding]::new($false)
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        $temp = Join-Path $directory ([System.IO.Path]::GetFileName($fullPath) + "." + [Guid]::NewGuid().ToString("N") + ".tmp")
        $backup = $temp + ".bak"
        try {
            # A fresh absolute temporary path per retry prevents a successful or externally
            # removed temp file from poisoning all subsequent atomic-write attempts.
            [System.IO.File]::WriteAllText($temp, $json, $encoding)
            if ([System.IO.File]::Exists($fullPath)) {
                [System.IO.File]::Replace($temp, $fullPath, $backup)
            }
            else {
                [System.IO.File]::Move($temp, $fullPath)
            }

            return
        }
        catch {
            $baseException = $_.Exception.GetBaseException()
            if ($attempt -eq 8 -or
                ($baseException -isnot [System.IO.IOException] -and
                 $baseException -isnot [System.UnauthorizedAccessException])) {
                throw
            }

            Start-Sleep -Milliseconds (10 * $attempt)
        }
        finally {
            if (Test-Path -LiteralPath $temp) {
                Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
            }
            if (Test-Path -LiteralPath $backup) {
                Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Read-God2JsonWithRetry {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [int] $MaximumAttempts = 8,
        [int] $InitialDelayMilliseconds = 20
    )

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
        }
        catch {
            if ($attempt -eq $MaximumAttempts) {
                throw
            }

            Start-Sleep -Milliseconds ($InitialDelayMilliseconds * $attempt)
        }
    }
}

function Initialize-God2NativeApi {
    if ("God2Automation.NativeApi" -as [type]) {
        return
    }

    Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace God2Automation
{
    public static class NativeApi
    {
        private const UInt32 PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const UInt32 PROCESS_VM_READ = 0x0010;
        private const UInt32 TOKEN_QUERY = 0x0008;
        private const int TokenIntegrityLevel = 25;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(UInt32 access, bool inherit, UInt32 processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, UInt32 desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UInt32 nSize, out UIntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        public static extern UInt32 WTSGetActiveConsoleSessionId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetThreadDesktop(UInt32 threadId);

        [DllImport("kernel32.dll")]
        public static extern UInt32 GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, System.Text.StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, UInt32 uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PostMessage(IntPtr hWnd, UInt32 msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, UInt32 msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern UInt32 GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(UInt32 idThread);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadKeyboardLayout(string pwszKLID, UInt32 flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, UInt32 nFlags);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        public static extern UInt32 SendInput(UInt32 nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern UInt32 MapVirtualKey(UInt32 uCode, UInt32 uMapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        private delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern UInt32 GetWindowThreadProcessId(IntPtr hWnd, out UInt32 processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(UInt32 idAttach, UInt32 idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT pt, UInt32 uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public UInt32 type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public UInt32 mouseData;
            public UInt32 dwFlags;
            public UInt32 time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public UInt16 wVk;
            public UInt16 wScan;
            public UInt32 dwFlags;
            public UInt32 time;
            public IntPtr dwExtraInfo;
        }

        public sealed class RectInfo
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Right { get; set; }
            public int Bottom { get; set; }
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        public sealed class WindowInfo
        {
            public long Hwnd { get; set; }
            public string ClassName { get; set; }
            public string Title { get; set; }
            public int ControlId { get; set; }
            public bool IsVisible { get; set; }
            public bool IsEnabled { get; set; }
            public UInt32 ThreadId { get; set; }
            public UInt32 ProcessId { get; set; }
            public RectInfo Bounds { get; set; }
            public List<WindowInfo> Children { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_MANDATORY_LABEL
        {
            public SID_AND_ATTRIBUTES Label;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SID_AND_ATTRIBUTES
        {
            public IntPtr Sid;
            public UInt32 Attributes;
        }

        public static int GetIntegrityRid(int pid)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (UInt32)pid);
            if (process == IntPtr.Zero) return -1;
            try
            {
                IntPtr token;
                if (!OpenProcessToken(process, TOKEN_QUERY, out token)) return -2;
                try
                {
                    int length = 0;
                    GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out length);
                    IntPtr buffer = Marshal.AllocHGlobal(length);
                    try
                    {
                        if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out length)) return -3;
                        TOKEN_MANDATORY_LABEL label = (TOKEN_MANDATORY_LABEL)Marshal.PtrToStructure(buffer, typeof(TOKEN_MANDATORY_LABEL));
                        SecurityIdentifier sid = new SecurityIdentifier(label.Label.Sid);
                        string[] parts = sid.Value.Split('-');
                        return Int32.Parse(parts[parts.Length - 1]);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        public static byte[] ReadProcessBytes(int pid, UInt32 address, int length)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, (UInt32)pid);
            if (process == IntPtr.Zero) throw new InvalidOperationException("OpenProcess ReadProcessBytes failed: " + Marshal.GetLastWin32Error());
            try
            {
                byte[] buffer = new byte[length];
                UIntPtr read;
                if (!ReadProcessMemory(process, new IntPtr(unchecked((int)address)), buffer, (UInt32)length, out read))
                {
                    throw new InvalidOperationException("ReadProcessMemory failed: " + Marshal.GetLastWin32Error());
                }
                int count = checked((int)read.ToUInt32());
                if (count == buffer.Length) return buffer;
                byte[] sliced = new byte[count];
                Array.Copy(buffer, sliced, count);
                return sliced;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        public static string GetDesktopName()
        {
            IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
            if (desktop == IntPtr.Zero) return "";
            var builder = new System.Text.StringBuilder(256);
            int needed;
            if (!GetUserObjectInformation(desktop, 2, builder, builder.Capacity, out needed)) return "";
            return builder.ToString();
        }

        private static string ReadClassName(IntPtr hwnd)
        {
            var builder = new System.Text.StringBuilder(256);
            int length = GetClassName(hwnd, builder, builder.Capacity);
            return length <= 0 ? "" : builder.ToString();
        }

        private static string ReadWindowText(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            var builder = new System.Text.StringBuilder(Math.Max(length + 1, 256));
            int copied = GetWindowText(hwnd, builder, builder.Capacity);
            return copied <= 0 ? "" : builder.ToString();
        }

        private static RectInfo ReadBounds(IntPtr hwnd)
        {
            RECT rect;
            if (!GetWindowRect(hwnd, out rect))
            {
                return new RectInfo();
            }

            return new RectInfo
            {
                Left = rect.Left,
                Top = rect.Top,
                Right = rect.Right,
                Bottom = rect.Bottom
            };
        }

        public static WindowInfo DescribeWindow(IntPtr hwnd)
        {
            UInt32 pid;
            UInt32 tid = GetWindowThreadProcessId(hwnd, out pid);
            return new WindowInfo
            {
                Hwnd = hwnd.ToInt64(),
                ClassName = ReadClassName(hwnd),
                Title = ReadWindowText(hwnd),
                ControlId = GetDlgCtrlID(hwnd),
                IsVisible = IsWindowVisible(hwnd),
                IsEnabled = IsWindowEnabled(hwnd),
                ThreadId = tid,
                ProcessId = pid,
                Bounds = ReadBounds(hwnd),
                Children = new List<WindowInfo>()
            };
        }

        public static WindowInfo DescribeWindowTree(IntPtr hwnd)
        {
            WindowInfo root = DescribeWindow(hwnd);
            var children = new List<WindowInfo>();
            EnumChildWindows(hwnd, delegate(IntPtr child, IntPtr lParam)
            {
                children.Add(DescribeWindow(child));
                return true;
            }, IntPtr.Zero);
            root.Children = children;
            return root;
        }

        public static List<WindowInfo> DescribeProcessWindows(int processId)
        {
            var windows = new List<WindowInfo>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                UInt32 pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == (UInt32)processId)
                {
                    windows.Add(DescribeWindowTree(hwnd));
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        public static long GetFocusedWindowFor(IntPtr hwnd)
        {
            UInt32 pid;
            UInt32 targetThread = GetWindowThreadProcessId(hwnd, out pid);
            UInt32 currentThread = GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (targetThread != 0 && targetThread != currentThread)
                {
                    attached = AttachThreadInput(currentThread, targetThread, true);
                }

                return GetFocus().ToInt64();
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThread, targetThread, false);
                }
            }
        }

        public static WindowInfo DescribeWindowAtScreenPoint(IntPtr root, int x, int y)
        {
            POINT screen = new POINT();
            screen.X = x;
            screen.Y = y;
            IntPtr hwnd = WindowFromPoint(screen);
            if (hwnd == IntPtr.Zero)
            {
                return new WindowInfo { Hwnd = 0, ClassName = "", Title = "", ControlId = 0, IsVisible = false, IsEnabled = false, Bounds = new RectInfo(), Children = new List<WindowInfo>() };
            }

            return DescribeWindow(hwnd);
        }

        private const UInt32 INPUT_MOUSE = 0;
        private const UInt32 INPUT_KEYBOARD = 1;
        private const UInt32 MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const UInt32 MOUSEEVENTF_LEFTUP = 0x0004;
        private const UInt32 MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const UInt32 MOUSEEVENTF_RIGHTUP = 0x0010;
        private const UInt32 KEYEVENTF_KEYUP = 0x0002;

        public static void Click(int x, int y)
        {
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(50);
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            inputs[1].type = INPUT_MOUSE;
            inputs[1].U.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(150);
        }

        public static void RealClick(int x, int y, int hoverMs, int holdMs, int afterMs)
        {
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(hoverMs);
            INPUT[] down = new INPUT[1];
            down[0].type = INPUT_MOUSE;
            down[0].U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            SendInput(1, down, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(holdMs);
            INPUT[] up = new INPUT[1];
            up[0].type = INPUT_MOUSE;
            up[0].U.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            SendInput(1, up, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(afterMs);
        }

        public static void RealRightClick(int x, int y, int hoverMs, int holdMs, int afterMs)
        {
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(hoverMs);
            INPUT[] down = new INPUT[1];
            down[0].type = INPUT_MOUSE;
            down[0].U.mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
            SendInput(1, down, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(holdMs);
            INPUT[] up = new INPUT[1];
            up[0].type = INPUT_MOUSE;
            up[0].U.mi.dwFlags = MOUSEEVENTF_RIGHTUP;
            SendInput(1, up, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(afterMs);
        }

        public static void RealDrag(
            int fromX,
            int fromY,
            int toX,
            int toY,
            int hoverMs,
            int holdMs,
            int moveStepMs,
            int afterMs)
        {
            SetCursorPos(fromX, fromY);
            System.Threading.Thread.Sleep(hoverMs);
            INPUT[] down = new INPUT[1];
            down[0].type = INPUT_MOUSE;
            down[0].U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            SendInput(1, down, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(holdMs);

            const int steps = 12;
            for (int step = 1; step <= steps; step++)
            {
                int x = fromX + ((toX - fromX) * step / steps);
                int y = fromY + ((toY - fromY) * step / steps);
                SetCursorPos(x, y);
                System.Threading.Thread.Sleep(moveStepMs);
            }

            INPUT[] up = new INPUT[1];
            up[0].type = INPUT_MOUSE;
            up[0].U.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            SendInput(1, up, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(afterMs);
        }

        public static void BringToTop(IntPtr hwnd)
        {
            IntPtr HWND_TOPMOST = new IntPtr(-1);
            const UInt32 SWP_NOMOVE = 0x0002;
            const UInt32 SWP_NOSIZE = 0x0001;
            const UInt32 SWP_SHOWWINDOW = 0x0040;
            ShowWindow(hwnd, 9);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(hwnd);
            System.Threading.Thread.Sleep(500);
        }

        public static void MoveTopLeft(IntPtr hwnd)
        {
            IntPtr HWND_TOPMOST = new IntPtr(-1);
            const UInt32 SWP_NOSIZE = 0x0001;
            const UInt32 SWP_SHOWWINDOW = 0x0040;
            ShowWindow(hwnd, 9);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(hwnd);
            System.Threading.Thread.Sleep(500);
        }

        public static void MoveResizeTopLeft(IntPtr hwnd, int width, int height)
        {
            IntPtr HWND_TOPMOST = new IntPtr(-1);
            const UInt32 SWP_SHOWWINDOW = 0x0040;
            ShowWindow(hwnd, 9);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, width, height, SWP_SHOWWINDOW);
            SetForegroundWindow(hwnd);
            System.Threading.Thread.Sleep(500);
        }

        public static void Key(UInt16 vk)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = vk;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = vk;
            inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(40);
        }

        public static void ShiftKey(UInt16 vk)
        {
            INPUT[] inputs = new INPUT[4];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = 0x10;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = vk;
            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].U.ki.wVk = vk;
            inputs[2].U.ki.dwFlags = KEYEVENTF_KEYUP;
            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].U.ki.wVk = 0x10;
            inputs[3].U.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(50);
        }

        public static void CtrlKey(UInt16 vk)
        {
            INPUT[] inputs = new INPUT[4];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = 0x11;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = vk;
            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].U.ki.wVk = vk;
            inputs[2].U.ki.dwFlags = KEYEVENTF_KEYUP;
            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].U.ki.wVk = 0x11;
            inputs[3].U.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(50);
        }

        public static void TypeAscii(string text)
        {
            foreach (char c in text)
            {
                if (c >= 'a' && c <= 'z') Key((UInt16)(0x41 + (c - 'a')));
                else if (c >= 'A' && c <= 'Z') ShiftKey((UInt16)(0x41 + (c - 'A')));
                else if (c >= '0' && c <= '9') Key((UInt16)(0x30 + (c - '0')));
                else if (c == '_') ShiftKey(0xBD);
                else if (c == '-') Key(0xBD);
                else throw new InvalidOperationException("Unsupported automation character.");
            }
        }

        public static void TypeMessageChars(IntPtr hwnd, string text, int delayMs)
        {
            IntPtr layout = GetKeyboardLayout(0);
            bool capsWasOn = IsCapsLockOn();
            if (capsWasOn) SetCapsLock(false);
            try
            {
                foreach (char c in text)
                {
                    short keyScan = VkKeyScanEx(c, layout);
                    if (keyScan == -1)
                    {
                        throw new InvalidOperationException("Unsupported keyboard layout character.");
                    }
                    UInt32 vk = (UInt32)(keyScan & 0xFF);
                    UInt32 shiftState = (UInt32)((keyScan >> 8) & 0xFF);
                    if ((shiftState & 0x01) != 0) KeyDown(0x10);
                    if ((shiftState & 0x02) != 0) KeyDown(0x11);
                    if ((shiftState & 0x04) != 0) KeyDown(0x12);
                    KeyDown((UInt16)vk);
                    System.Threading.Thread.Sleep(80);
                    KeyUp((UInt16)vk);
                    if ((shiftState & 0x04) != 0) KeyUp(0x12);
                    if ((shiftState & 0x02) != 0) KeyUp(0x11);
                    if ((shiftState & 0x01) != 0) KeyUp(0x10);
                    System.Threading.Thread.Sleep(delayMs);
                }
            }
            finally
            {
                if (capsWasOn) SetCapsLock(true);
            }
        }

        public static void RequestEnglishKeyboard(IntPtr hwnd)
        {
            const UInt32 KLF_ACTIVATE = 0x00000001;
            const UInt32 WM_INPUTLANGCHANGEREQUEST = 0x0050;
            IntPtr layout = LoadKeyboardLayout("00000409", KLF_ACTIVATE);
            if (layout == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to load en-US keyboard layout.");
            }
            PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout);
            System.Threading.Thread.Sleep(150);
        }

        public static UInt32 GetWindowKeyboardLanguage(IntPtr hwnd)
        {
            UInt32 pid;
            UInt32 threadId = GetWindowThreadProcessId(hwnd, out pid);
            return (UInt32)(GetKeyboardLayout(threadId).ToInt64() & 0xFFFF);
        }

        public static void ForceEnglishKeyboard(IntPtr hwnd)
        {
            RequestEnglishKeyboard(hwnd);
            for (int attempt = 0; attempt < 6 && GetWindowKeyboardLanguage(hwnd) != 0x0409; attempt++)
            {
                KeyDown(0x5B);
                Key(0x20);
                KeyUp(0x5B);
                System.Threading.Thread.Sleep(180);
            }
            if (GetWindowKeyboardLanguage(hwnd) != 0x0409)
            {
                throw new InvalidOperationException("Target window keyboard layout is not en-US.");
            }
        }

        public static void SendUnicodeChars(IntPtr hwnd, string text, int delayMs)
        {
            const UInt32 WM_CHAR = 0x0102;
            foreach (char c in text)
            {
                SendMessage(hwnd, WM_CHAR, new IntPtr((int)c), IntPtr.Zero);
                System.Threading.Thread.Sleep(delayMs);
            }
        }

        public static void TypeUnicodeInput(string text, int delayMs)
        {
            const UInt32 KEYEVENTF_UNICODE = 0x0004;
            foreach (char c in text)
            {
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].U.ki.wVk = 0;
                inputs[0].U.ki.wScan = c;
                inputs[0].U.ki.dwFlags = KEYEVENTF_UNICODE;
                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].U.ki.wVk = 0;
                inputs[1].U.ki.wScan = c;
                inputs[1].U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                System.Threading.Thread.Sleep(delayMs);
            }
        }

        public static void KeyDown(UInt16 vk)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = vk;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(10);
        }

        public static void KeyUp(UInt16 vk)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = vk;
            inputs[0].U.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(10);
        }

        public static bool IsCapsLockOn()
        {
            return (GetKeyState(0x14) & 0x0001) != 0;
        }

        public static void SetCapsLock(bool on)
        {
            if (IsCapsLockOn() != on)
            {
                KeyDown(0x14);
                KeyUp(0x14);
                System.Threading.Thread.Sleep(120);
            }
        }

        public static string KeyboardStateSummary()
        {
            IntPtr layout = GetKeyboardLayout(0);
            bool caps = (GetKeyState(0x14) & 0x0001) != 0;
            bool num = (GetKeyState(0x90) & 0x0001) != 0;
            return String.Format("HKL=0x{0:X};CapsLock={1};NumLock={2}", layout.ToInt64(), caps ? 1 : 0, num ? 1 : 0);
        }

        public static void PostChars(IntPtr hwnd, string text)
        {
            const UInt32 WM_CHAR = 0x0102;
            foreach (char c in text)
            {
                PostMessage(hwnd, WM_CHAR, (IntPtr)c, IntPtr.Zero);
                System.Threading.Thread.Sleep(35);
            }
        }

        public static void PostCharsWithDelay(IntPtr hwnd, string text, int delayMs)
        {
            const UInt32 WM_CHAR = 0x0102;
            foreach (char c in text)
            {
                PostMessage(hwnd, WM_CHAR, (IntPtr)c, IntPtr.Zero);
                System.Threading.Thread.Sleep(delayMs);
            }
        }

        public static void PostClientClick(IntPtr hwnd, int x, int y)
        {
            const UInt32 WM_LBUTTONDOWN = 0x0201;
            const UInt32 WM_LBUTTONUP = 0x0202;
            IntPtr wParam = (IntPtr)1;
            IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
            PostMessage(hwnd, WM_LBUTTONDOWN, wParam, lParam);
            System.Threading.Thread.Sleep(80);
            PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
            System.Threading.Thread.Sleep(150);
        }

        public static void SendClientClick(IntPtr hwnd, int x, int y)
        {
            const UInt32 WM_LBUTTONDOWN = 0x0201;
            const UInt32 WM_LBUTTONUP = 0x0202;
            IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
            SendMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
            System.Threading.Thread.Sleep(80);
            SendMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
            System.Threading.Thread.Sleep(250);
        }

        public static void BmClick(IntPtr hwnd)
        {
            const UInt32 BM_CLICK = 0x00F5;
            SendMessage(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            System.Threading.Thread.Sleep(250);
        }
    }
}
"@
}

function ConvertTo-God2IntegrityLevel {
    param([int] $Rid)
    if ($Rid -ge 16384) { return "System" }
    if ($Rid -ge 12288) { return "High" }
    if ($Rid -ge 8192) { return "Medium" }
    if ($Rid -ge 4096) { return "Low" }
    if ($Rid -ge 0) { return "Untrusted" }
    return "Unknown"
}

function Get-God2ProcessSnapshot {
    param([int] $ProcessId)
    Initialize-God2NativeApi
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $process) {
        return $null
    }
    $startedAt = $null
    try {
        $startedAt = $process.StartTime.ToString("o")
    }
    catch {
        $startedAt = $null
    }
    $rid = [God2Automation.NativeApi]::GetIntegrityRid($ProcessId)
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
    [pscustomobject]@{
        pid = $ProcessId
        name = $process.ProcessName
        path = $cim.ExecutablePath
        commandLine = $null
        mainWindowHandle = $process.MainWindowHandle.ToInt64()
        sessionId = $process.SessionId
        integrityLevel = ConvertTo-God2IntegrityLevel $rid
        integrityRid = $rid
        startedAt = $startedAt
    }
}

function Get-God2AutomationStatusPath {
    $repoRoot = Get-God2RepoRoot
    return Join-Path $repoRoot "Automation\State\host-status.json"
}

function Get-God2AutomationStateRoot {
    return Split-Path -Parent (Get-God2AutomationStatusPath)
}

function Get-God2AutomationCommandPath {
    return Join-Path (Get-God2AutomationStateRoot) "host-command.json"
}

function Get-God2DatabaseSecretPath {
    return Join-Path (Get-God2AutomationStateRoot) "db-secret.bin"
}

function Get-God2DatabaseSecretStatusPath {
    return Join-Path (Get-God2AutomationStateRoot) "db-secret-status.json"
}

function Get-God2DatabaseAdminSecretPath {
    return Join-Path (Get-God2AutomationStateRoot) "db-admin-secret.bin"
}

function Get-God2DatabaseBuilderSecretPath {
    return Join-Path (Get-God2AutomationStateRoot) "db-builder-secret.bin"
}

function Get-God2ServerExecutablePath {
    $repoRoot = Get-God2RepoRoot
    return Join-Path $repoRoot "Release\God2ClassicServer\current\God2 Classic Server.exe"
}

function Test-God2ProcessOwnsTcpListener {
    param(
        [Parameter(Mandatory = $true)] [int] $ProcessId,
        [ValidateRange(1, 65535)] [int] $Port = 2592,
        [string] $ExpectedLocalAddress = "127.0.0.1"
    )

    $listeners = @(Get-NetTCPConnection `
        -LocalPort $Port `
        -State Listen `
        -ErrorAction SilentlyContinue | Where-Object {
            [int]$_.OwningProcess -eq $ProcessId -and
            ([string]::IsNullOrWhiteSpace($ExpectedLocalAddress) -or
                [string]$_.LocalAddress -eq $ExpectedLocalAddress)
        })
    return ($listeners.Count -eq 1)
}

function Test-God2ServerReleaseManifest {
    param([string] $ReleaseDirectory = $null)

    if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
        $ReleaseDirectory = Split-Path -Parent (Get-God2ServerExecutablePath)
    }

    $result = [ordered]@{
        succeeded = $false
        releaseDirectory = $ReleaseDirectory
        buildId = $null
        errorCode = $null
        diagnostic = $null
    }
    $manifestPath = Join-Path $ReleaseDirectory "release-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $result.errorCode = "release.manifest_missing"
        $result.diagnostic = "Release manifest is missing."
        return [pscustomobject]$result
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -ne 1 -or $manifest.files.Count -eq 0) {
            throw "Release manifest schema or file list is invalid."
        }

        $manifestPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $manifest.files) {
            $relativePath = [string]$entry.path
            if ([string]::IsNullOrWhiteSpace($relativePath) -or
                [IO.Path]::IsPathRooted($relativePath) -or
                $relativePath.Split(@('\', '/')) -contains '..') {
                throw "Release manifest contains an unsafe file path."
            }
            if (-not $manifestPaths.Add($relativePath)) {
                throw "Release manifest contains a duplicate file path: $relativePath"
            }

            $path = Join-Path $ReleaseDirectory $relativePath
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Release file is missing: $relativePath"
            }

            $actualLength = (Get-Item -LiteralPath $path).Length
            if ($actualLength -ne [long]$entry.length) {
                throw "Release length mismatch: $relativePath"
            }

            $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            if (-not [string]::Equals($actualHash, [string]$entry.sha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Release hash mismatch: $relativePath"
            }
        }

        $actualPaths = @(Get-ChildItem -LiteralPath $ReleaseDirectory -File -Recurse |
            Where-Object { -not [string]::Equals($_.FullName, $manifestPath, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { $_.FullName.Substring($ReleaseDirectory.Length + 1) })
        if ($actualPaths.Count -ne $manifestPaths.Count) {
            throw "Release directory file set does not match the manifest."
        }
        foreach ($actualPath in $actualPaths) {
            if (-not $manifestPaths.Contains($actualPath)) {
                throw "Release directory contains an unmanifested file: $actualPath"
            }
        }

        $entryPointRelative = [string]$manifest.entryPoint
        if ([string]::IsNullOrWhiteSpace($entryPointRelative) -or
            [IO.Path]::IsPathRooted($entryPointRelative) -or
            $entryPointRelative.Split(@('\', '/')) -contains '..' -or
            -not $manifestPaths.Contains($entryPointRelative)) {
            throw "Release entry point is unsafe or is not included in the manifest."
        }
        $entryPoint = Join-Path $ReleaseDirectory $entryPointRelative
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
            throw "Release entry point is missing."
        }

        if ([bool]$manifest.testsExecuted) {
            $summary = $manifest.testSummary
            if ($null -eq $summary -or
                [int]$summary.total -le 0 -or
                [int]$summary.passed -lt 0 -or
                [int]$summary.failed -ne 0 -or
                [int]$summary.skipped -lt 0 -or
                ([int]$summary.passed + [int]$summary.failed + [int]$summary.skipped) -ne [int]$summary.total) {
                throw "Release test summary is missing or inconsistent."
            }

            $testEntries = @($summary.evidence)
            if ([int]$summary.trxFileCount -le 0 -or $testEntries.Count -ne [int]$summary.trxFileCount) {
                throw "Release test evidence count does not match the summary."
            }
            $repoRoot = Get-God2RepoRoot
            $testPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            $testTotal = 0
            $testPassed = 0
            $testFailed = 0
            $testSkipped = 0
            foreach ($testEntry in $testEntries) {
                $relativeTestPath = [string]$testEntry.path
                if ([string]::IsNullOrWhiteSpace($relativeTestPath) -or
                    [IO.Path]::IsPathRooted($relativeTestPath) -or
                    $relativeTestPath.Split(@('\', '/')) -contains '..' -or
                    -not $relativeTestPath.StartsWith("Artifacts\FormalServerLogs\TestResults\", [StringComparison]::OrdinalIgnoreCase) -or
                    -not $testPaths.Add($relativeTestPath)) {
                    throw "Release manifest contains an unsafe or duplicate TRX path."
                }

                $testPath = Join-Path $repoRoot $relativeTestPath
                if (-not (Test-Path -LiteralPath $testPath -PathType Leaf)) {
                    throw "Release TRX evidence is missing: $relativeTestPath"
                }
                if ((Get-Item -LiteralPath $testPath).Length -ne [long]$testEntry.length) {
                    throw "Release TRX length mismatch: $relativeTestPath"
                }
                $testHash = (Get-FileHash -LiteralPath $testPath -Algorithm SHA256).Hash
                if (-not [string]::Equals($testHash, [string]$testEntry.sha256, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Release TRX hash mismatch: $relativeTestPath"
                }
                if ([int]$testEntry.total -le 0 -or [int]$testEntry.failed -ne 0 -or
                    ([int]$testEntry.passed + [int]$testEntry.failed + [int]$testEntry.skipped) -ne [int]$testEntry.total) {
                    throw "Release TRX counters are invalid: $relativeTestPath"
                }
                $testTotal += [int]$testEntry.total
                $testPassed += [int]$testEntry.passed
                $testFailed += [int]$testEntry.failed
                $testSkipped += [int]$testEntry.skipped
            }
            if ($testTotal -ne [int]$summary.total -or
                $testPassed -ne [int]$summary.passed -or
                $testFailed -ne [int]$summary.failed -or
                $testSkipped -ne [int]$summary.skipped) {
                throw "Release TRX aggregate counters do not match the manifest summary."
            }
        }
        elseif ($null -ne $manifest.testSummary) {
            throw "Release manifest cannot claim test evidence when testsExecuted is false."
        }

        $result.succeeded = $true
        $result.buildId = [string]$manifest.buildId
    }
    catch {
        $result.errorCode = "release.manifest_verification_failed"
        $result.diagnostic = $_.Exception.Message
    }

    return [pscustomobject]$result
}

function Get-God2LauncherProfilePath {
    return Join-Path (Get-God2RepoRoot) "Artifacts\ClientInstrumentation\LauncherAutomation\launcher-profile.json"
}

function Get-God2DatabaseConfig {
    $path = Join-Path (Get-God2RepoRoot) "config\database.json"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Database config is missing: $path"
    }

    $config = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $port = [int]$config.port
    $parsedPort = 0
    if ([int]::TryParse($env:GOD2_DATABASE_PORT, [ref]$parsedPort)) {
        $port = $parsedPort
    }

    [pscustomobject]@{
        path = $path
        host = if ([string]::IsNullOrWhiteSpace($env:GOD2_DATABASE_HOST)) { [string]$config.host } else { [string]$env:GOD2_DATABASE_HOST }
        port = $port
        databaseName = if ([string]::IsNullOrWhiteSpace($env:GOD2_DATABASE_NAME)) { [string]$config.databaseName } else { [string]$env:GOD2_DATABASE_NAME }
        username = if ([string]::IsNullOrWhiteSpace($env:GOD2_DATABASE_USERNAME)) { [string]$config.username } else { [string]$env:GOD2_DATABASE_USERNAME }
        passwordSource = if ([string]::IsNullOrWhiteSpace($env:GOD2_DATABASE_PASSWORD_SOURCE)) { [string]$config.passwordSource } else { [string]$env:GOD2_DATABASE_PASSWORD_SOURCE }
        passwordEnvironmentVariable = if ([string]::IsNullOrWhiteSpace($env:GOD2_DATABASE_PASSWORD_ENV)) { [string]$config.passwordEnvironmentVariable } else { [string]$env:GOD2_DATABASE_PASSWORD_ENV }
        connectionTimeoutSeconds = [int]$config.connectionTimeoutSeconds
        sslMode = "Preferred"
    }
}

function Get-God2EnvironmentValue {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [string] $Target = "Process"
    )

    return [Environment]::GetEnvironmentVariable($Name, $Target)
}

function New-God2DatabaseSecretStatus {
    param(
        [bool] $HasSecret,
        [string] $Source,
        [string] $FailureCode = $null,
        [string] $DiagnosticZh = $null
    )

    [pscustomobject]@{
        schemaVersion = 1
        hasSecret = $HasSecret
        source = $Source
        failureCode = $FailureCode
        diagnosticZh = $DiagnosticZh
    }
}

function Initialize-God2DatabasePasswordEnvironment {
    param([string] $EnvironmentVariableName = $null)

    Add-Type -AssemblyName System.Security
    $config = Get-God2DatabaseConfig
    $name = if ([string]::IsNullOrWhiteSpace($EnvironmentVariableName)) {
        [string]$config.passwordEnvironmentVariable
    }
    else {
        $EnvironmentVariableName
    }

    if ([string]::IsNullOrWhiteSpace($name)) {
        return New-God2DatabaseSecretStatus `
            -HasSecret $false `
            -Source "Configuration" `
            -FailureCode "automation.environment.password_variable_missing" `
            -DiagnosticZh "資料庫密碼環境變數名稱缺失；請檢查 config/database.json 的 passwordEnvironmentVariable。"
    }

    if ([string]::Equals($config.passwordSource, "ConfigValue", [StringComparison]::OrdinalIgnoreCase)) {
        $storedConfig = Get-Content -LiteralPath $config.path -Raw -Encoding UTF8 | ConvertFrom-Json
        $storedPassword = [string]$storedConfig.password
        if ([string]::IsNullOrEmpty($storedPassword)) {
            return New-God2DatabaseSecretStatus `
                -HasSecret $false `
                -Source "ConfigValue" `
                -FailureCode "mariadb.password_missing" `
                -DiagnosticZh "database.json 已選擇 ConfigValue，但 password 未設定。"
        }

        [Environment]::SetEnvironmentVariable($name, $storedPassword, "Process")
        return New-God2DatabaseSecretStatus -HasSecret $true -Source "ConfigValue"
    }

    $processValue = Get-God2EnvironmentValue -Name $name -Target "Process"
    if (-not [string]::IsNullOrEmpty($processValue)) {
        return New-God2DatabaseSecretStatus -HasSecret $true -Source "ProcessEnvironment"
    }

    $userValue = Get-God2EnvironmentValue -Name $name -Target "User"
    if (-not [string]::IsNullOrEmpty($userValue)) {
        [Environment]::SetEnvironmentVariable($name, $userValue, "Process")
        return New-God2DatabaseSecretStatus -HasSecret $true -Source "UserEnvironment"
    }

    $machineValue = Get-God2EnvironmentValue -Name $name -Target "Machine"
    if (-not [string]::IsNullOrEmpty($machineValue)) {
        [Environment]::SetEnvironmentVariable($name, $machineValue, "Process")
        return New-God2DatabaseSecretStatus -HasSecret $true -Source "MachineEnvironment"
    }

    $secretPath = Get-God2DatabaseSecretPath
    if (Test-Path -LiteralPath $secretPath) {
        try {
            $protected = [IO.File]::ReadAllBytes($secretPath)
            $plain = [Security.Cryptography.ProtectedData]::Unprotect(
                $protected,
                $null,
                [Security.Cryptography.DataProtectionScope]::CurrentUser)
            try {
                $json = [Text.Encoding]::UTF8.GetString($plain)
                $secret = $json | ConvertFrom-Json
                $secretVariable = [string]$secret.environmentVariableName
                if (-not [string]::IsNullOrWhiteSpace($secretVariable) -and
                    -not [string]::Equals($secretVariable, $name, [StringComparison]::Ordinal)) {
                    return New-God2DatabaseSecretStatus `
                        -HasSecret $false `
                        -Source "DpapiLocalSecret" `
                        -FailureCode "automation.environment.secret_variable_mismatch" `
                        -DiagnosticZh "DPAPI Secret 的環境變數名稱與目前設定不一致。"
                }

                $password = [string]$secret.password
                if ([string]::IsNullOrEmpty($password)) {
                    return New-God2DatabaseSecretStatus `
                        -HasSecret $false `
                        -Source "DpapiLocalSecret" `
                        -FailureCode "mariadb.password_missing" `
                        -DiagnosticZh "DPAPI Secret 存在，但未包含有效資料庫密碼。"
                }

                [Environment]::SetEnvironmentVariable($name, $password, "Process")
                return New-God2DatabaseSecretStatus -HasSecret $true -Source "DpapiLocalSecret"
            }
            finally {
                if ($plain) {
                    [Array]::Clear($plain, 0, $plain.Length)
                }
                $json = $null
                $secret = $null
                $password = $null
            }
        }
        catch {
            return New-God2DatabaseSecretStatus `
                -HasSecret $false `
                -Source "DpapiLocalSecret" `
                -FailureCode "automation.environment.secret_unprotect_failed" `
                -DiagnosticZh "DPAPI Secret 無法由目前 Windows 使用者解密。"
        }
    }

    return New-God2DatabaseSecretStatus `
        -HasSecret $false `
        -Source "Missing" `
        -FailureCode "mariadb.password_missing" `
        -DiagnosticZh "找不到資料庫密碼。請一次性設定使用者環境變數或執行 Automation/Prepare-God2DatabaseSecret.ps1 建立 DPAPI Secret。"
}

function Initialize-God2NamedDatabaseSecretEnvironment {
    param(
        [Parameter(Mandatory = $true)] [string] $EnvironmentVariableName,
        [Parameter(Mandatory = $true)] [string] $SecretPath,
        [Parameter(Mandatory = $true)] [string] $MissingDiagnosticZh
    )

    Add-Type -AssemblyName System.Security
    $existing = [Environment]::GetEnvironmentVariable($EnvironmentVariableName, "Process")
    if (-not [string]::IsNullOrEmpty($existing)) {
        return New-God2DatabaseSecretStatus -HasSecret $true -Source "ProcessEnvironment"
    }
    if (-not (Test-Path -LiteralPath $SecretPath)) {
        return New-God2DatabaseSecretStatus -HasSecret $false -Source "Missing" `
            -FailureCode "mariadb.password_missing" -DiagnosticZh $MissingDiagnosticZh
    }

    try {
        $protected = [IO.File]::ReadAllBytes($SecretPath)
        $plain = [Security.Cryptography.ProtectedData]::Unprotect(
            $protected, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        try {
            $payload = ([Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json)
            if (-not [string]::Equals([string]$payload.environmentVariableName, $EnvironmentVariableName, [StringComparison]::Ordinal) -or
                [string]::IsNullOrEmpty([string]$payload.password)) {
                throw "Protected database secret payload does not match the requested environment variable."
            }
            [Environment]::SetEnvironmentVariable($EnvironmentVariableName, [string]$payload.password, "Process")
            return New-God2DatabaseSecretStatus -HasSecret $true -Source "DpapiLocalSecret"
        }
        finally {
            if ($plain) { [Array]::Clear($plain, 0, $plain.Length) }
            $payload = $null
        }
    }
    catch {
        return New-God2DatabaseSecretStatus -HasSecret $false -Source "DpapiLocalSecret" `
            -FailureCode "automation.environment.secret_unprotect_failed" `
            -DiagnosticZh "資料庫 DPAPI Secret 無法由目前 Windows 使用者解密。"
    }
}

function Initialize-God2DatabaseAdminPasswordEnvironment {
    return Initialize-God2NamedDatabaseSecretEnvironment `
        -EnvironmentVariableName "GOD2_DB_ADMIN_PASSWORD" `
        -SecretPath (Get-God2DatabaseAdminSecretPath) `
        -MissingDiagnosticZh "找不到 migration 管理憑證；請依資料庫管理文件重新建立 db-admin-secret.bin。"
}

function Initialize-God2DatabaseBuilderPasswordEnvironment {
    return Initialize-God2NamedDatabaseSecretEnvironment `
        -EnvironmentVariableName "GOD2_DB_BUILDER_PASSWORD" `
        -SecretPath (Get-God2DatabaseBuilderSecretPath) `
        -MissingDiagnosticZh "找不到 Catalog Builder 憑證；請依資料庫管理文件重新建立 db-builder-secret.bin。"
}

function Get-God2AutomationTaskSpec {
    param([string] $TaskName = "God2Classic.AutomationHost")

    [pscustomobject]@{
        taskName = $TaskName
        command = "powershell.exe"
        arguments = "-NoProfile -ExecutionPolicy Bypass -File .\Automation\God2AutomationHost.ps1"
        workingDirectory = Get-God2RepoRoot
        runLevel = "HighestAvailable"
        logonType = "InteractiveToken"
    }
}

function Get-God2AutomationTaskValidation {
    param([string] $TaskName = "God2Classic.AutomationHost")

    $spec = Get-God2AutomationTaskSpec -TaskName $TaskName
    $result = [ordered]@{
        taskName = $TaskName
        exists = $false
        isCorrect = $false
        command = $null
        arguments = $null
        workingDirectory = $null
        runLevel = $null
        logonType = $null
        commandCorrect = $false
        argumentsCorrect = $false
        actionRelative = $false
        workingDirectoryCorrect = $false
        runLevelHighest = $false
        logonTypeInteractive = $false
        diagnosticZh = $null
    }

    try {
        $xmlText = Export-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    }
    catch {
        $result.diagnosticZh = "Scheduled task does not exist or cannot be read: $($_.Exception.Message)"
        return [pscustomobject]$result
    }

    [xml]$xml = $xmlText
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace("t", $xml.DocumentElement.NamespaceURI)

    $commandNode = $xml.SelectSingleNode("//t:Actions/t:Exec/t:Command", $ns)
    $argumentNode = $xml.SelectSingleNode("//t:Actions/t:Exec/t:Arguments", $ns)
    $workingDirectoryNode = $xml.SelectSingleNode("//t:Actions/t:Exec/t:WorkingDirectory", $ns)
    $runLevelNode = $xml.SelectSingleNode("//t:Principals/t:Principal/t:RunLevel", $ns)
    $logonTypeNode = $xml.SelectSingleNode("//t:Principals/t:Principal/t:LogonType", $ns)

    $result.exists = $true
    $result.command = if ($commandNode) { [string]$commandNode.InnerText } else { "" }
    $result.arguments = if ($argumentNode) { [string]$argumentNode.InnerText } else { "" }
    $result.workingDirectory = if ($workingDirectoryNode) { [string]$workingDirectoryNode.InnerText } else { "" }
    $result.runLevel = if ($runLevelNode) { [string]$runLevelNode.InnerText } else { "" }
    $result.logonType = if ($logonTypeNode) { [string]$logonTypeNode.InnerText } else { "" }

    $commandFileName = [System.IO.Path]::GetFileName($result.command)
    $result.commandCorrect = [string]::Equals($commandFileName, $spec.command, [StringComparison]::OrdinalIgnoreCase)
    $result.argumentsCorrect = [string]::Equals($result.arguments, $spec.arguments, [StringComparison]::Ordinal)
    $result.actionRelative = $result.argumentsCorrect -and $result.arguments -notmatch "[A-Za-z]:\\"

    try {
        $actualWorkingDirectory = [System.IO.Path]::GetFullPath($result.workingDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
        $expectedWorkingDirectory = [System.IO.Path]::GetFullPath($spec.workingDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
        $result.workingDirectoryCorrect = [string]::Equals($actualWorkingDirectory, $expectedWorkingDirectory, [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $result.workingDirectoryCorrect = $false
    }

    $result.runLevelHighest = [string]::Equals($result.runLevel, $spec.runLevel, [StringComparison]::OrdinalIgnoreCase)
    $result.logonTypeInteractive = [string]::Equals($result.logonType, $spec.logonType, [StringComparison]::OrdinalIgnoreCase)
    $result.isCorrect = $result.commandCorrect -and
        $result.argumentsCorrect -and
        $result.actionRelative -and
        $result.workingDirectoryCorrect -and
        $result.runLevelHighest -and
        $result.logonTypeInteractive

    if (-not $result.isCorrect) {
        $result.diagnosticZh = "Scheduled task configuration is incorrect and must be repaired or re-registered."
    }

    [pscustomobject]$result
}



using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace God2.LauncherAutomationRecorder;

internal static class Program
{
    private static readonly string DefaultLauncher =
        Environment.GetEnvironmentVariable("GOD2_LAUNCHER_PATH") ??
        Path.Combine(AppContext.BaseDirectory, "Launcher.exe");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.Mode is null)
            {
                options = options.WithMode("--record");
            }

            if (options.Mode == "--record")
            {
                return Recorder.Run(options);
            }

            if (options.Mode == "--replay")
            {
                return Replayer.Run(options);
            }

            Usage();
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void Usage()
    {
        Console.WriteLine("God2 Launcher Automation Recorder");
        Console.WriteLine("  --record [--launcher <path>] [--out <dir>] [--timeout-ms <n>]");
        Console.WriteLine("  --replay --flow <launcher-flow.json> [--launcher <path>] [--timeout-ms <n>]");
        Console.WriteLine("Record hotkey: Ctrl+Shift+F12 after the manual demonstration is complete.");
    }

    internal static string DefaultLauncherPath => DefaultLauncher;
    internal static string DefaultArtifactRoot => Path.GetFullPath(AppContext.BaseDirectory);
    internal static JsonSerializerOptions SerializerOptions => JsonOptions;
}

internal sealed class CliOptions
{
    public string? Mode { get; private init; }
    public string LauncherPath { get; private init; } = Program.DefaultLauncherPath;
    public string OutputRoot { get; private init; } = Program.DefaultArtifactRoot;
    public string? FlowPath { get; private init; }
    public int TimeoutMs { get; private init; } = 600000;

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--record" or "--replay")
            {
                result = result.WithMode(arg);
                continue;
            }

            if (arg == "--launcher" && i + 1 < args.Length)
            {
                result = result.WithLauncher(args[++i]);
                continue;
            }

            if (arg == "--out" && i + 1 < args.Length)
            {
                result = result.WithOut(args[++i]);
                continue;
            }

            if (arg == "--flow" && i + 1 < args.Length)
            {
                result = result.WithFlow(args[++i]);
                continue;
            }

            if (arg == "--timeout-ms" && i + 1 < args.Length && int.TryParse(args[++i], out var timeout))
            {
                result = result.WithTimeout(timeout);
                continue;
            }

            throw new InvalidOperationException($"Unknown or incomplete argument: {arg}");
        }

        return result;
    }

    public CliOptions WithMode(string value) => new() { Mode = value, LauncherPath = LauncherPath, OutputRoot = OutputRoot, FlowPath = FlowPath, TimeoutMs = TimeoutMs };
    private CliOptions WithLauncher(string value) => new() { Mode = Mode, LauncherPath = value, OutputRoot = OutputRoot, FlowPath = FlowPath, TimeoutMs = TimeoutMs };
    private CliOptions WithOut(string value) => new() { Mode = Mode, LauncherPath = LauncherPath, OutputRoot = value, FlowPath = FlowPath, TimeoutMs = TimeoutMs };
    private CliOptions WithFlow(string value) => new() { Mode = Mode, LauncherPath = LauncherPath, OutputRoot = OutputRoot, FlowPath = value, TimeoutMs = TimeoutMs };
    private CliOptions WithTimeout(int value) => new() { Mode = Mode, LauncherPath = LauncherPath, OutputRoot = OutputRoot, FlowPath = FlowPath, TimeoutMs = value };
}

internal sealed record LauncherFlow
{
    public int Version { get; init; } = 1;
    public string CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow.ToString("o");
    public string TargetExecutable { get; init; } = "";
    public CredentialPolicy Credentials { get; init; } = new();
    public List<LauncherEvent> Events { get; init; } = [];
}

internal sealed record CredentialPolicy
{
    public bool SavesCredentials { get; init; }
    public string Policy { get; init; } = "No plaintext account or password data is recorded. Text keys are redacted and not replayable.";
}

internal sealed record LauncherEvent
{
    public int Sequence { get; init; }
    public long TimestampOffsetMs { get; init; }
    public string Name { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public int ProcessPid { get; init; }
    public string? ExecutablePath { get; init; }
    public long WindowHwnd { get; init; }
    public string WindowClassName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public RectRecord WindowClientRect { get; init; } = new();
    public int Dpi { get; init; }
    public string EventType { get; init; } = "";
    public string? MouseButton { get; init; }
    public string? Key { get; init; }
    public int ClientX { get; init; }
    public int ClientY { get; init; }
    public double RelativeX { get; init; }
    public double RelativeY { get; init; }
    public long? TargetChildHwnd { get; init; }
    public string? TargetChildClassName { get; init; }
    public int? TargetControlId { get; init; }
    public string Before { get; init; } = "LauncherMainVisible";
    public string After { get; init; } = "LauncherMainVisible";
    public int TimeoutMs { get; init; } = 5000;
    public string? ScreenshotHash { get; init; }
    public TemplateRecord? Template { get; init; }
}

internal sealed record RectRecord(int Left = 0, int Top = 0, int Right = 0, int Bottom = 0)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

internal sealed record TemplateRecord(int X, int Y, int Width, int Height, string Sha256);

internal sealed class Recorder : IDisposable
{
    private readonly CliOptions _options;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<LauncherEvent> _events = [];
    private readonly string _launcherFullPath;
    private readonly string _runDir;
    private readonly string _templateDir;
    private readonly Native.LowLevelMouseProc _mouseProc;
    private readonly Native.LowLevelKeyboardProc _keyboardProc;
    private int _sequence;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _stopRequested;

    private Recorder(CliOptions options)
    {
        _options = options;
        _launcherFullPath = Path.GetFullPath(options.LauncherPath);
        var runId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _runDir = Path.Combine(Path.GetFullPath(options.OutputRoot), runId);
        _templateDir = Path.Combine(_runDir, "templates");
        _mouseProc = MouseHook;
        _keyboardProc = KeyboardHook;
        Directory.CreateDirectory(_templateDir);
    }

    public static int Run(CliOptions options)
    {
        if (!File.Exists(options.LauncherPath))
        {
            throw new FileNotFoundException("Launcher.exe not found.", options.LauncherPath);
        }

        using var recorder = new Recorder(options);
        return recorder.Record();
    }

    private int Record()
    {
        Console.WriteLine("Record mode is armed.");
        Console.WriteLine("Waiting to record. Manually demonstrate only the Launcher flow, then press F12 or Ctrl+Shift+F12.");
        Console.WriteLine("Text keys are redacted and are not saved as credentials.");

        _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, Native.GetModuleHandle(null), 0);
        _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyboardProc, Native.GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }

        var deadline = Environment.TickCount64 + Math.Max(_options.TimeoutMs, 10000);
        while (!_stopRequested && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }

        SaveFlow();
        return _stopRequested ? 0 : 3;
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == Native.WM_LBUTTONDOWN || wParam == Native.WM_LBUTTONUP))
        {
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            TryAddMouseEvent(wParam == Native.WM_LBUTTONDOWN ? "MouseLeftDown" : "MouseLeftUp", info.pt.X, info.pt.Y);
        }

        return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var isDown = wParam == Native.WM_KEYDOWN || wParam == Native.WM_SYSKEYDOWN;
            var isUp = wParam == Native.WM_KEYUP || wParam == Native.WM_SYSKEYUP;
            if (isDown && Native.IsStopRecordingKey(info.vkCode))
            {
                _stopRequested = true;
                return Native.CallNextHookEx(_keyboardHook, code, wParam, lParam);
            }

            if (isDown || isUp)
            {
                TryAddKeyEvent(isDown ? "KeyDown" : "KeyUp", info.vkCode);
            }
        }

        return Native.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void TryAddMouseEvent(string eventType, int screenX, int screenY)
    {
        var hwnd = Native.WindowFromPoint(new Native.POINT { X = screenX, Y = screenY });
        if (!TryGetTarget(hwnd, out var target))
        {
            return;
        }

        var point = UnsafePoint(screenX, screenY);
        if (!Native.GetClientRect(target.RootHwnd, out var clientRect) ||
            !Native.ScreenToClient(target.RootHwnd, ref point))
        {
            return;
        }

        var width = Math.Max(1, clientRect.Right - clientRect.Left);
        var height = Math.Max(1, clientRect.Bottom - clientRect.Top);
        var template = CaptureTemplate(target.RootHwnd, point.X, point.Y);

        _events.Add(new LauncherEvent
        {
            Sequence = ++_sequence,
            TimestampOffsetMs = _clock.ElapsedMilliseconds,
            Name = GuessName(eventType),
            ProcessName = target.ProcessName,
            ProcessPid = target.Pid,
            ExecutablePath = target.ExecutablePath,
            WindowHwnd = target.RootHwnd.ToInt64(),
            WindowClassName = target.RootClass,
            WindowTitle = target.Title,
            WindowClientRect = new RectRecord(0, 0, width, height),
            Dpi = Native.GetDpiForWindow(target.RootHwnd),
            EventType = eventType,
            MouseButton = "Left",
            ClientX = point.X,
            ClientY = point.Y,
            RelativeX = Math.Clamp(point.X / (double)width, 0, 1),
            RelativeY = Math.Clamp(point.Y / (double)height, 0, 1),
            TargetChildHwnd = target.ChildHwnd == target.RootHwnd ? null : target.ChildHwnd.ToInt64(),
            TargetChildClassName = target.ChildClass,
            TargetControlId = target.ControlId,
            Before = GuessBefore(eventType),
            After = GuessAfter(eventType),
            TimeoutMs = eventType == "MouseLeftUp" ? 10000 : 5000,
            ScreenshotHash = template?.Sha256,
            Template = template
        });
    }

    private void TryAddKeyEvent(string eventType, uint virtualKey)
    {
        var foreground = Native.GetForegroundWindow();
        if (!TryGetTarget(foreground, out var target))
        {
            return;
        }

        var keyName = Native.IsSensitiveTextKey(virtualKey) ? "RedactedTextKey" : ((Keys)virtualKey).ToString();
        _events.Add(new LauncherEvent
        {
            Sequence = ++_sequence,
            TimestampOffsetMs = _clock.ElapsedMilliseconds,
            Name = keyName == "RedactedTextKey" ? "RedactedKey" : keyName,
            ProcessName = target.ProcessName,
            ProcessPid = target.Pid,
            ExecutablePath = target.ExecutablePath,
            WindowHwnd = target.RootHwnd.ToInt64(),
            WindowClassName = target.RootClass,
            WindowTitle = target.Title,
            WindowClientRect = target.ClientRect,
            Dpi = Native.GetDpiForWindow(target.RootHwnd),
            EventType = eventType,
            Key = keyName,
            Before = "LauncherMainVisible",
            After = "LauncherMainVisible",
            TimeoutMs = 5000
        });
    }

    private TemplateRecord? CaptureTemplate(IntPtr hwnd, int clientX, int clientY)
    {
        try
        {
            var screen = UnsafePoint(clientX, clientY);
            if (!Native.ClientToScreen(hwnd, ref screen))
            {
                return null;
            }

            const int size = 32;
            var x = Math.Max(0, screen.X - size / 2);
            var y = Math.Max(0, screen.Y - size / 2);
            using var bitmap = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(x, y, 0, 0, new Size(size, size));
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            var bytes = stream.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(_templateDir, $"{_sequence + 1:D3}-{hash[..12]}.png"), bytes);
            return new TemplateRecord(clientX - size / 2, clientY - size / 2, size, size, hash);
        }
        catch
        {
            return null;
        }
    }

    private void SaveFlow()
    {
        var flow = new LauncherFlow
        {
            TargetExecutable = _launcherFullPath,
            Events = NormalizeEvents(_events)
        };
        var flowPath = Path.Combine(_runDir, "launcher-flow.json");
        File.WriteAllText(flowPath, JsonSerializer.Serialize(flow, Program.SerializerOptions), Encoding.UTF8);

        var summaryPath = Path.Combine(_runDir, "launcher-flow-summary.md");
        File.WriteAllText(summaryPath, BuildSummary(flow, flowPath), Encoding.UTF8);
        var latestFlowPath = Path.Combine(Path.GetFullPath(_options.OutputRoot), "launcher-flow.json");
        var latestSummaryPath = Path.Combine(Path.GetFullPath(_options.OutputRoot), "launcher-flow-summary.md");
        File.Copy(flowPath, latestFlowPath, overwrite: true);
        File.Copy(summaryPath, latestSummaryPath, overwrite: true);
        Console.WriteLine(flowPath);
        Console.WriteLine(summaryPath);
        Console.WriteLine(latestFlowPath);
        Console.WriteLine(latestSummaryPath);
    }

    private static List<LauncherEvent> NormalizeEvents(List<LauncherEvent> events)
    {
        var result = new List<LauncherEvent>();
        var clickUps = events.Where(e => e.EventType == "MouseLeftUp").ToList();
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.EventType == "MouseLeftUp")
            {
                var clickOrdinal = clickUps.TakeWhile(x => !ReferenceEquals(x, e)).Count();
                e = e with
                {
                    Name = clickOrdinal switch
                    {
                        0 => "AcceptAgreement",
                        1 => "StartGame",
                        _ => e.ProcessName.Equals("God2_opt", StringComparison.OrdinalIgnoreCase) ? "DirectSoundOk" : $"Click{clickOrdinal + 1}"
                    },
                    Before = clickOrdinal switch
                    {
                        0 => "LauncherMainVisible",
                        1 => "EndpointIsLocalAndAgreementAccepted",
                        _ => e.ProcessName.Equals("God2_opt", StringComparison.OrdinalIgnoreCase) ? "DirectSoundDialogVisible" : "LauncherMainVisible"
                    },
                    After = clickOrdinal switch
                    {
                        0 => "AgreementAccepted",
                        1 => "NewGod2OptCreated",
                        _ => e.ProcessName.Equals("God2_opt", StringComparison.OrdinalIgnoreCase) ? "DirectSoundDialogGoneOrGod2Alive" : "LauncherMainVisible"
                    }
                };
            }

            result.Add(e with { Sequence = i + 1 });
        }

        return result;
    }

    private static string BuildSummary(LauncherFlow flow, string flowPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Launcher Automation Flow");
        builder.AppendLine();
        builder.AppendLine($"Flow: `{flowPath}`");
        builder.AppendLine($"Target: `{flow.TargetExecutable}`");
        builder.AppendLine("Credentials saved: NO");
        builder.AppendLine($"Events: {flow.Events.Count}");
        builder.AppendLine();
        builder.AppendLine("| Seq | Name | Type | Process | Relative X | Relative Y | Before | After |");
        builder.AppendLine("| --- | --- | --- | --- | ---: | ---: | --- | --- |");
        foreach (var e in flow.Events)
        {
            builder.AppendLine($"| {e.Sequence} | {e.Name} | {e.EventType} | {e.ProcessName} | {e.RelativeX:F4} | {e.RelativeY:F4} | {e.Before} | {e.After} |");
        }

        return builder.ToString();
    }

    private static string GuessName(string eventType) => eventType;
    private static string GuessBefore(string eventType) => eventType == "MouseLeftUp" ? "LauncherMainVisible" : "LauncherMainVisible";
    private static string GuessAfter(string eventType) => eventType == "MouseLeftUp" ? "LauncherMainVisible" : "LauncherMainVisible";
    private static Native.POINT UnsafePoint(int x, int y) => new() { X = x, Y = y };

    private bool TryGetTarget(IntPtr hwnd, out TargetWindow target)
    {
        target = default!;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        Native.GetWindowThreadProcessId(root, out var pid);
        if (pid == 0)
        {
            return false;
        }

        Process process;
        try
        {
            process = Process.GetProcessById((int)pid);
        }
        catch
        {
            return false;
        }

        var processName = process.ProcessName;
        if (!processName.Equals("Launcher", StringComparison.OrdinalIgnoreCase) &&
            !processName.Equals("God2_opt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Native.GetClientRect(root, out var rect))
        {
            return false;
        }

        target = new TargetWindow(
            root,
            hwnd,
            (int)pid,
            processName,
            SafePath(process),
            Native.GetClassName(root),
            Native.GetClassName(hwnd),
            Native.GetWindowText(root),
            new RectRecord(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top),
            Native.GetDlgCtrlID(hwnd));
        return true;
    }

    private static string? SafePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_mouseHook);
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_keyboardHook);
        }
    }
}

internal sealed class Replayer
{
    private static HashSet<int> _baselineGod2 = [];
    private static int? _newGod2Pid;

    public static int Run(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FlowPath))
        {
            throw new InvalidOperationException("--replay requires --flow <launcher-flow.json>.");
        }

        var flow = JsonSerializer.Deserialize<LauncherFlow>(File.ReadAllText(options.FlowPath), Program.SerializerOptions)
            ?? throw new InvalidOperationException("Flow JSON is empty or invalid.");
        var launcherPath = Path.GetFullPath(options.LauncherPath);
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException("Launcher.exe not found.", launcherPath);
        }

        EnsureEndpointIsLocal(Path.Combine(Path.GetDirectoryName(launcherPath)!, "ctserver.ini"));
        EnsureServerListening();
        _baselineGod2 = Process.GetProcessesByName("God2_opt").Select(p => p.Id).ToHashSet();

        foreach (var e in flow.Events.OrderBy(e => e.Sequence))
        {
            if (!WaitCondition(e.Before, e, flow, e.TimeoutMs))
            {
                throw new TimeoutException($"Replay timeout before {e.Sequence}:{e.Name}; condition={e.Before}");
            }

            ReplayEvent(e);

            if (!WaitCondition(e.After, e, flow, e.TimeoutMs))
            {
                throw new TimeoutException($"Replay timeout after {e.Sequence}:{e.Name}; condition={e.After}");
            }
        }

        if (_newGod2Pid is not null)
        {
            Console.WriteLine($"NEW_GOD2_PID={_newGod2Pid}");
        }

        Console.WriteLine("REPLAY=PASS");
        return 0;
    }

    private static void ReplayEvent(LauncherEvent e)
    {
        if (e.Name.Equals("DirectSoundOk", StringComparison.OrdinalIgnoreCase))
        {
            var dialog = FindDirectSoundDialog();
            if (dialog != IntPtr.Zero)
            {
                if (!TryClickDialogOk(dialog))
                {
                    ReplayMouseAtRecordedPoint(e, dialog);
                }
            }

            return;
        }

        if (e.EventType is "MouseLeftDown" or "MouseLeftUp" or "MouseClick")
        {
            ReplayMouseAtRecordedPoint(e, FindWindowForEvent(e));
        }
        else if (e.EventType is "KeyDown" or "KeyUp")
        {
            if (e.Key is null or "RedactedTextKey")
            {
                return;
            }

            if (Enum.TryParse<Keys>(e.Key, out var key))
            {
                Native.SendKey((ushort)key, e.EventType == "KeyDown");
            }
        }
    }

    private static void ReplayMouseAtRecordedPoint(LauncherEvent e, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Target window not found for {e.Name}.");
        }

        if (Native.IsIconic(hwnd))
        {
            Native.ShowWindow(hwnd, Native.SW_RESTORE);
        }

        Native.SetForegroundWindow(hwnd);
        Thread.Sleep(120);
        if (!Native.GetClientRect(hwnd, out var rect))
        {
            throw new InvalidOperationException($"GetClientRect failed for {e.Name}: {Marshal.GetLastWin32Error()}");
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        if (e.WindowClientRect.Width > 0 && e.WindowClientRect.Height > 0)
        {
            var widthRatio = width / (double)e.WindowClientRect.Width;
            var heightRatio = height / (double)e.WindowClientRect.Height;
            if (widthRatio < 0.5 || widthRatio > 2.0 || heightRatio < 0.5 || heightRatio > 2.0)
            {
                throw new InvalidOperationException($"Launcher layout mismatch for {e.Name}: recorded={e.WindowClientRect.Width}x{e.WindowClientRect.Height}, current={width}x{height}");
            }
        }

        var x = (int)Math.Round(Math.Clamp(e.RelativeX, 0, 1) * width);
        var y = (int)Math.Round(Math.Clamp(e.RelativeY, 0, 1) * height);
        var point = new Native.POINT { X = x, Y = y };
        if (!Native.ClientToScreen(hwnd, ref point))
        {
            throw new InvalidOperationException($"ClientToScreen failed for {e.Name}: {Marshal.GetLastWin32Error()}");
        }

        var hit = Native.WindowFromPoint(point);
        var hitRoot = hit == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(hit, Native.GA_ROOT);
        if (hitRoot != hwnd)
        {
            throw new InvalidOperationException($"Replay point is not over target window for {e.Name}; hit=0x{hit.ToInt64():X}, expected=0x{hwnd.ToInt64():X}");
        }

        Native.SendMouse(point.X, point.Y, e.EventType);
    }

    private static bool WaitCondition(string condition, LauncherEvent e, LauncherFlow flow, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 <= deadline)
        {
            if (CheckCondition(condition, e, flow))
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static bool CheckCondition(string condition, LauncherEvent e, LauncherFlow flow) => condition switch
    {
        "LauncherMainVisible" => FindLauncherWindow(flow.TargetExecutable) != IntPtr.Zero,
        "EndpointIsLocalAndAgreementAccepted" => FindLauncherWindow(flow.TargetExecutable) != IntPtr.Zero && EndpointIsLocal(flow.TargetExecutable),
        "AgreementAccepted" => FindLauncherWindow(flow.TargetExecutable) != IntPtr.Zero,
        "NewGod2OptCreated" => TryFindNewGod2(),
        "DirectSoundDialogVisible" => FindDirectSoundDialog() != IntPtr.Zero || Process.GetProcessesByName("God2_opt").Length > 0,
        "DirectSoundDialogGoneOrGod2Alive" => FindDirectSoundDialog() == IntPtr.Zero && Process.GetProcessesByName("God2_opt").Length > 0,
        _ => FindWindowForEvent(e) != IntPtr.Zero
    };

    private static bool TryFindNewGod2()
    {
        foreach (var process in Process.GetProcessesByName("God2_opt"))
        {
            if (!_baselineGod2.Contains(process.Id))
            {
                _newGod2Pid = process.Id;
                return true;
            }
        }

        return false;
    }

    private static IntPtr FindWindowForEvent(LauncherEvent e)
    {
        var candidates = Process.GetProcessesByName(e.ProcessName);
        foreach (var process in candidates)
        {
            var hwnd = process.MainWindowHandle;
            if (hwnd == IntPtr.Zero)
            {
                continue;
            }

            var cls = Native.GetClassName(hwnd);
            if (string.IsNullOrEmpty(e.WindowClassName) || cls.Equals(e.WindowClassName, StringComparison.OrdinalIgnoreCase))
            {
                return hwnd;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindLauncherWindow(string launcherPath)
    {
        var expected = Path.GetFullPath(launcherPath);
        foreach (var process in Process.GetProcessesByName("Launcher"))
        {
            var path = SafePath(process);
            if (path is not null && !Path.GetFullPath(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (process.MainWindowHandle != IntPtr.Zero && !Native.IsIconic(process.MainWindowHandle))
            {
                return process.MainWindowHandle;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindDirectSoundDialog()
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                return true;
            }

            try
            {
                if (!Process.GetProcessById((int)pid).ProcessName.Equals("God2_opt", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            if (Native.GetClassName(hwnd) == "#32770" &&
                (Native.GetWindowText(hwnd).Contains("Info", StringComparison.OrdinalIgnoreCase) || WindowHasText(hwnd, "Direct Sound Create failed!")))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool WindowHasText(IntPtr hwnd, string needle)
    {
        var found = false;
        Native.EnumChildWindows(hwnd, (child, _) =>
        {
            if (Native.GetWindowText(child).Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool TryClickDialogOk(IntPtr dialog)
    {
        var idOk = Native.GetDlgItem(dialog, 1);
        if (idOk != IntPtr.Zero)
        {
            Native.SendMessage(dialog, Native.WM_COMMAND, new IntPtr(1), idOk);
            return true;
        }

        var clicked = false;
        Native.EnumChildWindows(dialog, (child, _) =>
        {
            if (Native.GetClassName(child).Equals("Button", StringComparison.OrdinalIgnoreCase))
            {
                Native.SendMessage(child, Native.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                clicked = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return clicked;
    }

    private static void EnsureEndpointIsLocal(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("ctserver.ini not found.", path);
        }

        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        if (!text.Contains("server1_ip=127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ctserver.ini endpoint is not 127.0.0.1.");
        }
    }

    private static bool EndpointIsLocal(string launcherPath)
    {
        var ini = Path.Combine(Path.GetDirectoryName(launcherPath)!, "ctserver.ini");
        return File.Exists(ini) && Encoding.UTF8.GetString(File.ReadAllBytes(ini)).Contains("server1_ip=127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureServerListening()
    {
        var listener = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"if(Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 2592 -State Listen -ErrorAction SilentlyContinue){exit 0}else{exit 7}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        listener!.WaitForExit(5000);
        if (listener.ExitCode != 0)
        {
            throw new InvalidOperationException("Server 127.0.0.1:2592 Listen check failed.");
        }
    }

    private static string? SafePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }
}

internal sealed record TargetWindow(
    IntPtr RootHwnd,
    IntPtr ChildHwnd,
    int Pid,
    string ProcessName,
    string? ExecutablePath,
    string RootClass,
    string ChildClass,
    string Title,
    RectRecord ClientRect,
    int ControlId);

internal static class Native
{
    public const int WH_MOUSE_LL = 14;
    public const int WH_KEYBOARD_LL = 13;
    public const int GA_ROOT = 2;
    public const int SW_RESTORE = 9;
    public static readonly IntPtr WM_LBUTTONDOWN = new(0x0201);
    public static readonly IntPtr WM_LBUTTONUP = new(0x0202);
    public static readonly IntPtr WM_KEYDOWN = new(0x0100);
    public static readonly IntPtr WM_KEYUP = new(0x0101);
    public static readonly IntPtr WM_SYSKEYDOWN = new(0x0104);
    public static readonly IntPtr WM_SYSKEYUP = new(0x0105);
    public const uint WM_COMMAND = 0x0111;
    public const uint BM_CLICK = 0x00F5;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hmod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hmod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern int GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static string GetWindowText(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        _ = GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static bool IsStopRecordingKey(uint vkCode) =>
        vkCode == (uint)Keys.F12;

    public static bool IsSensitiveTextKey(uint vkCode)
    {
        var key = (Keys)vkCode;
        return key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or >= Keys.NumPad0 and <= Keys.NumPad9
            or Keys.Space or Keys.OemMinus or Keys.Oemplus or Keys.Oemcomma or Keys.OemPeriod or Keys.OemQuestion
            or Keys.OemSemicolon or Keys.OemQuotes or Keys.OemOpenBrackets or Keys.OemCloseBrackets or Keys.OemPipe;
    }

    public static void SendMouse(int screenX, int screenY, string eventType)
    {
        SetCursorPos(screenX, screenY);
        Thread.Sleep(50);
        if (eventType == "MouseLeftDown")
        {
            SendInputChecked(MOUSEEVENTF_LEFTDOWN);
        }
        else if (eventType == "MouseLeftUp")
        {
            SendInputChecked(MOUSEEVENTF_LEFTUP);
        }
        else
        {
            SendInputChecked(MOUSEEVENTF_LEFTDOWN);
            Thread.Sleep(80);
            SendInputChecked(MOUSEEVENTF_LEFTUP);
        }
    }

    public static void SendKey(ushort virtualKey, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = down ? 0u : KEYEVENTF_KEYUP } }
        };
        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1)
        {
            throw new InvalidOperationException($"SendInput key failed: {Marshal.GetLastWin32Error()}");
        }
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static void SendInputChecked(uint mouseFlag)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = mouseFlag } }
        };
        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1)
        {
            throw new InvalidOperationException($"SendInput mouse failed: {Marshal.GetLastWin32Error()}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
}

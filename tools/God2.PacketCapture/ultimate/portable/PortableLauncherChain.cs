using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class God2PortableLauncherChain
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private const uint BM_CLICK = 0x00F5;

    private static string Text(IntPtr window)
    {
        var value = new StringBuilder(512);
        GetWindowText(window, value, value.Capacity);
        return value.ToString();
    }

    private static string ClassName(IntPtr window)
    {
        var value = new StringBuilder(128);
        GetClassName(window, value, value.Capacity);
        return value.ToString();
    }

    private static bool FindDirectSoundDialog(uint processId, bool clickButton)
    {
        bool matched = false;
        EnumWindows((top, unused) =>
        {
            uint owner;
            GetWindowThreadProcessId(top, out owner);
            if (owner != processId) return true;

            bool directSound = Text(top).IndexOf("Direct Sound", StringComparison.OrdinalIgnoreCase) >= 0;
            var buttons = new List<IntPtr>();
            EnumChildWindows(top, (child, ignored) =>
            {
                string text = Text(child);
                if (text.IndexOf("Direct Sound Create failed!", StringComparison.OrdinalIgnoreCase) >= 0)
                    directSound = true;
                if (ClassName(child).Equals("Button", StringComparison.OrdinalIgnoreCase))
                    buttons.Add(child);
                return true;
            }, IntPtr.Zero);

            if (!directSound) return true;
            if (!clickButton)
            {
                matched = true;
                return false;
            }
            if (buttons.Count == 0) return true;
            SendMessage(buttons[0], BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            matched = true;
            return false;
        }, IntPtr.Zero);
        return matched;
    }

    public static bool IsDirectSoundDialogPresent(uint processId)
    {
        return FindDirectSoundDialog(processId, false);
    }

    public static bool ClickDirectSoundDialog(uint processId)
    {
        return FindDirectSoundDialog(processId, true);
    }
}

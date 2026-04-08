using System.Runtime.InteropServices;

namespace ClickyWindows.Helpers;

internal static class NativeMethods
{
    // ── Window Style Constants ──────────────────────────────────────
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;

    // ── Keyboard Hook Constants ─────────────────────────────────────
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;    // Left Alt
    public const int VK_RMENU = 0xA5;    // Right Alt

    // ── Delegates ───────────────────────────────────────────────────
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ── Window Style ────────────────────────────────────────────────
    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    // ── Keyboard Hook ───────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ── Keyboard State ──────────────────────────────────────────────
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using ClickyWindows.Helpers;

namespace ClickyWindows.Services;

public enum ShortcutTransition
{
    Pressed,
    Released
}

/// <summary>
/// Monitors for the global Ctrl+Alt push-to-talk shortcut using a low-level keyboard hook.
/// Port of GlobalPushToTalkShortcutMonitor.swift — detects modifier-only press/release
/// transitions system-wide, even when the app is in the background.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    public event EventHandler<ShortcutTransition>? ShortcutTransitionChanged;

    private IntPtr hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? hookProc;
    private bool isShortcutActive;

    public void Start()
    {
        // Keep a reference to prevent garbage collection of the delegate
        hookProc = HookCallback;
        hookId = SetHook(hookProc);
    }

    public void Stop()
    {
        if (hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }
    }

    private static IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule!;
        return NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            proc,
            NativeMethods.GetModuleHandle(currentModule.ModuleName),
            0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var vkCode = hookStruct.vkCode;
            var messageType = wParam.ToInt32();

            // We only care about Ctrl and Alt key events
            bool isCtrlOrAlt = vkCode is NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL
                                     or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU;

            if (isCtrlOrAlt)
            {
                bool isKeyDown = messageType is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                bool isKeyUp = messageType is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

                if (isKeyDown || isKeyUp)
                {
                    // Check if both Ctrl and Alt are currently held
                    bool ctrlHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LCONTROL) & 0x8000) != 0
                                 || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RCONTROL) & 0x8000) != 0;
                    bool altHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LMENU) & 0x8000) != 0
                                || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RMENU) & 0x8000) != 0;

                    bool bothHeld = ctrlHeld && altHeld;

                    if (bothHeld && !isShortcutActive)
                    {
                        isShortcutActive = true;
                        ShortcutTransitionChanged?.Invoke(this, ShortcutTransition.Pressed);
                    }
                    else if (!bothHeld && isShortcutActive)
                    {
                        isShortcutActive = false;
                        ShortcutTransitionChanged?.Invoke(this, ShortcutTransition.Released);
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

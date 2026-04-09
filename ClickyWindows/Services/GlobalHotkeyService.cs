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
///
/// IMPORTANT: We track key state ourselves rather than using GetAsyncKeyState, because
/// GetAsyncKeyState lags one event behind inside a WH_KEYBOARD_LL hook callback —
/// it reports the state BEFORE the current event, not after. This caused the shortcut
/// to only "activate" on key-up and immediately deactivate.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    public event EventHandler<ShortcutTransition>? ShortcutTransitionChanged;

    private IntPtr hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? hookProc;
    private bool isShortcutActive;

    // Track modifier key state ourselves — GetAsyncKeyState is unreliable inside hooks
    private bool lCtrlDown;
    private bool rCtrlDown;
    private bool lAltDown;
    private bool rAltDown;

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

            bool isKeyDown = messageType is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            bool isKeyUp = messageType is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                // Update our own key state tracking based on the current event
                switch (vkCode)
                {
                    case NativeMethods.VK_LCONTROL:
                        lCtrlDown = isKeyDown;
                        break;
                    case NativeMethods.VK_RCONTROL:
                        rCtrlDown = isKeyDown;
                        break;
                    case NativeMethods.VK_LMENU:
                        lAltDown = isKeyDown;
                        break;
                    case NativeMethods.VK_RMENU:
                        rAltDown = isKeyDown;
                        break;
                    default:
                        // Not a modifier we care about — skip transition check
                        goto done;
                }

                bool ctrlHeld = lCtrlDown || rCtrlDown;
                bool altHeld = lAltDown || rAltDown;
                bool bothHeld = ctrlHeld && altHeld;

                if (bothHeld && !isShortcutActive)
                {
                    isShortcutActive = true;
                    Debug.WriteLine($"[Clicky] Shortcut PRESSED (LCtrl={lCtrlDown} RCtrl={rCtrlDown} LAlt={lAltDown} RAlt={rAltDown})");
                    ShortcutTransitionChanged?.Invoke(this, ShortcutTransition.Pressed);
                }
                else if (!bothHeld && isShortcutActive)
                {
                    isShortcutActive = false;
                    Debug.WriteLine($"[Clicky] Shortcut RELEASED (LCtrl={lCtrlDown} RCtrl={rCtrlDown} LAlt={lAltDown} RAlt={rAltDown})");
                    ShortcutTransitionChanged?.Invoke(this, ShortcutTransition.Released);
                }
            }
        }

        done:
        return NativeMethods.CallNextHookEx(hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

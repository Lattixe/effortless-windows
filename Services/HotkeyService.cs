using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Effortless.Services;

public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    // Modifiers
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    // Virtual key codes
    private const uint VK_D = 0x44;
    private const uint VK_R = 0x52;
    private const uint VK_P = 0x50;
    private const uint VK_L = 0x4C;
    private const uint VK_V = 0x56;
    private const uint VK_SPACE = 0x20;

    // Hotkey IDs
    private const int HOTKEY_DONE = 1;
    private const int HOTKEY_ADD_TIME = 2;
    private const int HOTKEY_PAUSE = 3;
    private const int HOTKEY_TOGGLE_LIST = 4;
    private const int HOTKEY_TOGGLE_SCRATCHPAD = 5;
    private const int HOTKEY_VAULT = 6;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _windowHandle;
    private HwndSource? _source;
    private bool _disposed;

    public event EventHandler? MarkDonePressed;
    public event EventHandler? AddTimePressed;
    public event EventHandler? PausePressed;
    public event EventHandler? ToggleListPressed;
    public event EventHandler? ToggleScratchPadPressed;
    public event EventHandler? VaultPressed;

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);

        // Register hotkeys
        var ctrlAltModifiers = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
        var shiftAltModifiers = MOD_SHIFT | MOD_ALT | MOD_NOREPEAT;

        RegisterHotKey(_windowHandle, HOTKEY_DONE, ctrlAltModifiers, VK_D);              // Ctrl+Alt+D - Mark done
        RegisterHotKey(_windowHandle, HOTKEY_ADD_TIME, ctrlAltModifiers, VK_R);          // Ctrl+Alt+R - Add 5 min
        RegisterHotKey(_windowHandle, HOTKEY_PAUSE, ctrlAltModifiers, VK_SPACE);         // Ctrl+Alt+Space - Pause
        RegisterHotKey(_windowHandle, HOTKEY_TOGGLE_LIST, shiftAltModifiers, VK_L);      // Shift+Alt+L - Task list
        RegisterHotKey(_windowHandle, HOTKEY_TOGGLE_SCRATCHPAD, shiftAltModifiers, VK_P); // Shift+Alt+P - Scratch pad
        RegisterHotKey(_windowHandle, HOTKEY_VAULT, shiftAltModifiers, VK_V);            // Shift+Alt+V - Vault thought
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var hotkeyId = wParam.ToInt32();

            switch (hotkeyId)
            {
                case HOTKEY_DONE:
                    MarkDonePressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case HOTKEY_ADD_TIME:
                    AddTimePressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case HOTKEY_PAUSE:
                    PausePressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case HOTKEY_TOGGLE_LIST:
                    ToggleListPressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case HOTKEY_TOGGLE_SCRATCHPAD:
                    ToggleScratchPadPressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case HOTKEY_VAULT:
                    VaultPressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_DONE);
            UnregisterHotKey(_windowHandle, HOTKEY_ADD_TIME);
            UnregisterHotKey(_windowHandle, HOTKEY_PAUSE);
            UnregisterHotKey(_windowHandle, HOTKEY_TOGGLE_LIST);
            UnregisterHotKey(_windowHandle, HOTKEY_TOGGLE_SCRATCHPAD);
            UnregisterHotKey(_windowHandle, HOTKEY_VAULT);
        }

        _source?.RemoveHook(HwndHook);
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PromptAssistant.Services
{
    public class HotkeyService : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9001;

        // Modifier flags
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        private HwndSource? _source;
        private IntPtr _hwnd = IntPtr.Zero;
        private bool _registered = false;

        public event Action? HotkeyPressed;

        public void Attach(Window window)
        {
            _hwnd = new WindowInteropHelper(window).Handle;
            if (_hwnd == IntPtr.Zero)
            {
                window.SourceInitialized += (_, _) =>
                {
                    _hwnd = new WindowInteropHelper(window).Handle;
                    AttachSource();
                };
            }
            else
            {
                AttachSource();
            }
        }

        private void AttachSource()
        {
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(WndProc);
        }

        public bool Register(string modifiers, string key)
        {
            if (_hwnd == IntPtr.Zero) return false;

            if (_registered)
                UnregisterHotKey(_hwnd, HOTKEY_ID);

            uint mods = MOD_NOREPEAT;
            foreach (var part in modifiers.Split('+'))
                mods |= part.Trim().ToUpperInvariant() switch
                {
                    "ALT" => MOD_ALT,
                    "CTRL" or "CONTROL" => MOD_CONTROL,
                    "SHIFT" => MOD_SHIFT,
                    "WIN" => MOD_WIN,
                    _ => 0
                };

            uint vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(
                Enum.TryParse<System.Windows.Input.Key>(key, true, out var k) ? k : System.Windows.Input.Key.P);

            _registered = RegisterHotKey(_hwnd, HOTKEY_ID, mods, vk);
            return _registered;
        }

        /// <summary>Temporarily release the hotkey (e.g. while the user records a new one) without disposing the pump.</summary>
        public void Unregister()
        {
            if (_registered && _hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, HOTKEY_ID);
                _registered = false;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_registered && _hwnd != IntPtr.Zero)
                UnregisterHotKey(_hwnd, HOTKEY_ID);
            _source?.RemoveHook(WndProc);
        }
    }
}

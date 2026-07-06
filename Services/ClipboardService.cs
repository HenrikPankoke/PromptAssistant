using System.Runtime.InteropServices;
using System.Windows;

namespace PromptAssistant.Services
{
    public static class ClipboardService
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        // keybd_event chosen over SendInput on purpose: no INPUT struct / union / cbSize,
        // so it sidesteps the malformed-struct failure that broke an earlier attempt.
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_MENU    = 0x12; // Alt
        private const byte VK_SHIFT   = 0x10;
        private const byte VK_LMENU   = 0xA4, VK_RMENU  = 0xA5;
        private const byte VK_LSHIFT  = 0xA0, VK_RSHIFT = 0xA1;
        private const byte VK_LWIN    = 0x5B, VK_RWIN   = 0x5C;
        private const byte VK_C       = 0x43;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static IntPtr _lastForegroundWindow = IntPtr.Zero;

        private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>
        /// Blocks until the user has physically released every modifier key (Alt/Shift/Ctrl/Win),
        /// or the timeout elapses. We must wait for a GENUINE release — a synthetic key-up cannot
        /// un-press a physically-held key, it would only make GetAsyncKeyState lie. Only once the
        /// chord is truly up can an injected Ctrl+C be interpreted as a clean copy rather than
        /// Ctrl+Alt+Shift+C. Returns false if the keys were still held at the timeout.
        /// </summary>
        private static bool WaitForModifiersUp(int timeoutMs)
        {
            int waited = 0;
            // GetAsyncKeyState(VK_MENU/SHIFT/CONTROL) reports either left or right variant.
            while (IsDown(VK_MENU) || IsDown(VK_SHIFT) || IsDown(VK_CONTROL) ||
                   IsDown(VK_LWIN) || IsDown(VK_RWIN))
            {
                if (waited >= timeoutMs) return false;
                System.Threading.Thread.Sleep(10);
                waited += 10;
            }
            return true;
        }

        private static void SendCtrlC()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);               // Ctrl down
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);                     // C down
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);       // C up
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Ctrl up
        }

        // Clipboard COM can be transiently locked by another process (CLIPBRD_E_CANT_OPEN);
        // retry a few times rather than treating a transient lock as "nothing selected".
        private static string? ReadClipboardTextWithRetry()
        {
            for (int i = 0; i < 5; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch (COMException) { System.Threading.Thread.Sleep(30); } // transient lock — retry
                catch { return null; }                                      // anything else — give up cleanly
            }
            return null;
        }

        private static void RestoreClipboard(string? previous)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (previous != null) Clipboard.SetText(previous);
                    else Clipboard.Clear();
                    return;
                }
                catch (COMException) { System.Threading.Thread.Sleep(30); } // transient lock — retry
                catch { return; }                                           // anything else — give up cleanly
            }
        }

        /// <summary>Capture the currently active window handle and return any selected text via clipboard.</summary>
        public static (IntPtr hwnd, string selectedText) CaptureSelection()
        {
            _lastForegroundWindow = GetForegroundWindow();

            // Save old clipboard content
            string? old = null;
            try { old = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { }

            // Send Ctrl+C to get selected text
            System.Windows.Forms.SendKeys.SendWait("^c");
            System.Threading.Thread.Sleep(80);

            string selected = "";
            try { if (Clipboard.ContainsText()) selected = Clipboard.GetText(); } catch { }

            // Restore old clipboard
            try
            {
                if (old != null) Clipboard.SetText(old);
                else Clipboard.Clear();
            }
            catch { }

            return (_lastForegroundWindow, selected);
        }

        /// <summary>Paste text into the previously captured window.</summary>
        public static void PasteInto(IntPtr hwnd, string text)
        {
            if (hwnd == IntPtr.Zero) return;

            // Store current clipboard, put our text, paste, restore
            string? old = null;
            try { old = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { }

            try
            {
                Clipboard.SetText(text);
                SetForegroundWindow(hwnd);
                System.Threading.Thread.Sleep(50);
                System.Windows.Forms.SendKeys.SendWait("^v");
                System.Threading.Thread.Sleep(80);
            }
            finally
            {
                try
                {
                    if (old != null) Clipboard.SetText(old);
                    else Clipboard.Clear();
                }
                catch { }
            }
        }

        public static void CopyToClipboard(string text)
        {
            try { Clipboard.SetText(text); } catch { }
        }

        public static string ReadClipboardText()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { return ""; }
        }

        public static IntPtr CaptureSourceWindow()
        {
            _lastForegroundWindow = GetForegroundWindow();
            return _lastForegroundWindow;
        }

        /// <summary>
        /// Waits for the hotkey chord to be released, sends a clean Ctrl+C to the (still-focused)
        /// source window, and returns the selected text — or null if nothing was selected.
        /// Selection is detected via the clipboard sequence number; the previous clipboard content
        /// is saved and restored. Run on a background STA thread (it blocks while waiting for the
        /// user to lift the keys and while the source app copies).
        /// </summary>
        public static string? TryCaptureSelection(IntPtr sourceHwnd)
        {
            if (sourceHwnd == IntPtr.Zero) return null;

            uint seqBefore = GetClipboardSequenceNumber();
            string? previous = ReadClipboardTextWithRetry();

            try
            {
                // THE FIX: a global hotkey is a chord (e.g. Alt+Shift+P) the user is still holding
                // when it fires. Injecting Ctrl+C now would land as Ctrl+Alt+Shift+C and copy
                // nothing. Wait for a genuine physical release first. ~2s cap, then give up.
                if (!WaitForModifiersUp(2000)) return null;

                // No SetForegroundWindow: a global hotkey never stole the source app's focus, so
                // it still has it (our popup is not created until after this returns).
                SendCtrlC();

                // The source app copies asynchronously — poll the sequence number (cap ~800ms)
                // rather than a single fixed sleep that races slow apps (browsers, Office).
                uint seqAfter = seqBefore;
                for (int i = 0; i < 80; i++)
                {
                    System.Threading.Thread.Sleep(10);
                    seqAfter = GetClipboardSequenceNumber();
                    if (seqAfter != seqBefore) break;
                }
                if (seqAfter == seqBefore) return null; // nothing copied → nothing was selected

                string? captured = ReadClipboardTextWithRetry();
                RestoreClipboard(previous);
                return string.IsNullOrWhiteSpace(captured) ? null : captured;
            }
            catch
            {
                RestoreClipboard(previous);
                return null;
            }
        }

        public static IntPtr LastForegroundWindow => _lastForegroundWindow;
    }
}

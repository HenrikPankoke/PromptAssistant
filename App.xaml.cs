using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using PromptAssistant.Models;
using PromptAssistant.Services;
using PromptAssistant.Views;

namespace PromptAssistant
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _trayIcon;
        private HotkeyService? _hotkey;
        private HiddenHotkeyWindow? _hotkeyWindow;
        private PopupWindow? _popup;
        private int _capturing = 0; // guards against overlapping selection-capture threads
        private System.Threading.Mutex? _singleInstanceMutex;
        public static AppSettings Settings { get; set; } = new();
        public static AiService AiService { get; } = new();

        private const string MutexName = "PromptAssistant-SingleInstance-9F3A7B2E-4C1D-4E8A-9B5F-2D6C8A1F3E90";
        private const string ShowEventName = "PromptAssistant-Show-9F3A7B2E-4C1D-4E8A-9B5F-2D6C8A1F3E90";
        private System.Threading.EventWaitHandle? _showSignal;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Only one instance may run at a time. A second launch signals the running
            // instance to show its popup, then exits — so double-clicking the exe again
            // behaves like clicking the tray icon rather than silently doing nothing.
            _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                try { System.Threading.EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                Shutdown();
                return;
            }

            // Listen for that signal from later launches.
            _showSignal = new System.Threading.EventWaitHandle(
                false, System.Threading.EventResetMode.AutoReset, ShowEventName);
            var listener = new System.Threading.Thread(() =>
            {
                while (_showSignal.WaitOne())
                    Current.Dispatcher.BeginInvoke(new Action(ShowPopup));
            })
            { IsBackground = true, Name = "SingleInstanceSignal" };
            listener.Start();

            base.OnStartup(e);
            Settings = SettingsService.Load();
            StatsService.Load();
            ApplyTheme(Settings.Theme);
            SetupTrayIcon();

            // Hidden window just to hold the hotkey message pump
            _hotkeyWindow = new HiddenHotkeyWindow();
            _hotkey = new HotkeyService();
            _hotkey.Attach(_hotkeyWindow);
            _hotkey.HotkeyPressed += () => ShowPopup(fromHotkey: true);
            _hotkeyWindow.Show();
            _hotkeyWindow.Hide();

            // SourceInitialized fires during Show(), so _hwnd is valid by this point
            _hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);

            // Warm up the popup invisibly so the WebView2 + React UI are already
            // initialized and painted the first time the user actually opens it.
            _popup = new PopupWindow(IntPtr.Zero, null);
            _popup.Closed += (_, _) => _popup = null;
            _popup.WarmUp();
        }

        // ── Theme ──────────────────────────────────────────────────────────────
        public static void ApplyTheme(string theme)
        {
            Settings.Theme = theme;
            var themes = (ResourceDictionary)Current.Resources.MergedDictionaries[0];
            var dict = (ResourceDictionary)themes[theme == "Light" ? "LightTheme" : "DarkTheme"];

            // Apply to Application resources
            foreach (var key in dict.Keys)
                Current.Resources[key] = dict[key];
        }

        // ── Tray icon ──────────────────────────────────────────────────────────
        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = CreateIcon(),
                Visible = true,
                Text = "Prompt Assistant"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Prompt Assistant", null, (_, _) => ShowPopup());
            menu.Items.Add(new ToolStripSeparator());

            var themeItem = new ToolStripMenuItem("Theme");
            themeItem.DropDownItems.Add("Dark",  null, (_, _) => SetTheme("Dark"));
            themeItem.DropDownItems.Add("Light", null, (_, _) => SetTheme("Light"));
            menu.Items.Add(themeItem);

            menu.Items.Add("Settings", null, (_, _) => OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApp());

            _trayIcon.ContextMenuStrip = menu;
            // Single left-click opens the popup; right-click shows the context menu.
            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowPopup();
            };
        }

        private static System.Drawing.Icon CreateIcon()
        {
            var stream = GetResourceStream(new Uri("pack://application:,,,/Resources/icon.ico"))?.Stream;
            if (stream != null)
                return new System.Drawing.Icon(stream);

            // Fallback if resource is missing
            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.FillEllipse(new SolidBrush(Color.FromArgb(26, 21, 53)), 0, 0, 15, 15);
            g.FillEllipse(new SolidBrush(Color.FromArgb(124, 106, 247)), 2, 2, 11, 11);
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        // ── Show popup ─────────────────────────────────────────────────────────
        // Back-compat overload for the tray entries (no source selection available there).
        public void ShowPopup() => ShowPopup(fromHotkey: false);

        // Called on the UI thread (WM_HOTKEY handler / tray events both fire here).
        public void ShowPopup(bool fromHotkey)
        {
            // Grab the source window immediately, while it is still the foreground window. Only
            // the hotkey path has a usable source app — clicking the tray already moved foreground
            // to the shell, so selection capture can't work there.
            var hwnd = fromHotkey ? ClipboardService.CaptureSourceWindow() : IntPtr.Zero;

            // Already open and focused → toggle it off. No capture needed.
            if (_popup != null && _popup.IsVisible && _popup.IsActive)
            {
                _popup.Hide();
                return;
            }

            if (!fromHotkey)
            {
                ShowOrToggle(hwnd, null);
                return;
            }

            // Ignore a second hotkey press while a capture is already in flight (prevents two
            // threads racing on the clipboard save/restore).
            if (System.Threading.Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return;

            // Hotkey path: capture the selection on a background STA thread so waiting for the user
            // to release the chord (and polling the clipboard) never freezes the UI thread. STA is
            // required for the WPF Clipboard APIs. Then marshal back to show the popup.
            var capture = new System.Threading.Thread(() =>
            {
                string? selected = null;
                try { selected = ClipboardService.TryCaptureSelection(hwnd); }
                finally { System.Threading.Interlocked.Exchange(ref _capturing, 0); }
                Current.Dispatcher.Invoke(() => ShowOrToggle(hwnd, selected));
            })
            { IsBackground = true, Name = "SelectionCapture" };
            capture.SetApartmentState(System.Threading.ApartmentState.STA);
            capture.Start();
        }

        // Reuses the single popup instance (preserves chat history etc.) and never leaves a
        // dangling reference to a closed window.
        private void ShowOrToggle(IntPtr hwnd, string? selectedText)
        {
            if (_popup == null)
            {
                _popup = new PopupWindow(hwnd, selectedText);
                _popup.Closed += (_, _) => _popup = null;
                _popup.Show();
                _popup.Activate();
                return;
            }

            _popup.PrepareForShow(hwnd, selectedText);
            if (!_popup.IsVisible) _popup.Show();
            _popup.Activate();
        }

        // ── Settings ───────────────────────────────────────────────────────────
        // The settings UI now lives inside the web app; open the popup and navigate to it.
        public static void OpenSettings(int tab = -1)
        {
            if (Current is not App app) return;
            app.EnsurePopupShown();
            app._popup?.NavigateToSettings();
        }

        private void EnsurePopupShown() => ShowOrToggle(IntPtr.Zero, null);

        /// <summary>Called by the bridge after the web UI persists settings.</summary>
        public static void OnSettingsSaved()
        {
            if (Current is not App app) return;
            app._hotkey?.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);
            ApplyTheme(Settings.Theme);
            app._popup?.ApplyWindowTheme(Settings.Theme);
        }

        /// <summary>Release the global hotkey while the user records a new combination in Settings.</summary>
        public static void SuspendHotkey() => (Current as App)?._hotkey?.Unregister();

        /// <summary>Re-register the currently configured hotkey (used when recording is cancelled).</summary>
        public static void ResumeHotkey()
        {
            if (Current is App app) app._hotkey?.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);
        }

        private void SetTheme(string theme)
        {
            ApplyTheme(theme);
            SettingsService.Save(Settings);
            _popup?.PostTheme(theme);
        }

        // ── Exit ───────────────────────────────────────────────────────────────
        private void ExitApp()
        {
            _trayIcon?.Dispose();
            _hotkey?.Dispose();
            _hotkeyWindow?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _hotkey?.Dispose();
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}

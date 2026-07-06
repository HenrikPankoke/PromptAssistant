using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using PromptAssistant.Services;

namespace PromptAssistant.Views
{
    /// <summary>
    /// Thin native host: a borderless window containing a WebView2 that renders the
    /// React UI. All UI logic lives in the web app; this class wires the message
    /// bridge and the few native behaviours (drag, selection injection, insert, hide).
    /// </summary>
    public partial class PopupWindow : Window, IBridgeHost
    {
        private IntPtr _sourceHwnd;
        private readonly WebBridge _bridge;
        private bool _webReady;
        private bool _warming;
        private string? _pendingSelection;
        private bool _pendingShown;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public PopupWindow(IntPtr sourceHwnd, string? selectedText = null)
        {
            InitializeComponent();
            _sourceHwnd = sourceHwnd;
            _pendingSelection = selectedText;
            _bridge = new WebBridge(this);
            SourceInitialized += (_, _) => ApplyRoundedCorners();
            SizeChanged += (_, _) => ApplyRoundedCorners();
            Loaded += async (_, _) => await InitWebAsync();
        }

        private async Task InitWebAsync()
        {
            var userData = Path.Combine(Path.GetTempPath(), "PromptAssistant.WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userData, null);
            await Web.EnsureCoreWebView2Async(env);

            var core = Web.CoreWebView2;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            core.SetVirtualHostNameToFolderMapping(
                "app.promptassistant", root, CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += (_, e) =>
            {
                try { _bridge.HandleMessage(e.WebMessageAsJson); } catch { }
            };

            ApplyWindowTheme(App.Settings.Theme);
            // Theme travels in the URL so the page applies the right CSS variables
            // before first paint — no light-to-dark flash while state loads.
            Web.Source = new Uri($"https://app.promptassistant/index.html?theme={App.Settings.Theme}");
        }

        // ── Warm-up ──────────────────────────────────────────────────────────────
        // Shown invisibly (transparent, not activated) right after app startup so the
        // WebView2 + React UI initialize in the background; hidden again once the web
        // app reports ready. The first real open then shows a fully rendered UI.
        public void WarmUp()
        {
            _warming = true;
            Opacity = 0;
            ShowActivated = false;
            Show();
        }

        private void CancelWarmUp()
        {
            if (!_warming) return;
            _warming = false;
            Opacity = 1;
            ShowActivated = true;
        }

        // ── Window theming ─────────────────────────────────────────────────────
        // The window has no native caption (WindowStyle=None, no WebView2 non-client
        // region trickery — that Windows-11-only feature painted an OS-colored sliver
        // that never followed our theme, and looked visibly wrong on Windows 10). The
        // whole title bar is plain HTML, so all we set natively is the dark-mode window
        // border outline (supported Win10 2004+ and Win11 — safe no-op if unsupported)
        // and the WebView2 background used only for the instant before the page paints.
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public void ApplyWindowTheme(string theme)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            bool dark = theme == "Dark";
            int darkFlag = dark ? 1 : 0;
            try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int)); } catch { }

            // Keep every pre-paint surface (WPF window, WebView2 default) in the theme
            // color so nothing mismatched can flash through before the page renders.
            Background = new SolidColorBrush(dark
                ? Color.FromRgb(0x15, 0x15, 0x17)
                : Color.FromRgb(0xFB, 0xFB, 0xFA));
            Web.DefaultBackgroundColor = dark
                ? System.Drawing.Color.FromArgb(0x15, 0x15, 0x17)
                : System.Drawing.Color.FromArgb(0xFB, 0xFB, 0xFA);
        }

        // ── Rounded corners (works identically on Windows 10 and 11) ─────────────
        // Windows 11 rounds top-level windows automatically via DWM; Windows 10 never
        // did, which is why corners looked square there. Clipping the window to a
        // rounded-rectangle region gives the same look on both, independent of DWM.
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int CornerRadiusDip = 10;

        private void ApplyRoundedCorners()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0 || WindowState != WindowState.Normal) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
            int h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
            int r = (int)Math.Round(CornerRadiusDip * dpi.DpiScaleX);

            var region = CreateRoundRectRgn(0, 0, w + 1, h + 1, r, r);
            if (region != IntPtr.Zero)
            {
                SetWindowRgn(hwnd, region, true); // ownership transfers to the window; do not DeleteObject
            }
        }

        // ── Dragging via the HTML title bar ───────────────────────────────────────
        // No native caption exists to drag by, so the web app posts "startDrag" on
        // mousedown over its title bar. Window.DragMove() doesn't work here — it relies
        // on WPF's own tracked mouse-button state, but the mousedown happened inside the
        // WebView2 child window, which WPF's input system never sees, so DragMove()
        // just throws immediately. Instead, tell Windows directly that this was a
        // caption click, which starts the native move loop independent of WPF's input
        // tracking — the standard trick for WebView2/CEF/Electron-style hybrid apps.
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        public void StartDrag()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        // ── Re-arm for a fresh hotkey invocation (reuses the instance) ────────────
        public void PrepareForShow(IntPtr sourceHwnd, string? selectedText)
        {
            CancelWarmUp(); // user beat the warm-up — make the window visible as-is
            _sourceHwnd = sourceHwnd;
            if (!string.IsNullOrEmpty(selectedText)) _pendingSelection = selectedText;
            else _pendingShown = true;
            FlushPending();
            Dispatcher.BeginInvoke(new Action(() => Web.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        public void RefreshTheme() => PostTheme(App.Settings.Theme);
        public void PostTheme(string theme)
        {
            PushEvent("theme", new { theme });
            ApplyWindowTheme(theme);
        }
        public void NavigateToSettings() => PushEvent("navigate", new { view = "settings" });

        private void FlushPending()
        {
            if (!_webReady) return;
            if (!string.IsNullOrEmpty(_pendingSelection))
            {
                PushEvent("selection", new { text = _pendingSelection });
                _pendingSelection = null;
                _pendingShown = false;
            }
            else if (_pendingShown)
            {
                PushEvent("shown", new { });
                _pendingShown = false;
            }
        }

        // ── IBridgeHost ──────────────────────────────────────────────────────────
        public IntPtr SourceHwnd => _sourceHwnd;
        public bool HasSource => _sourceHwnd != IntPtr.Zero;

        public void PostToWeb(string json) =>
            Dispatcher.Invoke(() => Web.CoreWebView2?.PostWebMessageAsJson(json));

        public void HideWindow() => Dispatcher.Invoke(Hide);

        public void OnWebReady() => Dispatcher.Invoke(() =>
        {
            _webReady = true;
            if (_warming)
            {
                // Warm-up complete — tuck the window away until the user asks for it.
                Hide();
                _warming = false;
                Opacity = 1;
                ShowActivated = true;
            }
            FlushPending();
        });

        public void InsertIntoSource(string text)
        {
            Hide();
            Task.Delay(150).ContinueWith(_ =>
            {
                ClipboardService.PasteInto(_sourceHwnd, text);
                Dispatcher.Invoke(() =>
                {
                    Show();
                    Activate();
                });
            });
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private void PushEvent(string evt, object payload) =>
            PostToWeb(JsonSerializer.Serialize(new { type = "push", @event = evt, payload }, Json));
    }
}

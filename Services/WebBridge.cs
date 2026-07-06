using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PromptAssistant.Models;

namespace PromptAssistant.Services
{
    /// <summary>Implemented by the WebView2 host window so the bridge can talk back to it.</summary>
    public interface IBridgeHost
    {
        IntPtr SourceHwnd { get; }
        bool HasSource { get; }
        void PostToWeb(string json);
        void HideWindow();
        void InsertIntoSource(string text);
        void OnWebReady();
        void StartDrag();
    }

    /// <summary>
    /// Routes JSON messages between the React UI (WebView2) and the native services.
    /// Protocol: JS sends { id, type, payload }; we reply { id, ok, result|error }.
    /// Streaming sends { type:"stream", id, chunk } … then { type:"streamEnd"|"streamError", id }.
    /// Unsolicited pushes: { type:"push", event, payload }.
    /// </summary>
    public class WebBridge
    {
        private readonly IBridgeHost _host;
        private readonly Dictionary<int, CancellationTokenSource> _streams = new();

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };

        public WebBridge(IBridgeHost host) => _host = host;

        // ── Entry point (called on UI thread from WebMessageReceived) ────────────
        public async void HandleMessage(string messageJson)
        {
            int id = -1;
            try
            {
                using var doc = JsonDocument.Parse(messageJson);
                var root = doc.RootElement;
                var type = root.GetProperty("type").GetString() ?? "";
                if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                    id = idEl.GetInt32();
                var payload = root.TryGetProperty("payload", out var p) ? p : default;

                switch (type)
                {
                    case "getState":
                        Reply(id, await BuildStateAsync());
                        _host.OnWebReady();
                        break;

                    case "saveSettings":
                        App.Settings = payload.Deserialize<AppSettings>(Json) ?? App.Settings;
                        SettingsService.Save(App.Settings);
                        App.OnSettingsSaved();
                        Reply(id, null);
                        break;

                    case "saveRecipe":
                        SaveRecipe(payload.GetProperty("recipe").Deserialize<Recipe>(Json)!);
                        Reply(id, null);
                        break;

                    case "deleteRecipe":
                        {
                            var rid = payload.GetProperty("id").GetString();
                            App.Settings.Recipes.RemoveAll(r => r.Id == rid);
                            SettingsService.Save(App.Settings);
                            Reply(id, null);
                        }
                        break;

                    case "fetchModels":
                        Reply(id, await FetchModelsAsync(payload.GetProperty("provider").GetString() ?? ""));
                        break;

                    case "runPrompt":
                        await RunPromptAsync(id, payload);
                        break;

                    case "sendChat":
                        await SendChatAsync(id, payload);
                        break;

                    case "cancel":
                        {
                            var sid = payload.GetProperty("id").GetInt32();
                            if (_streams.TryGetValue(sid, out var cts)) cts.Cancel();
                        }
                        break;

                    case "readClipboard":
                        Reply(id, ClipboardService.ReadClipboardText());
                        break;

                    case "copyToClipboard":
                        ClipboardService.CopyToClipboard(payload.GetProperty("text").GetString() ?? "");
                        Reply(id, null);
                        break;

                    case "insertIntoSource":
                        _host.InsertIntoSource(payload.GetProperty("text").GetString() ?? "");
                        Reply(id, null);
                        break;

                    case "exportChat":
                        ExportChat(payload.GetProperty("markdown").GetString() ?? "");
                        Reply(id, null);
                        break;

                    case "setStartup":
                        StartupService.Set(payload.GetProperty("enabled").GetBoolean());
                        Reply(id, null);
                        break;

                    case "openConfigFile":
                        OpenConfigFile();
                        Reply(id, null);
                        break;

                    case "copilotLogin":
                        Reply(id, null);
                        _ = CopilotLoginAsync();
                        break;

                    case "copilotLogout":
                        App.Settings.Providers.GitHubCopilotToken = "";
                        App.Settings.Providers.GitHubCopilotTokenExpiry = "";
                        SettingsService.Save(App.Settings);
                        Reply(id, null);
                        break;

                    case "startDrag":
                        _host.StartDrag();
                        Reply(id, null);
                        break;

                    case "suspendHotkey":
                        App.SuspendHotkey();
                        Reply(id, null);
                        break;

                    case "resumeHotkey":
                        App.ResumeHotkey();
                        Reply(id, null);
                        break;

                    case "close":
                        _host.HideWindow();
                        Reply(id, null);
                        break;

                    default:
                        ReplyError(id, $"Unknown message type: {type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                if (id >= 0) ReplyError(id, ex.Message);
            }
        }

        // ── State ────────────────────────────────────────────────────────────────
        private async Task<object> BuildStateAsync()
        {
            var models = await FetchModelsAsync(App.Settings.ActiveProvider);
            return new
            {
                settings = App.Settings,
                models,
                stats = new
                {
                    today = StatsService.GetToday(),
                    week = StatsService.GetWeekly(),
                    total = StatsService.GetTotal(),
                    daily = StatsService.GetDailyCounts(),
                },
                configPath = SettingsService.ConfigFilePath,
                startupEnabled = StartupService.IsEnabled(),
                copilotLoggedIn = !string.IsNullOrEmpty(App.Settings.Providers.GitHubCopilotToken),
                hasSource = _host.HasSource,
            };
        }

        private static async Task<List<ModelEntry>> FetchModelsAsync(string provider) => provider switch
        {
            "Anthropic" => ModelCatalog.AnthropicModels(),
            "OpenAI" => ModelCatalog.OpenAiModels(),
            "GitHubCopilot" => ModelCatalog.GitHubCopilotModels(),
            "Ollama" => await ModelCatalog.FetchOllamaModelsAsync(App.Settings.Providers.OllamaBaseUrl),
            "OpenRouter" => await ModelCatalog.FetchOpenRouterModelsAsync(App.Settings.Providers.OpenRouterApiKey),
            _ => new List<ModelEntry>(),
        };

        private static void SaveRecipe(Recipe recipe)
        {
            var idx = App.Settings.Recipes.FindIndex(r => r.Id == recipe.Id);
            if (idx >= 0) App.Settings.Recipes[idx] = recipe;
            else App.Settings.Recipes.Add(recipe);
            SettingsService.Save(App.Settings);
        }

        // ── Streaming: prompt ────────────────────────────────────────────────────
        private async Task RunPromptAsync(int id, JsonElement payload)
        {
            var recipeId = payload.GetProperty("recipeId").GetString();
            var recipe = App.Settings.Recipes.FirstOrDefault(r => r.Id == recipeId);
            if (recipe == null) { PostStreamError(id, "Recipe not found."); return; }

            var input = payload.GetProperty("input").GetString() ?? "";
            var pvals = new Dictionary<string, string>();
            if (payload.TryGetProperty("params", out var pv) && pv.ValueKind == JsonValueKind.Object)
                foreach (var prop in pv.EnumerateObject())
                    pvals[prop.Name] = prop.Value.GetString() ?? "";

            Enum.TryParse<AiProvider>(App.Settings.ActiveProvider, out var provider);
            var req = new CompletionRequest
            {
                Recipe = recipe,
                InputText = input,
                ParameterValues = pvals,
                Provider = provider,
                ModelId = App.Settings.ActiveModelId,
            };

            var cts = new CancellationTokenSource();
            _streams[id] = cts;
            try
            {
                await foreach (var chunk in App.AiService.StreamAsync(req, App.Settings, cts.Token))
                    PostStreamChunk(id, chunk);
                PostStreamEnd(id);
                StatsService.RecordExecution();
            }
            catch (OperationCanceledException) { PostStreamEnd(id); }
            catch (Exception ex) { PostStreamError(id, ex.Message); }
            finally { _streams.Remove(id); }
        }

        // ── Streaming: chat ──────────────────────────────────────────────────────
        private async Task SendChatAsync(int id, JsonElement payload)
        {
            var system = payload.GetProperty("system").GetString() ?? "";
            var history = new List<ChatMessage>();
            foreach (var m in payload.GetProperty("history").EnumerateArray())
                history.Add(new ChatMessage
                {
                    Role = m.GetProperty("role").GetString() ?? "user",
                    Content = m.GetProperty("content").GetString() ?? "",
                });

            var cts = new CancellationTokenSource();
            _streams[id] = cts;
            try
            {
                await foreach (var chunk in App.AiService.StreamChatAsync(system, history, App.Settings, cts.Token))
                    PostStreamChunk(id, chunk);
                PostStreamEnd(id);
                StatsService.RecordExecution();
            }
            catch (OperationCanceledException) { PostStreamEnd(id); }
            catch (Exception ex) { PostStreamError(id, ex.Message); }
            finally { _streams.Remove(id); }
        }

        // ── Copilot device-flow login ────────────────────────────────────────────
        private async Task CopilotLoginAsync()
        {
            PushCopilot("Requesting device code…", false);
            try
            {
                var device = await GitHubCopilotAuthService.RequestDeviceCodeAsync();
                if (device == null) { PushCopilot("Failed to get device code.", false); return; }

                PushCopilot($"Enter code  {device.UserCode}  at {device.VerificationUri} — waiting for authorization…", false);
                GitHubCopilotAuthService.OpenBrowser(device.VerificationUri);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(device.ExpiresIn));
                var result = await GitHubCopilotAuthService.PollForTokenAsync(device, cts.Token);

                if (!string.IsNullOrEmpty(result.AccessToken))
                {
                    App.Settings.Providers.GitHubCopilotToken = result.AccessToken;
                    SettingsService.Save(App.Settings);
                    PushCopilot("✓ Logged in to GitHub Copilot", true);
                }
                else PushCopilot($"Auth failed: {result.Error}", false);
            }
            catch (Exception ex) { PushCopilot($"Error: {ex.Message}", false); }
        }

        // ── Export chat to markdown ──────────────────────────────────────────────
        private static void ExportChat(string markdown)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export chat",
                Filter = "Markdown file (*.md)|*.md",
                FileName = $"chat-{DateTime.Now:yyyy-MM-dd-HHmm}.md",
                DefaultExt = ".md",
            };
            if (dlg.ShowDialog() == true)
                File.WriteAllText(dlg.FileName, markdown, System.Text.Encoding.UTF8);
        }

        private static void OpenConfigFile()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{SettingsService.ConfigFilePath}\"") { UseShellExecute = true });
            }
            catch { }
        }

        // ── Outgoing helpers ─────────────────────────────────────────────────────
        private void Reply(int id, object? result) =>
            _host.PostToWeb(JsonSerializer.Serialize(new { id, ok = true, result }, Json));

        private void ReplyError(int id, string error) =>
            _host.PostToWeb(JsonSerializer.Serialize(new { id, ok = false, error }, Json));

        private void PostStreamChunk(int id, string chunk) =>
            _host.PostToWeb(JsonSerializer.Serialize(new { type = "stream", id, chunk }, Json));

        private void PostStreamEnd(int id) =>
            _host.PostToWeb(JsonSerializer.Serialize(new { type = "streamEnd", id }, Json));

        private void PostStreamError(int id, string error) =>
            _host.PostToWeb(JsonSerializer.Serialize(new { type = "streamError", id, error }, Json));

        private void PushCopilot(string text, bool loggedIn) =>
            _host.PostToWeb(JsonSerializer.Serialize(new { type = "push", @event = "copilot", payload = new { text, loggedIn } }, Json));
    }
}

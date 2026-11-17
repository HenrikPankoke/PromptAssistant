using PromptAssistant.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PromptAssistant.Services
{
    /// <summary>Provides static model lists and dynamic fetching for Ollama/OpenRouter.</summary>
    public static class ModelCatalog
    {
        public static List<ModelEntry> AnthropicModels() =>
        [
            new() { Provider = AiProvider.Anthropic, Id = "claude-opus-4-6",    DisplayName = "Claude Opus 4.6" },
            new() { Provider = AiProvider.Anthropic, Id = "claude-opus-4-7",    DisplayName = "Claude Opus 4.7" },
            new() { Provider = AiProvider.Anthropic, Id = "claude-opus-4-8",    DisplayName = "Claude Opus 4.8" },
            new() { Provider = AiProvider.Anthropic, Id = "claude-sonnet-4-6",  DisplayName = "Claude Sonnet 4.6" },
            new() { Provider = AiProvider.Anthropic, Id = "claude-haiku-4-5-20251001", DisplayName = "Claude Haiku 4.5" },
        ];

        public static List<ModelEntry> OpenAiModels() =>
        [
            new() { Provider = AiProvider.OpenAI, Id = "gpt-4o",          DisplayName = "GPT-4o" },
            new() { Provider = AiProvider.OpenAI, Id = "gpt-4o-mini",     DisplayName = "GPT-4o Mini" },
            new() { Provider = AiProvider.OpenAI, Id = "gpt-4-turbo",     DisplayName = "GPT-4 Turbo" },
            new() { Provider = AiProvider.OpenAI, Id = "gpt-3.5-turbo",   DisplayName = "GPT-3.5 Turbo" },
            new() { Provider = AiProvider.OpenAI, Id = "o1",              DisplayName = "o1" },
            new() { Provider = AiProvider.OpenAI, Id = "o1-mini",         DisplayName = "o1 Mini" },
            new() { Provider = AiProvider.OpenAI, Id = "o3-mini",         DisplayName = "o3 Mini" },
        ];

        // Fallback list used only when the live /models fetch is unavailable (not logged
        // in, offline, endpoint error). Kept to known-valid Copilot IDs — Claude is left
        // out here because its Copilot ID varies by account/policy and is discovered live.
        public static List<ModelEntry> GitHubCopilotModels() =>
        [
            new() { Provider = AiProvider.GitHubCopilot, Id = "gpt-4o",      DisplayName = "GPT-4o (Copilot)" },
            new() { Provider = AiProvider.GitHubCopilot, Id = "gpt-4o-mini", DisplayName = "GPT-4o Mini (Copilot)" },
            new() { Provider = AiProvider.GitHubCopilot, Id = "o1",          DisplayName = "o1 (Copilot)" },
        ];

        // ── Dynamic: GitHub Copilot ────────────────────────────────────────────
        // Lists every model the user's Copilot subscription exposes. Tries the stored
        // OAuth token directly first; if the endpoint rejects it, exchanges it for a
        // Copilot session token (copilot_internal/v2/token) and retries against the
        // account-specific API host. Falls back to the static list only if both fail.
        public static async Task<List<ModelEntry>> FetchGitHubCopilotModelsAsync(string token)
        {
            if (string.IsNullOrEmpty(token)) return GitHubCopilotModels();

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

                var models = await TryFetchCopilotModelsAsync(http, "https://api.githubcopilot.com", token);
                if (models == null)
                {
                    var (sessionToken, apiBase) = await ExchangeCopilotSessionTokenAsync(http, token);
                    if (sessionToken != null)
                        models = await TryFetchCopilotModelsAsync(http, apiBase ?? "https://api.githubcopilot.com", sessionToken);
                }

                return models is { Count: > 0 } ? models : GitHubCopilotModels();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copilot model fetch failed: {ex.Message}");
                return GitHubCopilotModels();
            }
        }

        private static void AddCopilotHeaders(HttpRequestMessage req)
        {
            req.Headers.Add("Copilot-Integration-Id", "vscode-chat");
            req.Headers.Add("Editor-Version", "vscode/1.99.0");
            req.Headers.Add("Editor-Plugin-Version", "copilot-chat/0.26.7");
            req.Headers.Add("User-Agent", "GitHubCopilotChat/0.26.7");
            req.Headers.Add("X-GitHub-Api-Version", "2025-04-01");
        }

        private static async Task<List<ModelEntry>?> TryFetchCopilotModelsAsync(HttpClient http, string apiBase, string token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{apiBase.TrimEnd('/')}/models");
            req.Headers.Add("Authorization", $"Bearer {token}");
            AddCopilotHeaders(req);

            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Copilot /models at {apiBase} returned {(int)resp.StatusCode}");
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;

            var seen = new HashSet<string>();
            var result = new List<ModelEntry>();
            foreach (var m in arr.EnumerateArray())
            {
                var id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;

                // Skip non-chat models (embeddings etc.) when capability info is present.
                if (m.TryGetProperty("capabilities", out var caps) &&
                    caps.TryGetProperty("type", out var capType) &&
                    capType.GetString() is string t && t != "chat")
                    continue;

                // Skip models the account's policy has explicitly disabled.
                if (m.TryGetProperty("policy", out var pol) &&
                    pol.TryGetProperty("state", out var st) &&
                    st.GetString() == "disabled")
                    continue;

                var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                result.Add(new ModelEntry
                {
                    Provider = AiProvider.GitHubCopilot,
                    Id = id,
                    DisplayName = $"{name} (Copilot)"
                });
            }
            result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static async Task<(string? Token, string? ApiBase)> ExchangeCopilotSessionTokenAsync(HttpClient http, string oauthToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/v2/token");
            req.Headers.Add("Authorization", $"token {oauthToken}");
            AddCopilotHeaders(req);

            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Copilot token exchange returned {(int)resp.StatusCode}");
                return (null, null);
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tok = root.TryGetProperty("token", out var t) ? t.GetString() : null;
            string? api = null;
            if (root.TryGetProperty("endpoints", out var ep) && ep.TryGetProperty("api", out var a))
                api = a.GetString();
            return (tok, api);
        }

        // ── Dynamic: Ollama ────────────────────────────────────────────────────
        public static async Task<List<ModelEntry>> FetchOllamaModelsAsync(string baseUrl)
        {
            var result = new List<ModelEntry>();
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var json = await http.GetStringAsync($"{baseUrl.TrimEnd('/')}/api/tags");
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var arr))
                    foreach (var m in arr.EnumerateArray())
                    {
                        var name = m.GetProperty("name").GetString() ?? "";
                        result.Add(new ModelEntry
                        {
                            Provider = AiProvider.Ollama,
                            Id = name,
                            DisplayName = name
                        });
                    }
            }
            catch { }
            return result;
        }

        // ── Dynamic: OpenRouter ────────────────────────────────────────────────
        public static async Task<List<ModelEntry>> FetchOpenRouterModelsAsync(string apiKey)
        {
            var result = new List<ModelEntry>();
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                var json = await http.GetStringAsync("https://openrouter.ai/api/v1/models");
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var arr))
                    foreach (var m in arr.EnumerateArray())
                    {
                        var id = m.GetProperty("id").GetString() ?? "";
                        var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                        result.Add(new ModelEntry
                        {
                            Provider = AiProvider.OpenRouter,
                            Id = id,
                            DisplayName = name
                        });
                    }
            }
            catch { }
            return result;
        }
    }
}

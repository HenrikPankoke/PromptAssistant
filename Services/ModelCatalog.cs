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
        // Lists the models the user's Copilot subscription actually exposes (correct IDs,
        // respecting account/policy enablement) from the same endpoint + auth the chat
        // requests use. Falls back to the static list on any failure so the dropdown is
        // never empty and the provider stays usable.
        public static async Task<List<ModelEntry>> FetchGitHubCopilotModelsAsync(string token)
        {
            if (string.IsNullOrEmpty(token)) return GitHubCopilotModels();

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var req = new System.Net.Http.HttpRequestMessage(HttpMethod.Get, "https://api.githubcopilot.com/models");
                req.Headers.Add("Authorization", $"Bearer {token}");
                req.Headers.Add("Copilot-Integration-Id", "vscode-chat");
                req.Headers.Add("Editor-Version", "vscode/1.90.0");

                using var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return GitHubCopilotModels();

                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var arr))
                    return GitHubCopilotModels();

                var seen = new HashSet<string>();
                var result = new List<ModelEntry>();
                foreach (var m in arr.EnumerateArray())
                {
                    var id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;

                    // Keep only chat-capable models (skip embeddings) when capability info is present.
                    if (m.TryGetProperty("capabilities", out var caps) &&
                        caps.TryGetProperty("type", out var capType) &&
                        capType.GetString() is string t && t != "chat")
                        continue;

                    var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                    result.Add(new ModelEntry
                    {
                        Provider = AiProvider.GitHubCopilot,
                        Id = id,
                        DisplayName = $"{name} (Copilot)"
                    });
                }

                return result.Count > 0 ? result : GitHubCopilotModels();
            }
            catch
            {
                return GitHubCopilotModels();
            }
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

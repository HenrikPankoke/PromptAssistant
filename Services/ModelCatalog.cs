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

        public static List<ModelEntry> GitHubCopilotModels() =>
        [
            new() { Provider = AiProvider.GitHubCopilot, Id = "gpt-4o",            DisplayName = "GPT-4o (Copilot)" },
            new() { Provider = AiProvider.GitHubCopilot, Id = "gpt-4o-mini",       DisplayName = "GPT-4o Mini (Copilot)" },
            new() { Provider = AiProvider.GitHubCopilot, Id = "o1",                DisplayName = "o1 (Copilot)" },
            new() { Provider = AiProvider.GitHubCopilot, Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6 (Copilot)" },
        ];

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

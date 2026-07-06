using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using PromptAssistant.Models;

namespace PromptAssistant.Services
{
    public class AiService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

        // ── Entry point ────────────────────────────────────────────────────────
        public async IAsyncEnumerable<string> StreamAsync(
            CompletionRequest req,
            AppSettings settings,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            string userContent = BuildPrompt(req);

            switch (req.Provider)
            {
                case AiProvider.Anthropic:
                    await foreach (var t in AnthropicStreamAsync(req.ModelId, req.Recipe.SystemPrompt, userContent, settings.Providers.AnthropicApiKey, ct))
                        yield return t;
                    break;

                case AiProvider.OpenAI:
                    await foreach (var t in OpenAiStreamAsync("https://api.openai.com/v1/chat/completions", req.ModelId, req.Recipe.SystemPrompt, userContent, settings.Providers.OpenAiApiKey, ct))
                        yield return t;
                    break;

                case AiProvider.OpenRouter:
                    await foreach (var t in OpenAiStreamAsync("https://openrouter.ai/api/v1/chat/completions", req.ModelId, req.Recipe.SystemPrompt, userContent, settings.Providers.OpenRouterApiKey, ct))
                        yield return t;
                    break;

                case AiProvider.Ollama:
                    await foreach (var t in OllamaStreamAsync(settings.Providers.OllamaBaseUrl, req.ModelId, req.Recipe.SystemPrompt, userContent, ct))
                        yield return t;
                    break;

                case AiProvider.GitHubCopilot:
                    await foreach (var t in GitHubCopilotStreamAsync(req.ModelId, req.Recipe.SystemPrompt, userContent, settings.Providers.GitHubCopilotToken, ct))
                        yield return t;
                    break;
            }
        }

        // ── Chat entry point ───────────────────────────────────────────────────
        public async IAsyncEnumerable<string> StreamChatAsync(
            string systemPrompt,
            IReadOnlyList<ChatMessage> history,
            AppSettings settings,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Enum.TryParse<AiProvider>(settings.ActiveProvider, out var provider);

            switch (provider)
            {
                case AiProvider.Anthropic:
                    await foreach (var t in AnthropicChatAsync(settings.ActiveModelId, systemPrompt, history, settings.Providers.AnthropicApiKey, ct))
                        yield return t;
                    break;
                case AiProvider.OpenAI:
                    await foreach (var t in OpenAiChatAsync("https://api.openai.com/v1/chat/completions", settings.ActiveModelId, systemPrompt, history, settings.Providers.OpenAiApiKey, ct))
                        yield return t;
                    break;
                case AiProvider.OpenRouter:
                    await foreach (var t in OpenAiChatAsync("https://openrouter.ai/api/v1/chat/completions", settings.ActiveModelId, systemPrompt, history, settings.Providers.OpenRouterApiKey, ct))
                        yield return t;
                    break;
                case AiProvider.Ollama:
                    await foreach (var t in OllamaChatAsync(settings.Providers.OllamaBaseUrl, settings.ActiveModelId, systemPrompt, history, ct))
                        yield return t;
                    break;
                case AiProvider.GitHubCopilot:
                    await foreach (var t in OpenAiChatAsync("https://api.githubcopilot.com/chat/completions", settings.ActiveModelId, systemPrompt, history, settings.Providers.GitHubCopilotToken, ct))
                        yield return t;
                    break;
            }
        }

        // ── Prompt builder ─────────────────────────────────────────────────────
        private static string BuildPrompt(CompletionRequest req)
        {
            var text = req.Recipe.UserPromptTemplate;
            text = text.Replace("{input}", req.InputText);
            foreach (var (k, v) in req.ParameterValues)
                text = text.Replace($"{{param:{k}}}", v);
            return text;
        }

        // ── Anthropic ──────────────────────────────────────────────────────────
        private static async IAsyncEnumerable<string> AnthropicStreamAsync(
            string model, string system, string user, string apiKey,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var body = new
            {
                model,
                max_tokens = 4096,
                stream = true,
                system,
                messages = new[] { new { role = "user", content = user } }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
                var data = line[6..];
                if (data == "[DONE]") break;

                JsonElement doc;
                try { doc = JsonDocument.Parse(data).RootElement; } catch { continue; }

                if (doc.TryGetProperty("type", out var t) && t.GetString() == "content_block_delta")
                    if (doc.TryGetProperty("delta", out var d) && d.TryGetProperty("text", out var txt))
                        yield return txt.GetString() ?? "";
            }
        }

        // ── OpenAI-compatible (OpenAI + OpenRouter) ────────────────────────────
        private static async IAsyncEnumerable<string> OpenAiStreamAsync(
            string endpoint, string model, string system, string user, string apiKey,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var body = new
            {
                model,
                stream = true,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user",   content = user }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
                var data = line[6..];
                if (data == "[DONE]") break;

                JsonElement doc;
                try { doc = JsonDocument.Parse(data).RootElement; } catch { continue; }

                if (doc.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var delta = choices[0].GetProperty("delta");
                    if (delta.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null)
                        yield return c.GetString() ?? "";
                }
            }
        }

        // ── Ollama ─────────────────────────────────────────────────────────────
        private static async IAsyncEnumerable<string> OllamaStreamAsync(
            string baseUrl, string model, string system, string user,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var body = new
            {
                model,
                stream = true,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user",   content = user }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/chat");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(line)) continue;

                JsonElement doc;
                try { doc = JsonDocument.Parse(line).RootElement; } catch { continue; }

                if (doc.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c))
                    yield return c.GetString() ?? "";

                if (doc.TryGetProperty("done", out var done) && done.GetBoolean()) break;
            }
        }

        // ── Anthropic chat ─────────────────────────────────────────────────────
        private static async IAsyncEnumerable<string> AnthropicChatAsync(
            string model, string system, IReadOnlyList<ChatMessage> history, string apiKey,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var body = new
            {
                model,
                max_tokens = 4096,
                stream = true,
                system,
                messages = history.Select(m => new { role = m.Role, content = m.Content }).ToArray()
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
                var data = line[6..];
                if (data == "[DONE]") break;
                JsonElement doc;
                try { doc = JsonDocument.Parse(data).RootElement; } catch { continue; }
                if (doc.TryGetProperty("type", out var t) && t.GetString() == "content_block_delta")
                    if (doc.TryGetProperty("delta", out var d) && d.TryGetProperty("text", out var txt))
                        yield return txt.GetString() ?? "";
            }
        }

        // ── OpenAI-compatible chat ─────────────────────────────────────────────
        private static async IAsyncEnumerable<string> OpenAiChatAsync(
            string endpoint, string model, string system, IReadOnlyList<ChatMessage> history, string apiKey,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var messages = new List<object> { new { role = "system", content = system } };
            messages.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Content }));

            var body = new { model, stream = true, messages };

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (endpoint.Contains("githubcopilot"))
            {
                req.Headers.Add("Copilot-Integration-Id", "vscode-chat");
                req.Headers.Add("Editor-Version", "vscode/1.90.0");
            }
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
                var data = line[6..];
                if (data == "[DONE]") break;
                JsonElement doc;
                try { doc = JsonDocument.Parse(data).RootElement; } catch { continue; }
                if (doc.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var delta = choices[0].GetProperty("delta");
                    if (delta.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null)
                        yield return c.GetString() ?? "";
                }
            }
        }

        // ── Ollama chat ────────────────────────────────────────────────────────
        private static async IAsyncEnumerable<string> OllamaChatAsync(
            string baseUrl, string model, string system, IReadOnlyList<ChatMessage> history,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var messages = new List<object> { new { role = "system", content = system } };
            messages.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Content }));

            var body = new { model, stream = true, messages };

            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/chat");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(line)) continue;
                JsonElement doc;
                try { doc = JsonDocument.Parse(line).RootElement; } catch { continue; }
                if (doc.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c))
                    yield return c.GetString() ?? "";
                if (doc.TryGetProperty("done", out var done) && done.GetBoolean()) break;
            }
        }

        // ── GitHub Copilot ─────────────────────────────────────────────────────
        // Copilot Chat API is OpenAI-compatible at https://api.githubcopilot.com
        private static async IAsyncEnumerable<string> GitHubCopilotStreamAsync(
            string model, string system, string user, string token,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var body = new
            {
                model,
                stream = true,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user",   content = user }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.githubcopilot.com/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Copilot-Integration-Id", "vscode-chat");
            req.Headers.Add("Editor-Version", "vscode/1.90.0");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
                var data = line[6..];
                if (data == "[DONE]") break;

                JsonElement doc;
                try { doc = JsonDocument.Parse(data).RootElement; } catch { continue; }

                if (doc.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var delta = choices[0].GetProperty("delta");
                    if (delta.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null)
                        yield return c.GetString() ?? "";
                }
            }
        }
    }
}

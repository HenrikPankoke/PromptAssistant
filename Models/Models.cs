using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PromptAssistant.Models
{
    // ── AI Provider ────────────────────────────────────────────────────────────
    public enum AiProvider
    {
        Anthropic,
        OpenAI,
        OpenRouter,
        Ollama,
        GitHubCopilot
    }

    public class ModelEntry
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public AiProvider Provider { get; set; }
    }

    // ── Recipe (prompt template) ───────────────────────────────────────────────
    public class RecipeParameter
    {
        public string Name { get; set; } = "";           // e.g. "Tone"
        public List<string> Options { get; set; } = new();
        public string DefaultValue { get; set; } = "";
    }

    public class Recipe
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "✦";          // emoji or symbol
        public string SystemPrompt { get; set; } = "";    // instructions to AI
        public string UserPromptTemplate { get; set; } = "{input}";  // {input} = selected text, {param:Name} = param value
        public List<RecipeParameter> Parameters { get; set; } = new();
        public bool IsBuiltIn { get; set; } = false;
        public int SortOrder { get; set; } = 0;

        [JsonIgnore]
        public string ToolTipText
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(Name);

                if (!string.IsNullOrWhiteSpace(SystemPrompt))
                    sb.Append("\n\nSystem prompt:\n").Append(SystemPrompt);

                sb.Append("\n\nTemplate:\n").Append(UserPromptTemplate);

                if (Parameters.Count > 0)
                {
                    sb.Append("\n\nParameters:");
                    foreach (var p in Parameters)
                        sb.Append("\n • ").Append(p.Name).Append(": ")
                          .Append(string.Join(", ", p.Options))
                          .Append(" (default: ").Append(p.DefaultValue).Append(')');
                }

                return sb.ToString();
            }
        }
    }

    // ── App Settings ───────────────────────────────────────────────────────────
    public class ProviderSettings
    {
        public string AnthropicApiKey { get; set; } = "";
        public string OpenAiApiKey { get; set; } = "";
        public string OpenRouterApiKey { get; set; } = "";
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
        // GitHub Copilot uses device-flow OAuth — token stored separately
        public string GitHubCopilotToken { get; set; } = "";
        public string GitHubCopilotTokenExpiry { get; set; } = "";
    }

    public class AppSettings
    {
        public string Theme { get; set; } = "Dark";          // "Dark" | "Light"
        public string HotkeyModifiers { get; set; } = "Ctrl+Alt";
        public string HotkeyKey { get; set; } = "P";
        public string ActiveProvider { get; set; } = "Anthropic";
        public string ActiveModelId { get; set; } = "claude-sonnet-4-6";
        public string LastUsedRecipeId { get; set; } = "";
        public string LastSettingsSection { get; set; } = "general";
        public Dictionary<string, Dictionary<string, string>> LastUsedParams { get; set; } = new();
        public string DefaultChatSystemPrompt { get; set; } = "You are a helpful assistant.";
        public List<ChatSystemPrompt> ChatSystemPrompts { get; set; } = new();
        public ProviderSettings Providers { get; set; } = new();
        public List<Recipe> Recipes { get; set; } = new();
    }

    // ── Chat ──────────────────────────────────────────────────────────────────
    public class ChatMessage
    {
        public string Role { get; set; } = "";      // "user" | "assistant"
        public string Content { get; set; } = "";
    }

    public class ChatSystemPrompt
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
    }

    // ── Runtime state ──────────────────────────────────────────────────────────
    public class CompletionRequest
    {
        public Recipe Recipe { get; set; } = new();
        public string InputText { get; set; } = "";
        public Dictionary<string, string> ParameterValues { get; set; } = new();
        public AiProvider Provider { get; set; }
        public string ModelId { get; set; } = "";
    }
}

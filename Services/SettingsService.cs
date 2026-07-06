using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PromptAssistant.Models;

namespace PromptAssistant.Services
{
    public static class SettingsService
    {
        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptAssistant");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                    if (s != null)
                    {
                        s.Providers.AnthropicApiKey       = EncryptionService.Unprotect(s.Providers.AnthropicApiKey);
                        s.Providers.OpenAiApiKey           = EncryptionService.Unprotect(s.Providers.OpenAiApiKey);
                        s.Providers.OpenRouterApiKey       = EncryptionService.Unprotect(s.Providers.OpenRouterApiKey);
                        s.Providers.GitHubCopilotToken     = EncryptionService.Unprotect(s.Providers.GitHubCopilotToken);
                        return s;
                    }
                }
            }
            catch { /* fall through to defaults */ }

            return CreateDefaults();
        }

        public static void Save(AppSettings settings)
        {
            // Serialize a copy with encrypted credentials — never write plain text keys to disk
            var encrypted = new ProviderSettings
            {
                AnthropicApiKey           = EncryptionService.Protect(settings.Providers.AnthropicApiKey),
                OpenAiApiKey               = EncryptionService.Protect(settings.Providers.OpenAiApiKey),
                OpenRouterApiKey           = EncryptionService.Protect(settings.Providers.OpenRouterApiKey),
                OllamaBaseUrl              = settings.Providers.OllamaBaseUrl,
                GitHubCopilotToken         = EncryptionService.Protect(settings.Providers.GitHubCopilotToken),
                GitHubCopilotTokenExpiry   = settings.Providers.GitHubCopilotTokenExpiry,
            };
            var toWrite = new AppSettings
            {
                Theme                    = settings.Theme,
                HotkeyModifiers          = settings.HotkeyModifiers,
                HotkeyKey                = settings.HotkeyKey,
                ActiveProvider           = settings.ActiveProvider,
                ActiveModelId            = settings.ActiveModelId,
                LastUsedRecipeId         = settings.LastUsedRecipeId,
                LastSettingsSection      = settings.LastSettingsSection,
                LastUsedParams           = settings.LastUsedParams,
                DefaultChatSystemPrompt  = settings.DefaultChatSystemPrompt,
                ChatSystemPrompts        = settings.ChatSystemPrompts,
                Providers                = encrypted,
                Recipes                  = settings.Recipes,
            };
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(toWrite, JsonOpts));
        }

        public static string ConfigFilePath => ConfigPath;

        private static AppSettings CreateDefaults() => new()
        {
            Theme = "Dark",
            HotkeyModifiers = "Ctrl+Alt",
            HotkeyKey = "P",
            ActiveProvider = "Anthropic",
            ActiveModelId = "claude-sonnet-4-6",
            Providers = new ProviderSettings
            {
                OllamaBaseUrl = "http://localhost:11434"
            },
            Recipes = DefaultRecipes()
        };

        private static List<Recipe> DefaultRecipes() =>
        [
            new Recipe
            {
                Id = "builtin-rephrase",
                Name = "Rephrase",
                Icon = "✏️",
                IsBuiltIn = true,
                SortOrder = 0,
                SystemPrompt = "You are a writing assistant. Rephrase the given text according to the specified tone and length. Return only the rephrased text, no explanations.",
                UserPromptTemplate = "Rephrase the following text.\nTone: {param:Tone}\nLength: {param:Length}\n\nText:\n{input}",
                Parameters =
                [
                    new RecipeParameter { Name = "Tone", Options = ["Neutral","Professional","Casual","Formal","Friendly"], DefaultValue = "Neutral" },
                    new RecipeParameter { Name = "Length", Options = ["Same","Shorter","Longer"], DefaultValue = "Same" }
                ]
            },
            new Recipe
            {
                Id = "builtin-summarize",
                Name = "Summarize",
                Icon = "📋",
                IsBuiltIn = true,
                SortOrder = 1,
                SystemPrompt = "You are a summarization assistant. Summarize the given text concisely. Return only the summary.",
                UserPromptTemplate = "Summarize the following text.\nLength: {param:Length}\nStyle: {param:Style}\n\nText:\n{input}",
                Parameters =
                [
                    new RecipeParameter { Name = "Length", Options = ["One sentence","Short paragraph","Bullet points"], DefaultValue = "Short paragraph" },
                    new RecipeParameter { Name = "Style", Options = ["Neutral","Executive","Technical"], DefaultValue = "Neutral" }
                ]
            },
            new Recipe
            {
                Id = "builtin-reply",
                Name = "Reply",
                Icon = "↩️",
                IsBuiltIn = true,
                SortOrder = 2,
                SystemPrompt = "You are an email/message assistant. Write a reply to the given message. Return only the reply text.",
                UserPromptTemplate = "Write a reply to the following message.\nTone: {param:Tone}\n\nMessage:\n{input}",
                Parameters =
                [
                    new RecipeParameter { Name = "Tone", Options = ["Professional","Friendly","Brief","Apologetic","Firm"], DefaultValue = "Professional" }
                ]
            },
            new Recipe
            {
                Id = "builtin-email",
                Name = "Write Email",
                Icon = "📧",
                IsBuiltIn = true,
                SortOrder = 3,
                SystemPrompt = "You are an email writing assistant. Write a complete professional email based on the given notes or topic. Return only the email text including subject line.",
                UserPromptTemplate = "Write an email based on the following notes or topic.\nTone: {param:Tone}\nLength: {param:Length}\n\nNotes:\n{input}",
                Parameters =
                [
                    new RecipeParameter { Name = "Tone", Options = ["Professional","Friendly","Formal","Urgent"], DefaultValue = "Professional" },
                    new RecipeParameter { Name = "Length", Options = ["Short","Medium","Detailed"], DefaultValue = "Medium" }
                ]
            },
            new Recipe
            {
                Id = "builtin-translate",
                Name = "Translate",
                Icon = "🌍",
                IsBuiltIn = true,
                SortOrder = 4,
                SystemPrompt = "You are a translation assistant. Translate the given text accurately. Return only the translated text.",
                UserPromptTemplate = "Translate the following text to {param:Language}.\n\nText:\n{input}",
                Parameters =
                [
                    new RecipeParameter { Name = "Language", Options = ["English","Danish","German","French","Spanish","Japanese","Chinese"], DefaultValue = "English" }
                ]
            },
            new Recipe
            {
                Id = "builtin-improve",
                Name = "Improve Writing",
                Icon = "✨",
                IsBuiltIn = true,
                SortOrder = 5,
                SystemPrompt = "You are a writing coach. Improve the given text for clarity, grammar, and style. Return only the improved text.",
                UserPromptTemplate = "Improve the following text.\nFocus: {param:Focus}\n\nText:\n{input}",
                Parameters =
                [
                    new RecipeParameter { Name = "Focus", Options = ["Grammar & spelling","Clarity","Style & flow","All"], DefaultValue = "All" }
                ]
            },
            new Recipe
            {
                Id = "builtin-explain",
                Name = "Explain",
                Icon = "💡",
                IsBuiltIn = true,
                SortOrder = 6,
                SystemPrompt = "You are an explainer assistant. Explain the given text or concept clearly.",
                UserPromptTemplate = "Explain the following in {param:Level} terms.\n\nText:\n{input}",
                Parameters =
                [
                    new RecipeParameter { Name = "Level", Options = ["simple","technical","ELI5","expert"], DefaultValue = "simple" }
                ]
            },
            new Recipe
            {
                Id = "builtin-code-review",
                Name = "Code Review",
                Icon = "🔍",
                IsBuiltIn = true,
                SortOrder = 7,
                SystemPrompt = "You are a senior software engineer doing a code review. Analyze the code and provide clear, actionable feedback.",
                UserPromptTemplate = "Review the following code.\nFocus: {param:Focus}\n\nCode:\n{input}",
                Parameters =
                [
                    new RecipeParameter { Name = "Focus", Options = ["Bugs","Performance","Security","Style","All"], DefaultValue = "All" }
                ]
            }
        ];
    }
}
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "PromptAssistant";

    public static bool IsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppName) != null;
    }

    public static void Set(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (enable)
        {
            var path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(path))
                key.SetValue(AppName, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}
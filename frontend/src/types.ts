export type Provider =
  | 'Anthropic'
  | 'OpenAI'
  | 'OpenRouter'
  | 'Ollama'
  | 'GitHubCopilot'

export interface ModelEntry {
  id: string
  displayName: string
  provider: Provider
}

export interface RecipeParameter {
  name: string
  options: string[]
  defaultValue: string
}

export interface Recipe {
  id: string
  name: string
  icon: string
  systemPrompt: string
  userPromptTemplate: string
  parameters: RecipeParameter[]
  isBuiltIn: boolean
  sortOrder: number
}

export interface ProviderSettings {
  anthropicApiKey: string
  openAiApiKey: string
  openRouterApiKey: string
  ollamaBaseUrl: string
  gitHubCopilotToken: string
  gitHubCopilotTokenExpiry: string
}

export interface ChatSystemPrompt {
  id: string
  name: string
  content: string
}

export interface AppSettings {
  theme: string
  hotkeyModifiers: string
  hotkeyKey: string
  activeProvider: string
  activeModelId: string
  lastUsedRecipeId: string
  lastSettingsSection: string
  lastUsedParams: Record<string, Record<string, string>>
  defaultChatSystemPrompt: string
  chatSystemPrompts: ChatSystemPrompt[]
  providers: ProviderSettings
  recipes: Recipe[]
}

export interface ChatMessage {
  role: 'user' | 'assistant'
  content: string
}

export interface Stats {
  today: number
  week: number
  total: number
  daily: Record<string, number>
}

export interface AppState {
  settings: AppSettings
  models: ModelEntry[]
  stats: Stats
  configPath: string
  startupEnabled: boolean
  copilotLoggedIn: boolean
  hasSource: boolean
}

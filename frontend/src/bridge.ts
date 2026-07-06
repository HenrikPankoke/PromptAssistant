import type { AppState, ModelEntry, Recipe } from './types'

// ── Message shapes ─────────────────────────────────────────────────────────
type PushEvent =
  | { type: 'push'; event: 'selection'; payload: { text: string } }
  | { type: 'push'; event: 'theme'; payload: { theme: string } }
  | { type: 'push'; event: 'navigate'; payload: { view: string } }
  | { type: 'push'; event: 'shown'; payload: {} }
  | { type: 'push'; event: 'status'; payload: { text: string } }
  | { type: 'push'; event: 'copilot'; payload: { text: string; loggedIn: boolean } }

type Incoming =
  | { id: number; ok: boolean; result?: unknown; error?: string }
  | { type: 'stream'; id: number; chunk: string }
  | { type: 'streamEnd'; id: number }
  | { type: 'streamError'; id: number; error: string }
  | PushEvent

interface WebViewHost {
  postMessage(msg: unknown): void
  addEventListener(type: 'message', handler: (e: { data: Incoming }) => void): void
}

const host: WebViewHost | undefined = (window as any).chrome?.webview

let nextId = 1
const pending = new Map<number, { resolve: (v: any) => void; reject: (e: any) => void }>()
const streams = new Map<number, { onChunk: (c: string) => void; resolve: () => void; reject: (e: any) => void }>()
const pushHandlers = new Map<string, ((payload: any) => void)[]>()

function handle(data: Incoming) {
  if ('type' in data && data.type === 'push') {
    const hs = pushHandlers.get(data.event) ?? []
    hs.forEach((h) => h((data as any).payload))
    return
  }
  if ('type' in data && data.type === 'stream') {
    streams.get(data.id)?.onChunk(data.chunk)
    return
  }
  if ('type' in data && data.type === 'streamEnd') {
    const s = streams.get(data.id)
    if (s) { streams.delete(data.id); s.resolve() }
    return
  }
  if ('type' in data && data.type === 'streamError') {
    const s = streams.get(data.id)
    if (s) { streams.delete(data.id); s.reject(new Error(data.error)) }
    return
  }
  if ('id' in data) {
    const p = pending.get(data.id)
    if (p) {
      pending.delete(data.id)
      if (data.ok) p.resolve(data.result)
      else p.reject(new Error(data.error ?? 'error'))
    }
  }
}

if (host) {
  host.addEventListener('message', (e) => handle(e.data))
}

// ── Public API ─────────────────────────────────────────────────────────────
export function on(event: string, handler: (payload: any) => void) {
  const hs = pushHandlers.get(event) ?? []
  hs.push(handler)
  pushHandlers.set(event, hs)
  return () => {
    const list = pushHandlers.get(event) ?? []
    pushHandlers.set(event, list.filter((h) => h !== handler))
  }
}

export function call<T = any>(type: string, payload?: unknown): Promise<T> {
  if (!host) return mockCall<T>(type, payload)
  const id = nextId++
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve, reject })
    host.postMessage({ id, type, payload })
  })
}

export interface StreamHandle {
  done: Promise<void>
  cancel: () => void
}

export function stream(type: string, payload: unknown, onChunk: (chunk: string) => void): StreamHandle {
  if (!host) return mockStream(type, payload, onChunk)
  const id = nextId++
  const done = new Promise<void>((resolve, reject) => {
    streams.set(id, { onChunk, resolve, reject })
    host.postMessage({ id, type, payload })
  })
  return {
    done,
    cancel: () => host.postMessage({ type: 'cancel', payload: { id } }),
  }
}

export const isHosted = !!host

// ── Browser mock (npm run dev / preview panel) ──────────────────────────────
const mockRecipes: Recipe[] = [
  { id: 'builtin-rephrase', name: 'Rephrase', icon: '✏️', isBuiltIn: true, sortOrder: 0,
    systemPrompt: 'You are a writing assistant.', userPromptTemplate: 'Rephrase:\n{input}',
    parameters: [{ name: 'Tone', options: ['Neutral', 'Professional', 'Casual', 'Formal', 'Friendly'], defaultValue: 'Neutral' },
                 { name: 'Length', options: ['Same', 'Shorter', 'Longer'], defaultValue: 'Same' }] },
  { id: 'builtin-summarize', name: 'Summarize', icon: '📋', isBuiltIn: true, sortOrder: 1,
    systemPrompt: 'You summarize.', userPromptTemplate: 'Summarize:\n{input}', parameters: [] },
  { id: 'builtin-reply', name: 'Reply', icon: '↩️', isBuiltIn: true, sortOrder: 2,
    systemPrompt: 'You reply.', userPromptTemplate: 'Reply:\n{input}', parameters: [] },
  { id: 'builtin-email', name: 'Write Email', icon: '📧', isBuiltIn: true, sortOrder: 3,
    systemPrompt: 'You write emails.', userPromptTemplate: 'Email:\n{input}', parameters: [] },
  { id: 'builtin-translate', name: 'Translate', icon: '🌍', isBuiltIn: true, sortOrder: 4,
    systemPrompt: 'You translate.', userPromptTemplate: 'Translate:\n{input}',
    parameters: [{ name: 'Language', options: ['English', 'Danish', 'German', 'French'], defaultValue: 'English' }] },
  { id: 'builtin-improve', name: 'Improve Writing', icon: '✨', isBuiltIn: true, sortOrder: 5,
    systemPrompt: 'You improve writing.', userPromptTemplate: 'Improve:\n{input}', parameters: [] },
  { id: 'builtin-explain', name: 'Explain', icon: '💡', isBuiltIn: true, sortOrder: 6,
    systemPrompt: 'You explain.', userPromptTemplate: 'Explain:\n{input}', parameters: [] },
  { id: 'builtin-code-review', name: 'Code Review', icon: '🔍', isBuiltIn: true, sortOrder: 7,
    systemPrompt: 'You review code.', userPromptTemplate: 'Review:\n{input}', parameters: [] },
]

const mockModels: ModelEntry[] = [
  { provider: 'Anthropic', id: 'claude-sonnet-4-6', displayName: 'Claude Sonnet 4.6' },
  { provider: 'Anthropic', id: 'claude-opus-4-8', displayName: 'Claude Opus 4.8' },
]

// Respect ?theme= in the mock too, so dev preview and screenshot tooling can
// choose the theme the same way the real host does.
const mockTheme = new URLSearchParams(window.location.search).get('theme') === 'Dark' ? 'Dark' : 'Light'

const mockState: AppState = {
  settings: {
    theme: mockTheme, hotkeyModifiers: 'Ctrl+Alt', hotkeyKey: 'P',
    activeProvider: 'Anthropic', activeModelId: 'claude-sonnet-4-6',
    lastUsedRecipeId: 'builtin-rephrase', lastSettingsSection: 'general', lastUsedParams: {},
    defaultChatSystemPrompt: 'You are a helpful assistant.',
    chatSystemPrompts: [{ id: 'p1', name: 'Coding buddy', content: 'You are a senior engineer.' }],
    providers: { anthropicApiKey: 'sk-demo', openAiApiKey: '', openRouterApiKey: '',
      ollamaBaseUrl: 'http://localhost:11434', gitHubCopilotToken: '', gitHubCopilotTokenExpiry: '' },
    recipes: mockRecipes,
  },
  models: mockModels,
  stats: { today: 3, week: 21, total: 148, daily: mockDaily() },
  configPath: 'C:\\Users\\demo\\AppData\\Roaming\\PromptAssistant\\settings.json',
  startupEnabled: false,
  copilotLoggedIn: false,
  hasSource: true,
  version: '1.0.0.0',
}

function mockDaily(): Record<string, number> {
  const d: Record<string, number> = {}
  for (let i = 0; i < 7; i++) {
    const day = new Date(); day.setDate(day.getDate() - i)
    d[day.toISOString().slice(0, 10)] = Math.floor(Math.random() * 9)
  }
  return d
}

async function mockCall<T>(type: string, payload?: any): Promise<T> {
  await new Promise((r) => setTimeout(r, 120))
  switch (type) {
    case 'getState': return mockState as unknown as T
    case 'fetchModels': return mockModels as unknown as T
    default: return undefined as unknown as T
  }
}

// Realistic canned responses so the dev preview (and demo screenshots) show
// plausible output instead of lorem-ipsum-style filler.
function cannedPrompt(recipeId: string, input: string): string {
  switch (recipeId) {
    case 'builtin-rephrase':
      return (
        'Hi,\n\nI wanted to follow up on the figures I sent over earlier. Could you let me know once ' +
        "you've had a chance to review them? We'll need them finalized ahead of Thursday's meeting, " +
        'so a response at your earliest convenience would be much appreciated.\n\nThank you!'
      )
    case 'builtin-summarize':
      return (
        '• The Q3 report shows revenue up 12% quarter-over-quarter, driven mainly by the new subscription tier.\n' +
        '• Churn improved from 4.1% to 3.4% after the onboarding revamp shipped in July.\n' +
        '• Main risk: infrastructure costs are growing faster than revenue and need review before Q4 planning.'
      )
    case 'builtin-translate':
      return 'Hej! Tak for mødet i dag — jeg sender referatet og de næste skridt senere i eftermiddag.'
    case 'builtin-explain':
      return (
        'Think of an API as a restaurant menu. You (the app) don\'t need to know how the kitchen works — ' +
        'you just order from a fixed list of dishes (the endpoints), and the kitchen (the server) returns ' +
        'what you asked for in a predictable format. That contract is what makes different systems able to ' +
        'work together without knowing each other\'s internals.'
      )
    default:
      return (
        'Here is a polished version of your text with improved clarity and flow. The key points are kept ' +
        'intact while tightening the wording and fixing small grammatical issues:\n\n' +
        (input.length > 120 ? input.slice(0, 120) + '…' : input)
      )
  }
}

function cannedChat(lastUserMessage: string): string {
  const m = lastUserMessage.toLowerCase()
  if (m.includes('subject line')) {
    return (
      'Here are three subject line ideas for the announcement:\n\n' +
      '1. "Meet Your New AI Sidekick — Now Built Into Your Workflow"\n' +
      '2. "Write Less, Say More: Our New AI Features Are Here"\n' +
      '3. "From Draft to Done in Seconds — Introducing AI Assist"\n\n' +
      'Option 2 tends to perform best for feature announcements: it leads with the benefit rather than the technology.'
    )
  }
  if (m.includes('shorter') || m.includes('punchier')) {
    return (
      'Sure — tightened up:\n\n' +
      '1. "Your New AI Sidekick Is Here"\n' +
      '2. "Write Less. Say More."\n' +
      '3. "Draft to Done in Seconds"\n\n' +
      'All three are under 35 characters, so they won\'t get cut off on mobile inboxes.'
    )
  }
  return (
    'Happy to help with that. Could you share a bit more context — for example the audience you\'re ' +
    'writing for and the tone you want? In the meantime, here\'s a quick first pass you can react to.'
  )
}

function mockStream(type: string, payload: any, onChunk: (c: string) => void): StreamHandle {
  let cancelled = false
  const text =
    type === 'sendChat'
      ? cannedChat(payload?.history?.slice(-1)?.[0]?.content ?? '')
      : cannedPrompt(payload?.recipeId ?? '', payload?.input ?? '')
  const words = text.split(/(?<=\s)/) // keep whitespace/newlines intact
  const done = new Promise<void>((resolve) => {
    let i = 0
    const tick = () => {
      if (cancelled || i >= words.length) return resolve()
      onChunk(words[i++])
      setTimeout(tick, 18)
    }
    tick()
  })
  return { done, cancel: () => { cancelled = true } }
}

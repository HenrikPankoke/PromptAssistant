import { useEffect, useState } from 'react'
import type { AppSettings, ModelEntry, Recipe } from '../types'
import { useStore } from '../store'
import { call, on } from '../bridge'
import { setCloseBlocked } from '../closeGuard'
import { Button, IconButton, Label, Select, TextArea, TextInput, Toggle } from './ui'
import { RecipeEditorModal } from './RecipeEditorModal'

type Section = 'provider' | 'general' | 'prompts' | 'chat' | 'stats' | 'about'

const SECTIONS: { key: Section; label: string }[] = [
  { key: 'general', label: 'General' },
  { key: 'prompts', label: 'Prompts' },
  { key: 'chat', label: 'Chat' },
  { key: 'provider', label: 'Provider & Model' },
  { key: 'stats', label: 'Stats' },
  { key: 'about', label: 'About' },
]

const PROVIDERS = ['Anthropic', 'OpenAI', 'OpenRouter', 'Ollama', 'GitHubCopilot']
const GITHUB_URL = 'https://github.com/HenrikPankoke/PromptAssistant'

// Map a KeyboardEvent.code to a WPF System.Windows.Input.Key name the backend can parse.
// Returns null for modifier-only or unsupported keys (recorder keeps waiting).
function mapKeyCode(code: string): string | null {
  if (/^Key[A-Z]$/.test(code)) return code.slice(3) // KeyP -> P
  if (/^Digit[0-9]$/.test(code)) return 'D' + code.slice(5) // Digit1 -> D1
  if (/^F([1-9]|1[0-9]|2[0-4])$/.test(code)) return code // F1..F24
  if (/^Numpad[0-9]$/.test(code)) return 'NumPad' + code.slice(6) // Numpad1 -> NumPad1
  return null
}

// Turn a stored WPF key name back into a human label (D1 -> 1, NumPad1 -> Num1).
function keyLabel(key: string): string {
  if (/^D[0-9]$/.test(key)) return key.slice(1)
  if (/^NumPad[0-9]$/.test(key)) return 'Num' + key.slice(6)
  return key
}

function formatHotkey(modifiers: string, key: string): string {
  const parts = modifiers ? modifiers.split('+').filter(Boolean) : []
  if (key) parts.push(keyLabel(key))
  return parts.join(' + ')
}

function HotkeyRecorder() {
  const { state, save } = useStore()
  const s = state.settings
  const [recording, setRecording] = useState(false)

  useEffect(() => {
    if (!recording) return
    setCloseBlocked(true)
    call('suspendHotkey')

    const onKey = (e: KeyboardEvent) => {
      e.preventDefault()
      e.stopPropagation()
      if (e.key === 'Escape') {
        setRecording(false)
        call('resumeHotkey')
        return
      }
      const key = mapKeyCode(e.code)
      if (!key) return // modifier or unsupported — keep waiting

      const mods: string[] = []
      if (e.ctrlKey) mods.push('Ctrl')
      if (e.altKey) mods.push('Alt')
      if (e.shiftKey) mods.push('Shift')
      if (e.metaKey) mods.push('Win')
      if (mods.length === 0) return // require at least one modifier

      save({ ...s, hotkeyModifiers: mods.join('+'), hotkeyKey: key })
      setRecording(false)
    }

    // Safety: if the window loses focus/hides while recording, exit recording so it
    // never reopens stuck in detect mode.
    const onBlur = () => {
      setRecording(false)
      call('resumeHotkey')
    }

    window.addEventListener('keydown', onKey, true)
    window.addEventListener('blur', onBlur)
    return () => {
      window.removeEventListener('keydown', onKey, true)
      window.removeEventListener('blur', onBlur)
      setCloseBlocked(false)
    }
  }, [recording, s, save])

  return (
    <button
      onClick={() => setRecording((r) => !r)}
      title={
        'Click, then press your combination.\n' +
        'At least one modifier (Ctrl, Alt, Shift, or Win) plus one key:\n' +
        ' • Letter A–Z\n' +
        ' • Number 0–9\n' +
        ' • Function key F1–F24\n' +
        ' • Numpad digit 0–9\n' +
        'Example: Ctrl + Shift + Alt + P · Esc cancels'
      }
      className={`no-drag flex w-full items-center justify-between rounded-lg border px-3 py-2.5 text-left transition-all ${
        recording ? 'border-accent ring-2 ring-accent-soft bg-accent-soft' : 'border-border bg-elevated hover:border-border-strong'
      }`}
    >
      {recording ? (
        <span className="text-[13px] text-accent-text">Press a key combination…</span>
      ) : (
        <span className="font-mono text-[13px] text-text">{formatHotkey(s.hotkeyModifiers, s.hotkeyKey) || 'Not set'}</span>
      )}
      <span className="text-[11px] text-text-faint">{recording ? 'Esc to cancel' : 'Click to change'}</span>
    </button>
  )
}

export function SettingsView() {
  const { state, save } = useStore()
  // Restore the last-used section (persisted in settings, survives restarts).
  const stored = state.settings.lastSettingsSection as Section
  const [section, setSection] = useState<Section>(
    SECTIONS.some((s) => s.key === stored) ? stored : 'general',
  )

  function selectSection(key: Section) {
    setSection(key)
    save({ ...state.settings, lastSettingsSection: key })
  }

  return (
    <div className="flex h-full">
      <div className="w-52 shrink-0 border-r border-border bg-surface p-2">
        {SECTIONS.map((s) => (
          <button
            key={s.key}
            onClick={() => selectSection(s.key)}
            className={`mb-0.5 w-full rounded-lg px-3 py-2 text-left text-[13px] transition-colors ${
              section === s.key ? 'bg-selected text-text' : 'text-text-muted hover:bg-hover hover:text-text'
            }`}
          >
            {s.label}
          </button>
        ))}
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        {section === 'provider' && <ProviderSection />}
        {section === 'general' && <GeneralSection />}
        {section === 'prompts' && <PromptsSection />}
        {section === 'chat' && <ChatSection />}
        {section === 'stats' && <StatsSection />}
        {section === 'about' && <AboutSection />}
      </div>
    </div>
  )
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <h2 className="mb-4 text-base font-semibold">{children}</h2>
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="mb-4 space-y-1.5">
      <Label>{label}</Label>
      {children}
    </div>
  )
}

// ── Provider & Model ────────────────────────────────────────────────────────
function ProviderSection() {
  const { state, setLocal, save, reload } = useStore()
  const s = state.settings
  const [loadingModels, setLoadingModels] = useState(false)
  const [copilotStatus, setCopilotStatus] = useState('')

  useEffect(() => on('copilot', ({ text, loggedIn }: { text: string; loggedIn: boolean }) => {
    setCopilotStatus(text)
    if (loggedIn) reload()
  }), [reload])

  async function loadModels(provider: string) {
    setLoadingModels(true)
    try {
      const models = await call<ModelEntry[]>('fetchModels', { provider })
      const validId = models.find((m) => m.id === s.activeModelId)?.id ?? models[0]?.id ?? ''
      setLocal({ ...state, models })
      await save({ ...s, activeProvider: provider, activeModelId: validId })
    } finally {
      setLoadingModels(false)
    }
  }

  function patchProvider(patch: Partial<typeof s.providers>) {
    setLocal({ ...state, settings: { ...s, providers: { ...s.providers, ...patch } } })
  }

  return (
    <div className="max-w-xl">
      <SectionTitle>Provider &amp; Model</SectionTitle>

      <Field label="Provider">
        <Select
          value={s.activeProvider}
          onChange={(e) => loadModels(e.target.value)}
          options={PROVIDERS.map((p) => ({ value: p, label: p }))}
        />
      </Field>

      <Field label="Model">
        <div className="flex gap-2">
          <Select
            value={s.activeModelId}
            onChange={(e) => save({ ...s, activeModelId: e.target.value })}
            options={
              state.models.length
                ? state.models.map((m) => ({ value: m.id, label: m.displayName }))
                : [{ value: s.activeModelId, label: s.activeModelId || '(no models)' }]
            }
            className="flex-1"
          />
          <Button onClick={() => loadModels(s.activeProvider)} disabled={loadingModels}>
            {loadingModels ? '…' : '↻ Refresh'}
          </Button>
        </div>
      </Field>

      <div className="my-5 h-px bg-border" />

      {s.activeProvider === 'Anthropic' && (
        <Field label="Anthropic API Key">
          <TextInput type="password" value={s.providers.anthropicApiKey}
            onChange={(e) => patchProvider({ anthropicApiKey: e.target.value })}
            onBlur={() => save()} placeholder="sk-ant-…" />
        </Field>
      )}
      {s.activeProvider === 'OpenAI' && (
        <Field label="OpenAI API Key">
          <TextInput type="password" value={s.providers.openAiApiKey}
            onChange={(e) => patchProvider({ openAiApiKey: e.target.value })}
            onBlur={() => save()} placeholder="sk-…" />
        </Field>
      )}
      {s.activeProvider === 'OpenRouter' && (
        <Field label="OpenRouter API Key">
          <TextInput type="password" value={s.providers.openRouterApiKey}
            onChange={(e) => patchProvider({ openRouterApiKey: e.target.value })}
            onBlur={() => save()} placeholder="sk-or-…" />
        </Field>
      )}
      {s.activeProvider === 'Ollama' && (
        <Field label="Ollama Base URL">
          <TextInput value={s.providers.ollamaBaseUrl}
            onChange={(e) => patchProvider({ ollamaBaseUrl: e.target.value })}
            onBlur={() => save()} placeholder="http://localhost:11434" />
        </Field>
      )}
      {s.activeProvider === 'GitHubCopilot' && (
        <div className="space-y-3">
          <div className="rounded-lg border border-border bg-elevated p-3 text-[13px]">
            {state.copilotLoggedIn ? (
              <span className="text-text">✓ Logged in to GitHub Copilot</span>
            ) : (
              <span className="text-text-muted">
                {copilotStatus || 'Not logged in. Authenticate via GitHub device flow.'}
              </span>
            )}
          </div>
          {state.copilotLoggedIn ? (
            <Button onClick={async () => { await call('copilotLogout'); reload() }}>Logout</Button>
          ) : (
            <Button variant="primary" onClick={() => call('copilotLogin')}>Login with GitHub</Button>
          )}
        </div>
      )}
    </div>
  )
}

// ── General ─────────────────────────────────────────────────────────────────
function GeneralSection() {
  const { state, setLocal, save } = useStore()
  const s = state.settings
  const [startup, setStartup] = useState(state.startupEnabled)

  return (
    <div className="max-w-xl">
      <SectionTitle>General</SectionTitle>

      <Field label="Theme">
        <Select value={s.theme} onChange={(e) => save({ ...s, theme: e.target.value })}
          options={[{ value: 'Light', label: 'Light' }, { value: 'Dark', label: 'Dark' }]} />
      </Field>

      <Field label="Global Hotkey">
        <HotkeyRecorder />
      </Field>

      <div className="mb-4 flex items-center justify-between rounded-lg border border-border bg-elevated px-3 py-2.5">
        <div>
          <div className="text-[13px]">Launch at startup</div>
          <div className="text-xs text-text-muted">Start Prompt Assistant when you sign in</div>
        </div>
        <Toggle checked={startup} onChange={async (v) => { setStartup(v); await call('setStartup', { enabled: v }) }} />
      </div>

      <Field label="Config File">
        <div className="flex items-center gap-2">
          <div className="flex-1 truncate rounded-lg border border-border bg-elevated px-3 py-2 text-xs text-text-muted">
            {state.configPath}
          </div>
          <Button onClick={() => call('openConfigFile')}>Open</Button>
        </div>
      </Field>
    </div>
  )
}

// ── Prompts manager ─────────────────────────────────────────────────────────
function PromptsSection() {
  const { state } = useStore()
  const [editing, setEditing] = useState<Recipe | null>(null)
  const [creating, setCreating] = useState(false)
  const recipes = [...state.settings.recipes].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))

  return (
    <div className="max-w-xl">
      <div className="mb-4 flex items-center justify-between">
        <SectionTitle>Prompts</SectionTitle>
        <Button variant="primary" size="sm" onClick={() => setCreating(true)}>＋ New Prompt</Button>
      </div>

      <div className="space-y-1.5">
        {recipes.map((r) => (
          <div key={r.id} className="flex items-center gap-3 rounded-lg border border-border bg-elevated px-3 py-2.5">
            <span className="text-lg">{r.icon}</span>
            <div className="min-w-0 flex-1">
              <div className="truncate text-[13px]">{r.name}</div>
              <div className="truncate text-xs text-text-muted">
                {r.parameters.length ? `${r.parameters.length} parameter(s)` : 'No parameters'}
                {r.isBuiltIn ? ' · built-in' : ''}
              </div>
            </div>
            <Button size="sm" onClick={() => setEditing(r)}>Edit</Button>
          </div>
        ))}
      </div>

      {(editing || creating) && (
        <RecipeEditorModal
          recipe={editing}
          onClose={() => { setEditing(null); setCreating(false) }}
          onSaved={() => { setEditing(null); setCreating(false) }}
        />
      )}
    </div>
  )
}

// ── Chat settings ───────────────────────────────────────────────────────────
function ChatSection() {
  const { state, setLocal, save } = useStore()
  const s = state.settings

  function patchPreset(id: string, patch: { name?: string; content?: string }) {
    setLocal({
      ...state,
      settings: { ...s, chatSystemPrompts: s.chatSystemPrompts.map((p) => (p.id === id ? { ...p, ...patch } : p)) },
    })
  }

  function addPreset() {
    save({
      ...s,
      chatSystemPrompts: [...s.chatSystemPrompts, { id: crypto.randomUUID(), name: 'New Preset', content: 'You are a helpful assistant.' }],
    })
  }

  function removePreset(id: string) {
    save({ ...s, chatSystemPrompts: s.chatSystemPrompts.filter((p) => p.id !== id) })
  }

  return (
    <div className="max-w-xl">
      <SectionTitle>Chat</SectionTitle>

      <Field label="Default System Prompt">
        <TextArea value={s.defaultChatSystemPrompt} rows={3}
          onChange={(e) => setLocal({ ...state, settings: { ...s, defaultChatSystemPrompt: e.target.value } })}
          onBlur={() => save()} />
      </Field>

      <div className="mb-2 flex items-center justify-between">
        <Label>System Prompt Presets</Label>
        <Button size="sm" onClick={addPreset}>＋ Add Preset</Button>
      </div>
      <div className="space-y-2">
        {s.chatSystemPrompts.map((p) => (
          <div key={p.id} className="flex gap-2 rounded-lg border border-border bg-elevated p-3">
            <div className="flex-1 space-y-2">
              <TextInput value={p.name} onChange={(e) => patchPreset(p.id, { name: e.target.value })} onBlur={() => save()} placeholder="Preset name" />
              <TextArea value={p.content} rows={2} onChange={(e) => patchPreset(p.id, { content: e.target.value })} onBlur={() => save()} placeholder="System prompt content" />
            </div>
            <IconButton onClick={() => removePreset(p.id)}>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M18 6L6 18M6 6l12 12" strokeLinecap="round" />
              </svg>
            </IconButton>
          </div>
        ))}
        {s.chatSystemPrompts.length === 0 && (
          <div className="rounded-lg border border-dashed border-border px-3 py-4 text-center text-xs text-text-faint">
            No presets yet. The Default prompt is always available in Chat.
          </div>
        )}
      </div>
    </div>
  )
}

// ── Stats ───────────────────────────────────────────────────────────────────
function StatsSection() {
  const { state } = useStore()
  const { stats } = state

  const days: { label: string; count: number }[] = []
  for (let i = 6; i >= 0; i--) {
    const d = new Date()
    d.setDate(d.getDate() - i)
    const key = d.toISOString().slice(0, 10)
    days.push({ label: d.toLocaleDateString(undefined, { weekday: 'short' }), count: stats.daily[key] ?? 0 })
  }
  const max = Math.max(1, ...days.map((d) => d.count))

  return (
    <div className="max-w-xl">
      <SectionTitle>Usage Stats</SectionTitle>
      <div className="mb-6 grid grid-cols-3 gap-3">
        {[{ label: 'Today', v: stats.today }, { label: 'This week', v: stats.week }, { label: 'All time', v: stats.total }].map((c) => (
          <div key={c.label} className="rounded-xl border border-border bg-elevated p-4">
            <div className="text-2xl font-semibold">{c.v}</div>
            <div className="text-xs text-text-muted">{c.label}</div>
          </div>
        ))}
      </div>
      <Label>Last 7 days</Label>
      <div className="mt-3 space-y-2.5">
        {days.map((d, i) => (
          <div key={i} className="flex items-center gap-3">
            <span className="w-9 text-xs text-text-muted">{d.label}</span>
            <div className="h-2 flex-1 overflow-hidden rounded-full bg-elevated">
              <div className="h-full rounded-full bg-accent transition-all" style={{ width: `${(d.count / max) * 100}%` }} />
            </div>
            <span className={`w-6 text-right text-xs ${d.count ? 'text-text' : 'text-text-faint'}`}>{d.count}</span>
          </div>
        ))}
      </div>
    </div>
  )
}

// ── About ───────────────────────────────────────────────────────────────────
function AboutSection() {
  const { state } = useStore()
  return (
    <div className="max-w-xl">
      <SectionTitle>About</SectionTitle>

      <div className="flex items-center gap-3">
        <div className="grid h-11 w-11 place-items-center rounded-xl bg-accent text-lg font-bold text-white">P</div>
        <div>
          <div className="text-base font-semibold">Prompt Assistant</div>
          <div className="font-mono text-xs text-text-muted">Version {state.version}</div>
        </div>
      </div>

      <p className="mt-4 text-[13px] leading-relaxed text-text-muted">
        Your AI text sidekick, one hotkey away — in any Windows app. Local-first, provider-agnostic,
        and open source.
      </p>

      <div className="mt-5 space-y-3">
        <button
          onClick={() => call('openExternal', { url: GITHUB_URL })}
          className="no-drag inline-flex items-center gap-2 rounded-lg border border-border bg-elevated px-3.5 py-2 text-[13px] text-text transition-colors hover:border-border-strong hover:bg-hover"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
            <path d="M12 2C6.48 2 2 6.58 2 12.25c0 4.53 2.87 8.37 6.84 9.73.5.09.68-.22.68-.49 0-.24-.01-.87-.01-1.71-2.78.62-3.37-1.37-3.37-1.37-.45-1.18-1.11-1.49-1.11-1.49-.91-.64.07-.62.07-.62 1 .07 1.53 1.06 1.53 1.06.89 1.56 2.34 1.11 2.91.85.09-.66.35-1.11.63-1.36-2.22-.26-4.56-1.14-4.56-5.07 0-1.12.39-2.03 1.03-2.75-.1-.26-.45-1.3.1-2.71 0 0 .84-.28 2.75 1.05a9.36 9.36 0 0 1 2.5-.34c.85 0 1.71.12 2.5.34 1.91-1.33 2.75-1.05 2.75-1.05.55 1.41.2 2.45.1 2.71.64.72 1.03 1.63 1.03 2.75 0 3.94-2.34 4.81-4.57 5.06.36.32.68.94.68 1.9 0 1.37-.01 2.47-.01 2.81 0 .27.18.59.69.49A10.02 10.02 0 0 0 22 12.25C22 6.58 17.52 2 12 2z" />
          </svg>
          View on GitHub
        </button>

        <div className="text-xs text-text-faint">
          © 2026 Henrik Pankoke · Released under the MIT License
        </div>
      </div>
    </div>
  )
}

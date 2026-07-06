import { useEffect, useState, useCallback } from 'react'
import type { AppSettings, AppState } from './types'
import { fetchState, persistSettings, StoreContext } from './store'
import { on } from './bridge'
import { requestClose } from './closeGuard'
import { TitleBar } from './components/TitleBar'
import { PromptsView } from './components/PromptsView'
import { ChatView } from './components/ChatView'
import { SettingsView } from './components/SettingsView'

export default function App() {
  const [state, setState] = useState<AppState | null>(null)
  const [view, setView] = useState<'main' | 'settings'>('main')
  const [tab, setTab] = useState<'prompts' | 'chat'>('prompts')
  const [promptInput, setPromptInput] = useState('')
  const [focusTick, setFocusTick] = useState(0)
  const [tokenInfo, setTokenInfo] = useState('')

  // Initial load
  useEffect(() => {
    fetchState().then(setState)
  }, [])

  // Apply theme
  useEffect(() => {
    const theme = state?.settings.theme ?? 'Light'
    document.documentElement.classList.toggle('dark', theme === 'Dark')
  }, [state?.settings.theme])

  // Push events from host
  useEffect(() => {
    const offs = [
      on('selection', ({ text }: { text: string }) => {
        setPromptInput(text)
        setTab('prompts')
        setView('main')
        setFocusTick((t) => t + 1)
      }),
      on('shown', () => setFocusTick((t) => t + 1)),
      on('theme', ({ theme }: { theme: string }) => {
        setState((s) => (s ? { ...s, settings: { ...s.settings, theme } } : s))
      }),
      on('navigate', ({ view: v }: { view: string }) => {
        if (v === 'settings') setView('settings')
      }),
    ]
    return () => offs.forEach((o) => o())
  }, [])

  // Esc closes the window (WebView2 captures keys, so handle here)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') requestClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const setLocal = useCallback((next: AppState) => setState(next), [])

  const save = useCallback(
    async (settings?: AppSettings) => {
      const current = settings ?? state?.settings
      if (!current) return
      setState((s) => (s ? { ...s, settings: current } : s))
      await persistSettings(current)
    },
    [state?.settings],
  )

  const reload = useCallback(async () => {
    setState(await fetchState())
  }, [])

  if (!state) {
    return (
      <div className="grid h-full place-items-center text-sm text-text-faint">Loading…</div>
    )
  }

  return (
    <StoreContext.Provider value={{ state, setLocal, save, reload }}>
      <div className="flex h-full flex-col bg-bg">
        <TitleBar
          view={view}
          tab={tab}
          onTab={setTab}
          onSettings={() => setView('settings')}
          onBack={() => setView('main')}
        />

        <div className="min-h-0 flex-1">
          {view === 'settings' ? (
            <SettingsView />
          ) : tab === 'prompts' ? (
            <PromptsView
              input={promptInput}
              setInput={setPromptInput}
              focusTick={focusTick}
              onTokenInfo={setTokenInfo}
              goChatWith={(sys, history) => {
                setTab('chat')
                ;(window as any).__seedChat = { sys, history }
                setFocusTick((t) => t + 1)
              }}
            />
          ) : (
            <ChatView focusTick={focusTick} />
          )}
        </div>

        {/* Footer */}
        <div className="flex h-8 shrink-0 items-center justify-between border-t border-border bg-surface px-4 text-[10px] text-text-faint">
          <span>Esc to close · Ctrl+Enter to run</span>
          <span className="flex items-center gap-2">
            {tokenInfo && <span>{tokenInfo}</span>}
            <span className="text-text-muted">
              {state.settings.activeProvider} · {state.settings.activeModelId}
            </span>
          </span>
        </div>
      </div>
    </StoreContext.Provider>
  )
}

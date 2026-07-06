import { useEffect, useMemo, useRef, useState } from 'react'
import type { ChatMessage, Recipe } from '../types'
import { useStore } from '../store'
import { call, stream, type StreamHandle } from '../bridge'
import { Button, IconButton, Select } from './ui'
import { RecipeEditorModal } from './RecipeEditorModal'

interface Props {
  input: string
  setInput: (v: string) => void
  focusTick: number
  resetTick: number
  onTokenInfo: (info: string) => void
  goChatWith: (sys: string, history: ChatMessage[]) => void
}

function recipeTooltip(r: Recipe): string {
  let t = r.name
  if (r.systemPrompt) t += `\n\nSystem prompt:\n${r.systemPrompt}`
  t += `\n\nTemplate:\n${r.userPromptTemplate}`
  if (r.parameters.length) {
    t += '\n\nParameters:'
    for (const p of r.parameters) t += `\n • ${p.name}: ${p.options.join(', ')} (default: ${p.defaultValue})`
  }
  return t
}

export function PromptsView({ input, setInput, focusTick, resetTick, onTokenInfo, goChatWith }: Props) {
  const { state, save } = useStore()
  const recipes = useMemo(
    () => [...state.settings.recipes].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name)),
    [state.settings.recipes],
  )

  const [search, setSearch] = useState('')
  const [selectedId, setSelectedId] = useState(
    state.settings.lastUsedRecipeId || recipes[0]?.id || '',
  )
  const [params, setParams] = useState<Record<string, string>>({})
  const [output, setOutput] = useState('')
  const [generating, setGenerating] = useState(false)
  const [status, setStatus] = useState('')
  const [editorOpen, setEditorOpen] = useState(false)

  const inputRef = useRef<HTMLTextAreaElement>(null)
  const outputRef = useRef<HTMLDivElement>(null)
  const handleRef = useRef<StreamHandle | null>(null)

  const selected = recipes.find((r) => r.id === selectedId) ?? recipes[0]

  const filtered = search.trim()
    ? recipes.filter((r) => r.name.toLowerCase().includes(search.toLowerCase()))
    : recipes

  // Initialize params when selection changes
  useEffect(() => {
    if (!selected) return
    const saved = state.settings.lastUsedParams[selected.id]
    const next: Record<string, string> = {}
    for (const p of selected.parameters) {
      const v = saved?.[p.name]
      next[p.name] = v && p.options.includes(v) ? v : p.defaultValue
    }
    setParams(next)
  }, [selectedId, selected, state.settings.lastUsedParams])

  // Focus input on show / selection
  useEffect(() => {
    inputRef.current?.focus()
  }, [focusTick])

  // A new selection replaced the input — clear the previous output/status too.
  useEffect(() => {
    if (resetTick === 0) return // skip initial mount
    setOutput('')
    setStatus('')
  }, [resetTick])

  // Report token estimate
  useEffect(() => {
    onTokenInfo(output ? `~${Math.ceil(output.length / 4)} tokens` : '')
  }, [output, onTokenInfo])

  async function run() {
    if (generating || !selected) return
    const text = input.trim()
    if (!text) {
      setStatus('Enter some text in the input area first.')
      return
    }
    setGenerating(true)
    setOutput('')
    setStatus('Generating…')

    // Persist last-used recipe + params
    const nextSettings = {
      ...state.settings,
      lastUsedRecipeId: selected.id,
      lastUsedParams: { ...state.settings.lastUsedParams, [selected.id]: params },
    }
    save(nextSettings)

    let acc = ''
    try {
      const h = stream('runPrompt', { recipeId: selected.id, input: text, params }, (c) => {
        acc += c
      })
      handleRef.current = h
      await h.done
      setOutput(acc)
      setStatus('Done')
      requestAnimationFrame(() => outputRef.current?.scrollTo({ top: 0 }))
    } catch (e: any) {
      if (acc) {
        setOutput(acc)
        setStatus('Cancelled')
      } else {
        setOutput('')
        setStatus(`Error: ${e?.message ?? e}`)
      }
      requestAnimationFrame(() => outputRef.current?.scrollTo({ top: 0 }))
    } finally {
      setGenerating(false)
      handleRef.current = null
    }
  }

  function onKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
      run()
    }
  }

  return (
    <div className="flex h-full">
      {/* Sidebar */}
      <div className="flex w-56 shrink-0 flex-col border-r border-border bg-surface">
        <div className="p-2">
          <div className="relative">
            <svg
              className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-text-faint"
              width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
            >
              <circle cx="11" cy="11" r="7" />
              <path d="M21 21l-4.3-4.3" strokeLinecap="round" />
            </svg>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search prompts…"
              className="no-drag w-full rounded-lg border border-border bg-elevated py-1.5 pl-8 pr-2 text-xs text-text outline-none placeholder:text-text-faint focus:border-accent focus:ring-2 focus:ring-accent-soft"
            />
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-2">
          {filtered.map((r) => (
            <button
              key={r.id}
              title={recipeTooltip(r)}
              onClick={() => setSelectedId(r.id)}
              className={`mb-0.5 flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-left transition-colors ${
                r.id === selected?.id ? 'bg-selected' : 'hover:bg-hover'
              }`}
            >
              <span className="text-[15px] leading-none">{r.icon}</span>
              <span className="truncate text-[13px]">{r.name}</span>
            </button>
          ))}
        </div>

        <div className="p-2">
          <Button className="w-full" onClick={() => setEditorOpen(true)}>
            <span className="text-sm">＋</span> New Prompt
          </Button>
        </div>
      </div>

      {/* Work area */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Parameters */}
        {selected && selected.parameters.length > 0 && (
          <div className="flex flex-wrap items-center gap-x-5 gap-y-2 border-b border-border bg-elevated/50 px-4 py-2.5">
            {selected.parameters.map((p) => (
              <div key={p.name} className="flex items-center gap-2">
                <span className="text-[11px] text-text-muted">{p.name}</span>
                <Select
                  value={params[p.name] ?? p.defaultValue}
                  onChange={(e) => setParams((prev) => ({ ...prev, [p.name]: e.target.value }))}
                  options={p.options.map((o) => ({ value: o, label: o }))}
                  className="min-w-[110px] py-1"
                />
              </div>
            ))}
          </div>
        )}

        {/* Input */}
        <div className="flex min-h-0 flex-1 flex-col">
          <div className="flex items-center justify-between px-4 pb-1 pt-3">
            <span className="text-[10px] font-semibold uppercase tracking-wide text-text-faint">Input</span>
            <div className="flex gap-1">
              <Button
                size="sm"
                variant="subtle"
                onClick={async () => {
                  const t = await call<string>('readClipboard')
                  if (t) {
                    setInput(t)
                    inputRef.current?.focus()
                  }
                }}
              >
                Paste
              </Button>
              <Button size="sm" variant="subtle" onClick={() => { setInput(''); inputRef.current?.focus() }}>
                Clear
              </Button>
            </div>
          </div>
          <textarea
            ref={inputRef}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={onKeyDown}
            placeholder="Type or paste text here…"
            className="no-drag min-h-0 flex-1 resize-none bg-transparent px-4 pb-3 text-[13px] leading-relaxed text-text outline-none placeholder:text-text-faint"
          />
        </div>

        <div className="h-px bg-border" />

        {/* Output */}
        <div className="flex min-h-0 flex-1 flex-col">
          <div className="flex items-center justify-between px-4 pb-1 pt-3">
            <span className="text-[10px] font-semibold uppercase tracking-wide text-text-faint">Output</span>
            {output && (
              <Button size="sm" variant="subtle" onClick={() => { call('copyToClipboard', { text: output }); call('close') }}>
                Copy
              </Button>
            )}
          </div>
          <div ref={outputRef} className="min-h-0 flex-1 overflow-y-auto px-4 pb-3">
            {generating ? (
              <div className="flex items-center gap-2 pt-1 text-text-muted">
                <span className="flex gap-1">
                  <span className="h-1.5 w-1.5 rounded-full bg-accent animate-blink" />
                  <span className="h-1.5 w-1.5 rounded-full bg-accent animate-blink [animation-delay:0.2s]" />
                  <span className="h-1.5 w-1.5 rounded-full bg-accent animate-blink [animation-delay:0.4s]" />
                </span>
                <span className="text-xs">Generating…</span>
              </div>
            ) : (
              <div className="selectable prose-chat text-[13px] text-text">{output}</div>
            )}
          </div>
        </div>

        {/* Action bar */}
        <div className="flex items-center justify-between border-t border-border bg-surface px-4 py-2.5">
          <span className="text-[11px] text-text-muted">{status}</span>
          <div className="flex gap-2">
            {generating && (
              <Button onClick={() => handleRef.current?.cancel()}>Cancel</Button>
            )}
            {output && !generating && (
              <Button
                onClick={() =>
                  goChatWith(selected?.systemPrompt || state.settings.defaultChatSystemPrompt, [
                    { role: 'user', content: input.trim() },
                    { role: 'assistant', content: output },
                  ])
                }
              >
                Chat ↗
              </Button>
            )}
            <Button variant="primary" onClick={run} disabled={generating}>
              Run ▶
            </Button>
          </div>
        </div>
      </div>

      {editorOpen && (
        <RecipeEditorModal
          recipe={null}
          onClose={() => setEditorOpen(false)}
          onSaved={(r) => {
            setEditorOpen(false)
            setSelectedId(r.id)
          }}
        />
      )}
    </div>
  )
}

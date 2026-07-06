import { useEffect, useRef, useState } from 'react'
import type { ChatMessage } from '../types'
import { useStore } from '../store'
import { call, stream, type StreamHandle } from '../bridge'
import { Button, Select } from './ui'

interface Props {
  focusTick: number
}

const DEFAULT_KEY = '__default__'

export function ChatView({ focusTick }: Props) {
  const { state } = useStore()
  const presets = state.settings.chatSystemPrompts

  const [systemPrompt, setSystemPrompt] = useState(state.settings.defaultChatSystemPrompt)
  const [presetKey, setPresetKey] = useState(DEFAULT_KEY)
  const [history, setHistory] = useState<ChatMessage[]>([])
  const [input, setInput] = useState('')
  const [generating, setGenerating] = useState(false)
  const [pendingSystem, setPendingSystem] = useState<string | null>(null)
  const [pendingKey, setPendingKey] = useState<string | null>(null)

  const inputRef = useRef<HTMLTextAreaElement>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const lastReplyRef = useRef<HTMLDivElement>(null)
  const handleRef = useRef<StreamHandle | null>(null)

  // Seed from "Chat ↗" on prompts view
  useEffect(() => {
    const seed = (window as any).__seedChat as { sys: string; history: ChatMessage[] } | undefined
    if (seed) {
      setSystemPrompt(seed.sys)
      setPresetKey(DEFAULT_KEY)
      setHistory(seed.history)
      ;(window as any).__seedChat = undefined
    }
    inputRef.current?.focus()
  }, [focusTick])

  function optionsForPresets() {
    return [
      { value: DEFAULT_KEY, label: 'Default' },
      ...presets.map((p) => ({ value: p.id, label: p.name })),
    ]
  }

  function contentForKey(key: string): string {
    if (key === DEFAULT_KEY) return state.settings.defaultChatSystemPrompt
    return presets.find((p) => p.id === key)?.content ?? state.settings.defaultChatSystemPrompt
  }

  function onPresetChange(key: string) {
    const content = contentForKey(key)
    if (content === systemPrompt) return
    if (history.length > 0) {
      setPendingSystem(content)
      setPendingKey(key)
    } else {
      setSystemPrompt(content)
      setPresetKey(key)
    }
  }

  function confirmRestart() {
    setSystemPrompt(pendingSystem ?? state.settings.defaultChatSystemPrompt)
    setPresetKey(pendingKey ?? DEFAULT_KEY)
    setPendingSystem(null)
    setPendingKey(null)
    clearChat()
  }

  function cancelRestart() {
    setPendingSystem(null)
    setPendingKey(null)
  }

  function clearChat() {
    handleRef.current?.cancel()
    setHistory([])
    setInput('')
  }

  async function send() {
    const text = input.trim()
    if (!text || generating) return
    setInput('')
    const base = [...history, { role: 'user', content: text } as ChatMessage]
    setHistory(base)
    setGenerating(true)

    let acc = ''
    try {
      const h = stream('sendChat', { system: systemPrompt, history: base }, (c) => {
        acc += c
      })
      handleRef.current = h
      await h.done
      setHistory([...base, { role: 'assistant', content: acc }])
      scrollReplyToTop()
    } catch (e: any) {
      if (acc) setHistory([...base, { role: 'assistant', content: acc }])
      else setHistory([...base, { role: 'assistant', content: `⚠ ${e?.message ?? e}` }])
      scrollReplyToTop()
    } finally {
      setGenerating(false)
      handleRef.current = null
    }
  }

  function scrollReplyToTop() {
    requestAnimationFrame(() => {
      const el = lastReplyRef.current
      const container = scrollRef.current
      if (!el || !container) return
      // Land the reply's top just below the previous message, not flush to the edge.
      const top = el.offsetTop - 14
      container.scrollTo({ top: Math.max(0, top) })
    })
  }

  function exportMd() {
    if (history.length === 0) return
    let md = '# Chat Export\n\n'
    md += `**Date:** ${new Date().toISOString().slice(0, 16).replace('T', ' ')}  \n`
    md += `**Model:** ${state.settings.activeProvider} · ${state.settings.activeModelId}  \n`
    if (systemPrompt) md += `**System prompt:** ${systemPrompt}  \n`
    md += '\n---'
    for (const m of history) {
      md += `\n\n${m.role === 'user' ? '**You**' : '**Assistant**'}\n\n${m.content}\n\n---`
    }
    call('exportChat', { markdown: md })
  }

  function onKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      send()
    }
  }

  return (
    <div className="flex h-full flex-col">
      {/* System prompt bar */}
      <div className="flex items-center justify-between gap-2 border-b border-border bg-surface px-4 py-2">
        <div className="flex items-center gap-2">
          <span className="text-[11px] text-text-muted">System prompt</span>
          <Select
            value={presetKey}
            onChange={(e) => onPresetChange(e.target.value)}
            options={optionsForPresets()}
            className="min-w-[160px] py-1"
          />
        </div>
        <div className="flex gap-2">
          <Button size="sm" onClick={exportMd} disabled={history.length === 0}>
            Export MD
          </Button>
          <Button size="sm" onClick={clearChat}>
            New Chat
          </Button>
        </div>
      </div>

      {/* Restart banner */}
      {pendingSystem !== null && (
        <div className="flex items-center justify-between gap-2 border-b border-border bg-accent-soft px-4 py-2">
          <span className="text-[11px] text-accent-text">
            Changing the system prompt will clear the current conversation.
          </span>
          <div className="flex gap-2">
            <Button size="sm" onClick={confirmRestart}>Clear &amp; restart</Button>
            <Button size="sm" onClick={cancelRestart}>Cancel</Button>
          </div>
        </div>
      )}

      {/* Messages */}
      <div ref={scrollRef} className="min-h-0 flex-1 overflow-y-auto px-4 py-3">
        {history.length === 0 && !generating && (
          <div className="grid h-full place-items-center text-center">
            <div className="text-text-faint">
              <div className="mb-1 text-sm">Start a conversation</div>
              <div className="text-xs">Enter to send · Shift+Enter for a new line</div>
            </div>
          </div>
        )}
        {history.map((m, i) => {
          const isUser = m.role === 'user'
          const isLastAssistant = !isUser && i === history.length - 1
          return (
            <div
              key={i}
              ref={isLastAssistant ? lastReplyRef : undefined}
              className={`mb-3 flex ${isUser ? 'justify-end' : 'justify-start'}`}
            >
              <div
                className={`selectable prose-chat max-w-[78%] rounded-2xl border px-3.5 py-2.5 text-[13px] ${
                  isUser
                    ? 'rounded-br-md border-accent-soft bg-accent-soft text-text'
                    : 'rounded-bl-md border-border bg-elevated text-text'
                }`}
              >
                {m.content}
              </div>
            </div>
          )
        })}
        {generating && (
          <div className="mb-3 flex justify-start">
            <div className="flex items-center gap-2 rounded-2xl rounded-bl-md border border-border bg-elevated px-3.5 py-3">
              <span className="h-1.5 w-1.5 rounded-full bg-accent animate-blink" />
              <span className="h-1.5 w-1.5 rounded-full bg-accent animate-blink [animation-delay:0.2s]" />
              <span className="h-1.5 w-1.5 rounded-full bg-accent animate-blink [animation-delay:0.4s]" />
            </div>
          </div>
        )}
      </div>

      {/* Input bar */}
      <div className="flex items-end gap-2 border-t border-border bg-surface px-4 py-3">
        <textarea
          ref={inputRef}
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={onKeyDown}
          rows={1}
          placeholder="Message…"
          className="no-drag max-h-28 min-h-[38px] flex-1 resize-none rounded-xl border border-border bg-elevated px-3.5 py-2 text-[13px] text-text outline-none placeholder:text-text-faint focus:border-accent focus:ring-2 focus:ring-accent-soft"
        />
        {generating ? (
          <Button onClick={() => handleRef.current?.cancel()}>Stop</Button>
        ) : (
          <Button variant="primary" onClick={send} disabled={!input.trim()}>
            Send ▶
          </Button>
        )}
      </div>
    </div>
  )
}

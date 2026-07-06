import { call } from '../bridge'
import { requestClose } from '../closeGuard'
import { IconButton } from './ui'

interface Props {
  view: 'main' | 'settings'
  tab: 'prompts' | 'chat'
  onTab: (t: 'prompts' | 'chat') => void
  onSettings: () => void
  onBack: () => void
}

// No native caption exists (WindowStyle=None), so dragging is driven from here: a
// mousedown anywhere in the bar hands off to the host's DragMove(), unless it started
// on an interactive child (marked .no-drag) — same behavior CSS app-region used to
// give us, but version-independent (that WebView2 feature was Windows-11-only and
// rendered inconsistently on Windows 10).
function onTitleBarMouseDown(e: React.MouseEvent) {
  if (e.button !== 0) return
  if ((e.target as HTMLElement).closest('.no-drag')) return
  call('startDrag')
}

export function TitleBar({ view, tab, onTab, onSettings, onBack }: Props) {
  return (
    <div
      className="drag flex h-11 shrink-0 items-center gap-3 border-b border-border bg-surface px-3"
      onMouseDown={onTitleBarMouseDown}
    >
      {/* Logo */}
      <div className="flex items-center gap-2 pl-1">
        <div className="grid h-[18px] w-[18px] place-items-center rounded-md bg-accent text-[11px] font-bold text-white">
          P
        </div>
        <span className="text-[13px] font-semibold">Prompt Assistant</span>
      </div>

      {/* Center tabs (only on main) */}
      <div className="flex flex-1 justify-center">
        {view === 'main' && (
          <div className="no-drag flex rounded-lg border border-border bg-elevated p-0.5">
            {(['prompts', 'chat'] as const).map((t) => (
              <button
                key={t}
                onClick={() => onTab(t)}
                className={`rounded-md px-4 py-1 text-xs font-medium capitalize transition-colors ${
                  tab === t ? 'bg-accent-soft text-accent-text' : 'text-text-muted hover:text-text'
                }`}
              >
                {t}
              </button>
            ))}
          </div>
        )}
        {view === 'settings' && <span className="text-[13px] font-medium text-text-muted">Settings</span>}
      </div>

      {/* Right controls */}
      <div className="flex items-center gap-1">
        {view === 'main' ? (
          <IconButton title="Settings" onClick={onSettings}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
              <circle cx="12" cy="12" r="3" />
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
            </svg>
          </IconButton>
        ) : (
          <IconButton title="Back" onClick={onBack}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M15 18l-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </IconButton>
        )}
        <IconButton title="Close (Esc)" onClick={() => requestClose()} className="hover:bg-danger-soft hover:text-danger">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M18 6L6 18M6 6l12 12" strokeLinecap="round" />
          </svg>
        </IconButton>
      </div>
    </div>
  )
}

import React from 'react'

type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'ghost' | 'subtle' | 'danger'
  size?: 'sm' | 'md'
}

export function Button({ variant = 'ghost', size = 'md', className = '', ...rest }: ButtonProps) {
  const base =
    'no-drag inline-flex items-center justify-center gap-1.5 rounded-lg font-medium transition-colors ' +
    'disabled:opacity-40 disabled:cursor-default select-none'
  const sizes = size === 'sm' ? 'text-xs px-2.5 py-1' : 'text-[13px] px-3.5 py-1.5'
  const variants: Record<string, string> = {
    primary: 'bg-accent text-white hover:bg-accent-hover shadow-soft',
    ghost: 'text-text-muted hover:bg-hover hover:text-text border border-border',
    subtle: 'text-text-muted hover:bg-hover hover:text-text',
    danger: 'bg-danger text-white hover:opacity-90',
  }
  return <button className={`${base} ${sizes} ${variants[variant]} ${className}`} {...rest} />
}

export function IconButton({ className = '', ...rest }: React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      className={`no-drag grid place-items-center h-7 w-7 rounded-lg text-text-muted hover:bg-hover hover:text-text transition-colors ${className}`}
      {...rest}
    />
  )
}

export function TextInput({ className = '', ...rest }: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={`no-drag w-full rounded-lg border border-border bg-elevated px-3 py-2 text-[13px] text-text outline-none placeholder:text-text-faint focus:border-accent focus:ring-2 focus:ring-accent-soft transition-all ${className}`}
      {...rest}
    />
  )
}

export function TextArea({ className = '', ...rest }: React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      className={`no-drag w-full rounded-lg border border-border bg-elevated px-3 py-2 text-[13px] text-text outline-none placeholder:text-text-faint focus:border-accent focus:ring-2 focus:ring-accent-soft transition-all resize-none ${className}`}
      {...rest}
    />
  )
}

type SelectProps = React.SelectHTMLAttributes<HTMLSelectElement> & { options: { value: string; label: string }[] }

export function Select({ options, className = '', ...rest }: SelectProps) {
  return (
    <div className="relative no-drag">
      <select
        className={`appearance-none w-full rounded-lg border border-border bg-elevated pl-3 pr-8 py-1.5 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft transition-all cursor-pointer ${className}`}
        {...rest}
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      <svg
        className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-text-faint"
        width="10" height="10" viewBox="0 0 10 10" fill="none"
      >
        <path d="M1 3l4 4 4-4" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    </div>
  )
}

export function Label({ children }: { children: React.ReactNode }) {
  return <div className="text-[11px] font-semibold uppercase tracking-wide text-text-faint">{children}</div>
}

export function Toggle({ checked, onChange }: { checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      onClick={() => onChange(!checked)}
      className={`no-drag relative h-5 w-9 rounded-full transition-colors ${checked ? 'bg-accent' : 'bg-border-strong'}`}
    >
      <span
        className="absolute top-0.5 h-4 w-4 rounded-full bg-white shadow-soft transition-all"
        style={{ left: checked ? 'calc(100% - 18px)' : '2px' }}
      />
    </button>
  )
}

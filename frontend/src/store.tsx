import { createContext, useContext } from 'react'
import type { AppSettings, AppState } from './types'
import { call } from './bridge'

interface Store {
  state: AppState
  /** Replace local state without persisting (transient UI reflection). */
  setLocal: (next: AppState) => void
  /** Persist the given settings (defaults to current) to the host and update local state. */
  save: (settings?: AppSettings) => Promise<void>
  /** Re-fetch full state from the host. */
  reload: () => Promise<void>
}

export const StoreContext = createContext<Store | null>(null)

export function useStore(): Store {
  const s = useContext(StoreContext)
  if (!s) throw new Error('StoreContext missing')
  return s
}

export async function fetchState(): Promise<AppState> {
  return call<AppState>('getState')
}

export async function persistSettings(settings: AppSettings): Promise<void> {
  await call('saveSettings', settings)
}

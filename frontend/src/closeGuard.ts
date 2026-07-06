import { call } from './bridge'

// While the hotkey recorder is active, closing the window must be blocked so an
// Escape press (which cancels recording) or a stray click can't dismiss the popup.
let blocked = false

export function setCloseBlocked(value: boolean) {
  blocked = value
}

export function requestClose() {
  if (!blocked) call('close')
}

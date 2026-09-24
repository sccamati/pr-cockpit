import { onBeforeUnmount, onMounted } from 'vue'

/** One-key shortcuts for the open pull request, `?` for the help and Esc for peeling layers. */
export function useShortcuts({ shortcuts, enabled, onEscape, onHelp }: {
  shortcuts: Record<string, () => void>
  // Shortcuts and Esc act only while a pull request is open; `?` works everywhere.
  enabled: () => boolean
  onEscape: () => void
  onHelp: () => void
}) {
  // Capture phase: Monaco stops propagation of the keys it owns, so a bubble-phase
  // listener would never see them. Unmapped keys fall straight through to the editor,
  // which is why arrows, PageUp/Down, Home/End and F-keys are deliberately absent.
  function handleKey(event: KeyboardEvent) {
    if (event.ctrlKey || event.metaKey || event.altKey) return
    const target = event.target as HTMLElement | null
    // An open usages peek belongs to Monaco: Esc from inside the editor closes the peek, and
    // must not blur the editor or leave the file the way it otherwise would.
    if (event.key === 'Escape' && target?.closest?.('.monaco-editor') && document.querySelector('.peekview-widget')) return
    if (target?.closest?.('input, textarea, select, [contenteditable="true"]')) {
      // Esc has to work from inside the draft box — that is where it is reached for. Any
      // other field just gives the key back to the page.
      if (event.key !== 'Escape') return
      event.preventDefault()
      target.blur()
      if (target.closest('.comment-draft')) onEscape()
      return
    }
    if (event.key === '?') {
      event.preventDefault()
      onHelp()
      return
    }
    if (!enabled()) return
    if (event.key === 'Escape') {
      event.preventDefault()
      onEscape()
      return
    }
    const action = shortcuts[event.key]
    if (!action) return
    event.preventDefault()
    event.stopPropagation()
    action()
  }

  onMounted(() => window.addEventListener('keydown', handleKey, { capture: true }))
  onBeforeUnmount(() => window.removeEventListener('keydown', handleKey, { capture: true }))
}

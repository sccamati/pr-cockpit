import { computed, nextTick, ref, shallowRef, type Component } from 'vue'
import { api, type FileDiff, type UsageSource } from '@/api'
import type { PrScope } from '@/cockpit'
import { message, scrollBehavior } from '@/lib/format'

/** The open file: which one it is, its diff, and the editor showing it. */
export function useDiff({ details, projectId, repositoryId, defaultSince, onLeave, onOpened }: PrScope & {
  // The tree's "changes since update N" filter, followed when a caller does not say.
  defaultSince: () => number | null
  // The file on screen is about to change (comments drop what belonged to it).
  onLeave: () => void
  onOpened: (pullRequestId: number, path: string, request: number) => void
}) {
  const selectedFilePath = ref('')
  const fileDiff = ref<FileDiff | null>(null)
  const diffLoading = ref(false)
  const diffError = ref('')
  // Set while the diff on screen is a since-an-iteration comparison rather than the whole
  // change, so the toolbar can say so and offer the way back.
  const diffSinceIteration = ref<number | null>(null)
  const diffPanel = ref<HTMLElement | null>(null)
  const lastFilePath = ref('')
  const monacoComponent = shallowRef<Component | null>(null)
  const sideBySide = ref(false)
  // ponytail: hand-written structural type for the exposed diff instance. MonacoDiff is a
  // dynamic import, so InstanceType<typeof MonacoDiff> is not available here.
  const diffView = ref<{ goToDiff(target: 'next' | 'previous'): void; focusEditor(): void; cursorLine(): number | null; revealLine(line: number): void; selectedText(): string } | null>(null)
  let diffRequestId = 0

  // Usages for the file on screen, bound to this pull request and path now, so a late call
  // from an editor that is already gone still asks about the file it was made for.
  const usageSource = computed<UsageSource | undefined>(() => {
    const pullRequest = details.value
    const path = fileDiff.value?.path
    if (!pullRequest || !path) return undefined
    const project = projectId.value
    const repository = repositoryId.value
    return {
      load: signal => api.codeUsages(project, repository, pullRequest.id, path, signal),
      source: (target, signal) => api.codeSource(project, repository, pullRequest.id, target, signal),
    }
  })
  const filePosition = computed(() => {
    const index = details.value?.changedFiles.findIndex(file => file.path === selectedFilePath.value) ?? -1
    return index < 0 ? null : index + 1
  })

  function resetDiff() {
    ++diffRequestId
    onLeave()
    lastFilePath.value = ''
    selectedFilePath.value = ''
    fileDiff.value = null
    monacoComponent.value = null
    diffLoading.value = false
    diffError.value = ''
    diffSinceIteration.value = null
  }

  // sinceIteration: a number compares against that iteration, null asks for the whole diff
  // however the filter is set, and leaving it out follows the filter — so every existing
  // caller keeps working and the tree gets the narrowed diff for free.
  async function openFile(path: string, sinceIteration?: number | null) {
    if (!details.value) return
    const current = ++diffRequestId
    const since = sinceIteration === undefined ? defaultSince() : sinceIteration
    diffSinceIteration.value = since
    const pullRequestId = details.value.id
    onLeave()
    selectedFilePath.value = path
    onOpened(pullRequestId, path, current)
    fileDiff.value = null
    monacoComponent.value = null
    diffError.value = ''
    diffLoading.value = true
    await nextTick()
    if (current !== diffRequestId) return
    // The file can be picked from anywhere — the keyboard, "next file", the walkthrough — so
    // the tree follows the selection. 'nearest' means a click in the tree moves nothing.
    // ponytail: optional call — jsdom has no scrollIntoView, and a missing scroll is not
    // worth a stub in every test that opens a file.
    document.querySelector('.file-button.selected')?.scrollIntoView?.({ behavior: scrollBehavior(), block: 'nearest' })
    if (window.matchMedia('(max-width: 900px)').matches) {
      diffPanel.value?.scrollIntoView({ behavior: scrollBehavior(), block: 'start' })
    }
    try {
      const result = await api.fileDiff(projectId.value, repositoryId.value, pullRequestId, path, since ?? undefined)
      if (current !== diffRequestId) return
      if (result.kind === 'text') {
        const component = await import('./MonacoDiff.vue')
        if (current !== diffRequestId) return
        monacoComponent.value = component.default
      }
      fileDiff.value = result
    } catch (cause) {
      if (current === diffRequestId) diffError.value = message(cause)
    } finally {
      if (current === diffRequestId) diffLoading.value = false
    }
  }

  function goToDiff(target: 'next' | 'previous') {
    diffView.value?.goToDiff(target)
  }

  return {
    selectedFilePath, fileDiff, diffLoading, diffError, diffSinceIteration, diffPanel, lastFilePath, monacoComponent,
    sideBySide, diffView, usageSource, filePosition,
    openFile, resetDiff, goToDiff, diffRequest: () => diffRequestId,
  }
}

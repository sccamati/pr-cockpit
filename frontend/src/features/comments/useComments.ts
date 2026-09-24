import { computed, nextTick, ref, type ComputedRef, type Ref } from 'vue'
import { api, type FileDiff, type PrComment, type PrCommentThread } from '@/api'
import type { PrScope } from '@/cockpit'
import { fileName, message } from '@/lib/format'

// What is left after the deleted comments are dropped, so the templates never have to ask
// whether a comment still has content.
export type ReadableComment = PrComment & { content: string }
export type ReadableThread = Omit<PrCommentThread, 'comments'> & { comments: ReadableComment[] }

// "Resolved" is what Azure DevOps means by it: fixed, won't fix or closed. Anything else,
// including a thread with no status at all, is still waiting for somebody.
const resolvedStatuses = ['fixed', 'wontFix', 'closed']
export function isResolved(thread: PrCommentThread): boolean {
  return thread.status !== null && resolvedStatuses.includes(thread.status)
}

export function threadLocation(thread: PrCommentThread): string {
  if (!thread.filePath) return 'Cały PR'
  const line = thread.rightLine ?? thread.leftLine
  return line ? `${fileName(thread.filePath)}:${line}` : fileName(thread.filePath)
}

// Azure DevOps marks the comment, not the thread, so a thread is mine when I started it —
// its first comment. Answering inside somebody else's thread does not make closing it my job.
function startedByMe(thread: PrCommentThread): boolean {
  return thread.comments[0]?.isMine === true
}

// A deleted comment has no content, so on screen it was a row saying only that something
// used to be here — it broke up the conversation without adding to it. Filtering at the
// one place threads enter state means every counter, badge and editor marker follows,
// because they all derive from this list.
function withoutDeleted(list: PrCommentThread[]): ReadableThread[] {
  return list
    .map(thread => ({
      ...thread,
      comments: thread.comments.filter((comment): comment is ReadableComment => !!comment.content),
    }))
    // A thread whose every comment is gone has nothing left to read.
    .filter(thread => thread.comments.length > 0)
}

// Azure DevOps counts lines, not characters, and a repository can hold either ending.
const splitLines = (text: string) => text.split('\n').map(line => line.replace(/\r$/, ''))
const snippetContext = 3
// Every commented file gets its snippet — a review can touch sixty of them. A few at a
// time, because each diff costs the backend several Azure DevOps calls.
// ponytail: all files are fetched up front, not as they scroll into view; lazy loading
// when a pull request with hundreds of commented files shows up.
const snippetConcurrency = 4

/**
 * Pull request comment threads: the list and its filters, the snippets in the comments
 * view, the conversation anchored in the open file (docked or as Monaco zones), and every
 * write — none of them optimistic.
 */
export function useComments({
  details, projectId, repositoryId, selectedFilePath, fileDiff, orderedPaths, lastIteration, diffView, openFile,
}: PrScope & {
  selectedFilePath: Ref<string>
  fileDiff: Ref<FileDiff | null>
  orderedPaths: ComputedRef<string[]>
  lastIteration: ComputedRef<number>
  diffView: Ref<{ cursorLine?(): number | null; revealLine?(line: number): void } | null>
  openFile: (path: string, sinceIteration?: number | null) => Promise<void>
}) {
  const threads = ref<ReadableThread[]>([])
  const threadsLoading = ref(false)
  const threadsError = ref('')
  const commentsOpen = ref(false)
  const threadSearch = ref('')
  const threadFilter = ref<'all' | 'active' | 'mine'>('all')
  // Two steps, always. Enter never sends: a comment is visible to the whole team and cannot
  // be taken back, so the draft is written first and confirmed second.
  const draft = ref<{ target: string; text: string } | null>(null)
  // Editing reuses the send path: one place that sends, one place that reloads the threads.
  const editing = ref<{ threadId: number; commentId: number; text: string } | null>(null)
  // Deleting is two steps of its own. It is the one comment action that destroys something,
  // and Azure DevOps keeps the tombstone forever.
  const deleting = ref<number | null>(null)
  const commentSaving = ref(false)
  const commentError = ref('')
  // AzureDevOps:AllowComments is off by default and enforced in the backend. Asking for it
  // once turns "503 after the comment is written" into absent write actions.
  // It defaults to true and stays true when the probe fails: the backend is the real gate,
  // this only saves the typing.
  const commentsEnabled = ref(true)
  let threadsRequestId = 0

  // "Somebody pushed a fix after this comment" — the honest version of that question is a
  // comparison of iterations, which is exactly what Azure DevOps' own "update N" view does.
  function movedSinceComment(thread: PrCommentThread): boolean {
    return thread.iterationId !== null && lastIteration.value > thread.iterationId
  }

  const activeThreadCount = computed(() => threads.value.filter(thread => !isResolved(thread)).length)
  const threadsByFile = computed(() => {
    const map = new Map<string, { total: number; unresolved: number }>()
    for (const thread of threads.value) {
      if (!thread.filePath) continue
      const entry = map.get(thread.filePath) ?? { total: 0, unresolved: 0 }
      entry.total++
      if (!isResolved(thread)) entry.unresolved++
      map.set(thread.filePath, entry)
    }
    return map
  })
  // Every thread anchored in the open file, newest anchor last, so the bar above the diff
  // shows what is waiting here without having to hunt for the markers.
  const threadsInFile = computed(() => threads.value
    .filter(thread => thread.filePath === selectedFilePath.value)
    .sort((a, b) => (a.rightLine ?? 0) - (b.rightLine ?? 0)))
  // What a second pass has to close: my own remarks that nobody resolved.
  const myOpenThreads = computed(() =>
    threads.value.filter(thread => !isResolved(thread) && startedByMe(thread)))
  // With the identity probe down every comment arrives as isMine: false, so the filter would be
  // an empty list with no explanation. Then it is not offered at all.
  const mineKnown = computed(() =>
    threads.value.some(thread => thread.comments.some(comment => comment.isMine)))
  const visibleThreads = computed(() => {
    const search = threadSearch.value.trim().toLocaleLowerCase()
    return threads.value.filter(thread => {
      if (threadFilter.value === 'active' && isResolved(thread)) return false
      if (threadFilter.value === 'mine' && (isResolved(thread) || !startedByMe(thread))) return false
      if (!search) return true
      return thread.filePath?.toLocaleLowerCase().includes(search) ||
        thread.comments.some(comment =>
          comment.content?.toLocaleLowerCase().includes(search) ||
          comment.author?.toLocaleLowerCase().includes(search))
    })
  })
  // Grouped by file, files in the order the tree shows them, threads by line — the same
  // order you read the pull request in, so a comment is where you expect it to be.
  const threadGroups = computed(() => {
    const order = new Map(orderedPaths.value.map((path, index) => [path, index]))
    // Closing a remark starts with the ones the author answered in code, so in "mine" mode
    // those files float to the top. Everywhere else the reading order is the only order.
    const movedFirst = threadFilter.value === 'mine'
    const groups = new Map<string, ReadableThread[]>()
    for (const thread of visibleThreads.value) {
      const key = thread.filePath ?? ''
      const group = groups.get(key)
      if (group) group.push(thread)
      else groups.set(key, [thread])
    }
    return [...groups.entries()]
      .map(([path, items]) => ({
        path,
        label: path ? path : 'Bez pliku — cały PR',
        threads: items.sort((a, b) => (a.rightLine ?? a.leftLine ?? 0) - (b.rightLine ?? b.leftLine ?? 0)),
        rank: path ? order.get(path) ?? Number.MAX_SAFE_INTEGER : Number.MAX_SAFE_INTEGER,
        moved: items.some(thread => movedSinceComment(thread)),
      }))
      .sort((a, b) =>
        (movedFirst ? Number(b.moved) - Number(a.moved) : 0) ||
        a.rank - b.rank || a.label.localeCompare(b.label))
  })

  // --- Snippets: the few lines each comment is actually about ---
  // One diff per commented file, fetched once and kept for as long as the pull request is
  // open — a snippet per thread would mean one request per thread on a file that often
  // holds several.
  const snippets = ref<Record<string, { original: string[]; modified: string[] }>>({})
  const snippetsLoading = ref(false)
  // Bumped by every load and by a PR switch. Only the newest run writes, so a refresh
  // mid-load cannot leave "Wczytywanie kodu…" stuck or drop the load it started.
  let snippetsRun = 0

  function threadSnippet(thread: PrCommentThread) {
    if (!thread.filePath) return null
    const file = snippets.value[thread.filePath]
    if (!file) return null
    // A comment on a deleted line only has a left-hand anchor, and that line exists in the
    // original text, not in the modified one.
    const onRight = (thread.rightLine ?? 0) > 0
    const line = onRight ? thread.rightLine! : thread.leftLine ?? 0
    const lines = onRight ? file.modified : file.original
    if (!line || lines.length === 0) return null
    const from = Math.max(1, line - snippetContext)
    const to = Math.min(lines.length, line + 1)
    return {
      side: onRight ? 'po zmianie' : 'przed zmianą',
      lines: lines.slice(from - 1, to).map((text, index) => ({
        number: from + index,
        text,
        anchor: from + index === line,
      })),
    }
  }

  async function loadSnippets() {
    if (!details.value) return
    const run = ++snippetsRun
    // The order the list reads in, so the top of the view fills first.
    const inOrder = [...threadGroups.value.map(group => group.path), ...threads.value.map(thread => thread.filePath)]
    const queue = [...new Set(inOrder.filter((path): path is string => !!path))]
      .filter(path => !snippets.value[path])
    snippetsLoading.value = queue.length > 0
    if (queue.length === 0) return

    const project = projectId.value
    const repository = repositoryId.value
    const id = details.value.id
    const worker = async () => {
      for (let path = queue.shift(); path && run === snippetsRun; path = queue.shift()) {
        const diff = await api.fileDiff(project, repository, id, path).catch(() => null)
        if (run !== snippetsRun) return
        // A binary or oversized file simply has no snippet; the thread still lists fine.
        if (diff?.kind === 'text') {
          snippets.value = {
            ...snippets.value,
            [path]: { original: splitLines(diff.originalText), modified: splitLines(diff.modifiedText) },
          }
        }
      }
    }
    try {
      await Promise.all(Array.from({ length: snippetConcurrency }, worker))
    } finally {
      if (run === snippetsRun) snippetsLoading.value = false
    }
  }

  function resetThreads() {
    ++threadsRequestId
    threads.value = []
    threadsLoading.value = false
    threadsError.value = ''
    commentsOpen.value = false
    threadSearch.value = ''
    threadFilter.value = 'all'
    inlineThreadId.value = null
    draft.value = null
    editing.value = null
    deleting.value = null
    commentSaving.value = false
    commentError.value = ''
    ++snippetsRun
    snippets.value = {}
    snippetsLoading.value = false
  }

  async function loadThreads(project: string, repository: string, id: number) {
    const current = ++threadsRequestId
    threadsLoading.value = true
    threadsError.value = ''
    try {
      const result = await api.commentThreads(project, repository, id)
      if (current !== threadsRequestId) return
      threads.value = withoutDeleted(result)
      if (commentsOpen.value) void loadSnippets()
    } catch (cause) {
      if (current === threadsRequestId) threadsError.value = message(cause)
    } finally {
      if (current === threadsRequestId) threadsLoading.value = false
    }
  }

  function toggleComments() {
    commentsOpen.value = !commentsOpen.value
    if (!commentsOpen.value) return
    draft.value = null
    commentError.value = ''
    void loadSnippets()
  }

  // From the round screen straight to the remarks waiting for an answer.
  function openMyThreads() {
    threadFilter.value = 'mine'
    if (commentsOpen.value) return
    toggleComments()
  }

  // --- Writes ---
  // No optimistic write anywhere in here. The button locks, the request goes out, and the
  // threads are read back — a comment cannot be rolled back, so nothing is shown as sent
  // before Azure DevOps says it is. Every write takes this one road.
  async function writeThen(
    write: (project: string, repository: string, id: number) => Promise<unknown>,
    onStored: () => void = () => {},
  ) {
    if (!details.value || commentSaving.value) return
    const current = threadsRequestId
    const project = projectId.value
    const repository = repositoryId.value
    const id = details.value.id
    commentSaving.value = true
    commentError.value = ''
    try {
      await write(project, repository, id)
      if (current !== threadsRequestId) return
      onStored()
      await loadThreads(project, repository, id)
    } catch (cause) {
      if (current === threadsRequestId) commentError.value = message(cause)
    } finally {
      // Unconditionally: reloading the threads bumps the request id, so a guard here would
      // leave every comment button disabled for the rest of the pull request.
      commentSaving.value = false
    }
  }

  function startDraft(target: string) {
    if (!commentsEnabled.value) return
    draft.value = { target, text: '' }
    commentError.value = ''
  }

  function sendDraft(resolveAfter = false) {
    const pending = draft.value
    if (!pending || !pending.text.trim()) return Promise.resolve()
    return writeThen(async (project, repository, id) => {
      if (pending.target === 'new') {
        await api.createThread(project, repository, id, { content: pending.text, filePath: null, line: null })
      } else if (pending.target.startsWith('file:')) {
        const [, path, line] = pending.target.split(':')
        await api.createThread(project, repository, id,
          { content: pending.text, filePath: path!, line: line ? Number(line) : null })
      } else {
        const threadId = Number(pending.target)
        await api.replyToThread(project, repository, id, threadId, pending.text)
        // Two calls, in this order, because Azure DevOps has no combined one: the reply is
        // what matters, so resolving happens only once it is safely stored.
        if (resolveAfter) await api.setThreadStatus(project, repository, id, threadId, 'fixed')
      }
    }, () => { draft.value = null })
  }

  function setThreadStatus(threadId: number, status: string) {
    return writeThen((project, repository, id) => api.setThreadStatus(project, repository, id, threadId, status))
  }

  function startEdit(threadId: number, commentId: number, content: string) {
    editing.value = { threadId, commentId, text: content }
    draft.value = null
    deleting.value = null
    commentError.value = ''
  }

  function sendEdit() {
    const pending = editing.value
    if (!pending || !pending.text.trim()) return Promise.resolve()
    return writeThen((project, repository, id) =>
      api.editComment(project, repository, id, pending.threadId, pending.commentId, pending.text),
    () => { editing.value = null })
  }

  function confirmDelete(threadId: number, commentId: number) {
    return writeThen((project, repository, id) => api.deleteComment(project, repository, id, threadId, commentId),
      () => { deleting.value = null })
  }

  // --- The conversation in the open file ---
  // Clicking a line keeps you in the diff. An existing thread opens above it, a line
  // without one opens a draft anchored there — switching panels at that moment would take
  // away the code the comment is about. Both are still two-step: nothing is sent here.
  const inlineThreadId = ref<number | null>(null)
  const inlineThread = computed(() =>
    threads.value.find(thread => thread.id === inlineThreadId.value) ?? null)
  const inlineThreadIndex = computed(() =>
    threadsInFile.value.findIndex(thread => thread.id === inlineThreadId.value))
  const lineDraft = computed(() => {
    const target = draft.value?.target ?? ''
    return target.startsWith(`file:${selectedFilePath.value}:`)
      ? Number(target.split(':')[2]) || null
      : null
  })
  const commentLinesForFile = computed(() => threadsInFile.value
    .filter(thread => (thread.rightLine ?? 0) > 0 && !isResolved(thread))
    .map(thread => thread.rightLine!))
  const resolvedLinesForFile = computed(() => threadsInFile.value
    .filter(thread => (thread.rightLine ?? 0) > 0 && isResolved(thread))
    .map(thread => thread.rightLine!))

  // Comment blocks are rendered by Monaco as zones between the code; these are the
  // containers it created for us, and the markup is teleported into them.
  const zoneTargets = ref<{ line: number; el: HTMLElement }[]>([])
  const collapsedZones = ref(new Set<number>())
  // Hiding them all is a per-session preference, not per file: you turn comments off to read
  // the code, and turning them back on for every file would defeat that.
  const showComments = ref(true)
  const zoneLines = computed(() => {
    const lines = showComments.value
      ? threadsInFile.value.filter(thread => (thread.rightLine ?? 0) > 0).map(thread => thread.rightLine!)
      : []
    if (lineDraft.value) lines.push(lineDraft.value)
    return [...new Set(lines)].sort((a, b) => a - b)
  })
  // Each container with the thread anchored at its line, or null for the line being drafted.
  const zoneCards = computed(() => zoneTargets.value.map(zone => ({
    ...zone,
    thread: threadsInFile.value.find(thread => thread.rightLine === zone.line) ?? null,
  })))
  // A file whose diff is binary or too large has no editor, so its threads would have
  // nowhere to live. There the docked block stays.
  const useZones = computed(() => fileDiff.value?.kind === 'text')

  function toggleZone(line: number) {
    const next = new Set(collapsedZones.value)
    if (!next.delete(line)) next.add(line)
    collapsedZones.value = next
  }

  // The panel shrinks when the conversation opens, so the line has to be scrolled back into
  // view after the layout settles — otherwise it ends up just below the fold.
  function revealCommentLine(line: number | null | undefined) {
    if (!line) return
    void nextTick(() => diffView.value?.revealLine?.(line))
  }

  function openLineComments(line: number) {
    const existing = threads.value.find(thread =>
      thread.filePath === selectedFilePath.value && thread.rightLine === line)
    commentError.value = ''
    if (existing) {
      inlineThreadId.value = existing.id
      draft.value = null
      // Opening a collapsed conversation has to expand it, or the click looks like a no-op.
      const next = new Set(collapsedZones.value)
      next.delete(line)
      collapsedZones.value = next
    } else {
      // Nothing to read here and nothing that could be written: clicking an empty line with
      // writing switched off would open a draft box that can only fail.
      if (!commentsEnabled.value) return
      inlineThreadId.value = null
      draft.value = { target: `file:${selectedFilePath.value}:${line}`, text: '' }
    }
    revealCommentLine(line)
  }

  function openThreadInFile(thread: PrCommentThread) {
    showComments.value = true
    inlineThreadId.value = thread.id
    draft.value = null
    commentError.value = ''
    revealCommentLine(thread.rightLine)
  }

  // With several comments in one file, stepping through them beats hunting for chips.
  function stepThreadInFile(offset: 1 | -1) {
    const list = threadsInFile.value
    if (list.length === 0) return
    const next = inlineThreadIndex.value < 0
      ? (offset === 1 ? 0 : list.length - 1)
      : (inlineThreadIndex.value + offset + list.length) % list.length
    openThreadInFile(list[next]!)
  }

  function closeInlineComments() {
    inlineThreadId.value = null
    if (lineDraft.value !== null) draft.value = null
  }

  function commentOnCursorLine() {
    const line = diffView.value?.cursorLine?.() ?? null
    if (!selectedFilePath.value || !line) return
    openLineComments(line)
  }

  // The open file is about to change or close: its conversation, zones and line draft go
  // with it. Called before the path changes, so the draft being dropped is this file's.
  function leaveFile() {
    inlineThreadId.value = null
    zoneTargets.value = []
    collapsedZones.value = new Set()
    if (lineDraft.value !== null) draft.value = null
  }

  async function openThread(thread: PrCommentThread) {
    if (!thread.filePath || !details.value?.changedFiles.some(file => file.path === thread.filePath)) return
    // The comments view sits in the same panel as the diff, so leaving it open would load
    // the file behind it and nothing on screen would change.
    commentsOpen.value = false
    // The whole diff even when the iteration filter is on: a thread is anchored to a line of
    // the full file, and a narrowed diff may not contain that line at all.
    await openFile(thread.filePath, null)
    openThreadInFile(thread)
  }

  async function showChangesSinceComment(thread: PrCommentThread) {
    if (!thread.filePath || thread.iterationId === null) return
    commentsOpen.value = false
    await openFile(thread.filePath, thread.iterationId)
  }

  return {
    threads, threadsLoading, threadsError, commentsOpen, threadSearch, threadFilter,
    draft, editing, deleting, commentSaving, commentError, commentsEnabled,
    activeThreadCount, threadsByFile, threadsInFile, myOpenThreads, mineKnown, visibleThreads, threadGroups,
    snippetsLoading, threadSnippet, movedSinceComment, lastIteration,
    inlineThreadId, inlineThread, inlineThreadIndex, lineDraft, commentLinesForFile, resolvedLinesForFile,
    zoneTargets, collapsedZones, showComments, zoneLines, zoneCards, useZones,
    resetThreads, loadThreads, toggleComments, openMyThreads,
    startDraft, sendDraft, setThreadStatus, startEdit, sendEdit, confirmDelete,
    toggleZone, openLineComments, openThreadInFile, stepThreadInFile, closeInlineComments,
    commentOnCursorLine, leaveFile, openThread, showChangesSinceComment,
  }
}

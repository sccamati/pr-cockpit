<script setup lang="ts">
// The shell: picking a project, a repository and a pull request, the open file with its
// diff, the file tree and the keyboard. Each feature of an open pull request lives in its
// own composable (use*.ts) and is handed to the child components through cockpit.ts.
import { computed, nextTick, onBeforeUnmount, onMounted, provide, ref, shallowRef, type Component } from 'vue'
import CommentDraft from './CommentDraft.vue'
import CommentThread from './CommentThread.vue'
import CommentsView from './CommentsView.vue'
import ContextRail from './ContextRail.vue'
import FileChat from './FileChat.vue'
import FileTree from './FileTree.vue'
import WalkDone from './WalkDone.vue'
import WalkEntry from './WalkEntry.vue'
import { api, type ChangedFile, type FileDiff, type Project, type PullRequestDetails, type PullRequestSummary, type Repository, type UsageSource } from './api'
import { cockpitKey } from './cockpit'
import { commentPreview, renderDescription } from './description'
import { buildFileTree, flattenTree, type TreeFile } from './fileTree'
import { changeLabel, fileDirectory, fileName, formatDate, message, scrollBehavior, threadStatusLabels } from './format'
import { useChecklist } from './useChecklist'
import { isResolved, useComments } from './useComments'
import { useFileAi } from './useFileAi'
import { useReviewProgress } from './useReviewProgress'
import { useSummary } from './useSummary'
import { useWalkthrough } from './useWalkthrough'

const projects = ref<Project[]>([])
const repositories = ref<Repository[]>([])
const pullRequests = ref<PullRequestSummary[]>([])
const details = ref<PullRequestDetails | null>(null)
const projectId = ref('')
const repositoryId = ref('')
const loading = ref(false)
const error = ref('')
const checklistProgress = ref<Record<number, number>>({})
const progressLoading = ref(false)
const progressError = ref('')
const fileReviewProgress = ref<Record<number, { reviewed: number; total: number }>>({})
let requestId = 0

// --- The open file ---
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
const focusMode = ref(false)
const sideBySide = ref(false)
// ponytail: hand-written structural type for the exposed diff instance. MonacoDiff is a
// dynamic import, so InstanceType<typeof MonacoDiff> is not available here.
const diffView = ref<{ goToDiff(target: 'next' | 'previous'): void; focusEditor(): void; cursorLine(): number | null; revealLine(line: number): void; selectedText(): string } | null>(null)
const helpDialog = ref<HTMLDialogElement | null>(null)
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

// --- The tree's filters ---
const fileSearch = ref('')
const onlyUnreviewed = ref(false)
// "Changes since update N", the way Azure DevOps offers it. Azure DevOps groups commits into
// iterations (one per push) and can only compare whole iterations, so this filter is per
// iteration and labelled with the commit that ended it — a commit from the middle of a push
// cannot be isolated without diffing commit to commit ourselves.
const filterIteration = ref<number | null>(null)
const filterPaths = ref<string[] | null>(null)
const filterLoading = ref(false)
const filterError = ref('')
let filterRequestId = 0

// --- The features of an open pull request ---
const scope = { details, projectId, repositoryId }
const summaryState = useSummary(scope)
const checklistState = useChecklist(scope)
const review = useReviewProgress({ ...scope, onLoaded: () => walk.afterReviewsLoaded() })
const { roleByPath, resetSummary, loadSavedSummary } = summaryState
const { checklist, checklistCompleted, remainingChecklist, resetChecklist, loadChecklist } = checklistState
const {
  walkPosition, fileReviewError, reviewedPaths, stalePaths, remainingCount, criticalPaths, manualCriticalLimit,
  isReviewed, isStale, isCritical, toggleCritical, setFileReviewed, resetFileReviews, loadFileReviews,
} = review

const lastIteration = computed(() =>
  (details.value?.iterations ?? []).reduce((highest, item) => Math.max(highest, item.id), 0))
const filterPathSet = computed(() => filterPaths.value && new Set(filterPaths.value))
const matchingFiles = computed(() => {
  const search = fileSearch.value.trim().toLocaleLowerCase()
  const since = filterPathSet.value
  return details.value?.changedFiles.filter(file =>
    (!since || since.has(file.path)) &&
    (!search || file.path.toLocaleLowerCase().includes(search) ||
      file.originalPath?.toLocaleLowerCase().includes(search))) ?? []
})
const filteredFiles = computed(() => onlyUnreviewed.value
  ? matchingFiles.value.filter(file => !isReviewed(file.path))
  : matchingFiles.value)
const unreviewedMatches = computed(() => matchingFiles.value.filter(file => !isReviewed(file.path)))
function toTreeFile(file: ChangedFile): TreeFile {
  return {
    path: file.path,
    name: fileName(file.path),
    changeType: changeLabel(file.changeType),
    originalPath: file.originalPath,
    reviewed: isReviewed(file.path),
    stale: isStale(file.path),
    critical: isCritical(file.path),
    criticalDisabled: !isCritical(file.path) && criticalPaths.value.length >= manualCriticalLimit.value,
    selected: selectedFilePath.value === file.path,
    role: roleByPath.value.get(file.path) ?? null,
    comments: threadsByFile.value.get(file.path)?.total ?? 0,
    unresolvedComments: threadsByFile.value.get(file.path)?.unresolved ?? 0,
  }
}
// Lockfiles, snapshots and build output are classified by the backend and get their own
// collapsed group, so a 44-file pull request stops opening on twelve rows nobody reads.
// They stay listed and navigable — hiding a file would be a claim it does not exist.
const noiseFiles = computed(() => filteredFiles.value.filter(file => file.category))
const codeFiles = computed(() => filteredFiles.value.filter(file => !file.category))
const expandAll = computed(() => fileSearch.value.trim().length > 0)
const fileTree = computed(() => buildFileTree(codeFiles.value.map(toTreeFile), expandAll.value))
const noiseTree = computed(() => buildFileTree(noiseFiles.value.map(toTreeFile), expandAll.value))
// Everything on screen is either all matching files, or only the unreviewed ones — so the
// rendered tree is the right basis for both stepping and "next unreviewed". Noise comes
// last, which is what keeps "next unreviewed" walking the code first.
const orderedPaths = computed(() => [...flattenTree(fileTree.value), ...flattenTree(noiseTree.value)])
const orderedIndex = computed(() => orderedPaths.value.indexOf(selectedFilePath.value))
const hasPreviousFile = computed(() => orderedIndex.value > 0)
const hasNextFile = computed(() => orderedIndex.value >= 0 && orderedIndex.value < orderedPaths.value.length - 1)
const nextUnreviewedPath = computed(() => {
  const paths = orderedPaths.value
  const selectedIndex = paths.indexOf(selectedFilePath.value)
  const afterSelected = paths.slice(selectedIndex + 1).find(path => !isReviewed(path))
  const beforeSelected = paths.slice(0, Math.max(selectedIndex, 0)).find(path => !isReviewed(path))
  return afterSelected ?? beforeSelected ?? null
})
const lastFileName = computed(() => lastFilePath.value ? fileName(lastFilePath.value) : '')
const descriptionHtml = computed(() => details.value?.description
  ? renderDescription(details.value.description, details.value.workItems)
  : '')
const filePosition = computed(() => {
  const index = details.value?.changedFiles.findIndex(file => file.path === selectedFilePath.value) ?? -1
  return index < 0 ? null : index + 1
})
// j/k/m deliberately do not move focus, so a screen reader needs this spoken instead.
const readingStatus = computed(() => filePosition.value && details.value
  ? `Plik ${filePosition.value} z ${details.value.changedFilesCount} · ${selectedFilePath.value}`
  : '')
// Comparing the newest iteration with itself changes nothing, so it is not offered. Newest
// first, because "since my last pass" is the reason anybody opens this list.
const iterationChoices = computed(() => {
  const titles = new Map((details.value?.commits ?? []).map(commit => [commit.id, commit.message]))
  return (details.value?.iterations ?? []).filter(item => item.id < lastIteration.value).reverse().map(item => ({
    id: item.id,
    label: `Po aktualizacji ${item.id}` +
      (item.sourceCommitSha && titles.has(item.sourceCommitSha)
        ? ` — ${titles.get(item.sourceCommitSha)}` : ''),
  }))
})

const comments = useComments({ ...scope, selectedFilePath, fileDiff, orderedPaths, lastIteration, diffView, openFile })
const walk = useWalkthrough({
  details, summary: summaryState, review, orderedPaths, lastIteration, nextUnreviewedPath, selectedFilePath, openFile,
})
const fileAi = useFileAi({ ...scope, selectedFilePath, diffRequest: () => diffRequestId })
const {
  threads, commentsOpen, draft, editing, deleting, commentError, commentsEnabled, inlineThreadId, inlineThread,
  inlineThreadIndex, lineDraft, activeThreadCount, threadsByFile, threadsInFile, commentLinesForFile, resolvedLinesForFile,
  zoneTargets, zoneCards, zoneLines, collapsedZones, showComments, useZones,
  resetThreads, loadThreads, toggleComments, toggleZone, openLineComments, openThreadInFile, stepThreadInFile,
  closeInlineComments, commentOnCursorLine, leaveFile,
} = comments
const {
  view, walkWorthwhile, walkPaths, walkFilePath, enterWalkthrough, leaveWalkthrough, goToWalkIndex, walkAdvance, walkBack,
  resetWalkthrough,
} = walk
const {
  explanation, explanationLoading, explanationError, explanationOpen, questionOpen, questionTurns,
  resetExplanation, resetFileAi, fileOpened, explainFile, openQuestions,
} = fileAi

provide(cockpitKey, {
  ...scope, selectedFilePath,
  summary: summaryState, checklist: checklistState, review, walk, comments, fileAi,
  openFile, openCriticalFile, backToList,
})

function openCriticalFile(path: string) {
  void openFile(path)
  diffPanel.value?.scrollIntoView?.({ behavior: scrollBehavior(), block: 'start' })
}

function openNextUnreviewed() {
  if (nextUnreviewedPath.value) void openFile(nextUnreviewedPath.value)
}

function toggleReviewed() {
  if (!selectedFilePath.value || (!fileDiff.value && !isReviewed(selectedFilePath.value))) return
  const path = selectedFilePath.value
  // A stale marker counts as unread, so ticking the box restamps it rather than clearing it.
  void setFileReviewed(path, !isReviewed(path))
}

function resetDiff() {
  ++diffRequestId
  leaveFile()
  lastFilePath.value = ''
  selectedFilePath.value = ''
  fileDiff.value = null
  monacoComponent.value = null
  diffLoading.value = false
  diffError.value = ''
  diffSinceIteration.value = null
  resetExplanation()
  fileSearch.value = ''
  onlyUnreviewed.value = false
  ++filterRequestId
  filterIteration.value = null
  filterPaths.value = null
  filterLoading.value = false
  filterError.value = ''
}

// Everything that belongs to one pull request. Each reset bumps that feature's request id,
// which is what makes a late answer about the previous pull request land nowhere.
function resetPullRequest() {
  resetDiff()
  resetSummary()
  resetChecklist()
  resetFileReviews()
  resetWalkthrough()
  resetThreads()
  resetFileAi()
  details.value = null
  error.value = ''
}

function resetListProgress() {
  checklistProgress.value = {}
  progressLoading.value = false
  progressError.value = ''
  fileReviewProgress.value = {}
}

// Guards the outer requestId, not its own, exactly like loadChecklistProgress.
async function loadFileReviewProgress(current: number, project: string, repository: string) {
  try {
    const result = await api.fileReviewProgress(project, repository)
    if (current === requestId) {
      fileReviewProgress.value = Object.fromEntries(result.map(item =>
        [item.pullRequestId, { reviewed: item.reviewedCount, total: item.changedFilesCount }]))
    }
  } catch {
    // The checklist progress row already reports loading failures for this list; a second
    // banner for the file counter would be noise. Missing counters simply do not render.
    if (current === requestId) fileReviewProgress.value = {}
  }
}

async function loadChecklistProgress(current: number, project: string, repository: string) {
  progressLoading.value = true
  progressError.value = ''
  try {
    const result = await api.checklistProgress(project, repository)
    if (current === requestId) {
      checklistProgress.value = Object.fromEntries(result.map(item => [item.pullRequestId, item.completedCount]))
    }
  } catch (cause) {
    if (current === requestId) progressError.value = message(cause)
  } finally {
    if (current === requestId) progressLoading.value = false
  }
}

async function loadProjects() {
  const current = ++requestId
  loading.value = true
  error.value = ''
  try {
    const result = await api.projects()
    if (current !== requestId) return
    projects.value = result
    if (projects.value.length === 1) {
      projectId.value = projects.value[0]!.id
      await loadRepositories()
    }
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

async function loadRepositories() {
  const current = ++requestId
  resetPullRequest()
  resetListProgress()
  repositories.value = []
  repositoryId.value = ''
  pullRequests.value = []
  if (!projectId.value) return
  loading.value = true
  try {
    const result = await api.repositories(projectId.value)
    if (current !== requestId) return
    repositories.value = result
    if (repositories.value.length === 1) {
      repositoryId.value = repositories.value[0]!.id
      await loadPullRequests()
    }
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

async function loadPullRequests() {
  const current = ++requestId
  resetPullRequest()
  resetListProgress()
  pullRequests.value = []
  if (!repositoryId.value) return
  loading.value = true
  try {
    const result = await api.pullRequests(projectId.value, repositoryId.value)
    if (current === requestId) {
      pullRequests.value = result
      if (result.length > 0) {
        void loadChecklistProgress(current, projectId.value, repositoryId.value)
        void loadFileReviewProgress(current, projectId.value, repositoryId.value)
      }
    }
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

async function openPullRequest(id: number) {
  const current = ++requestId
  resetPullRequest()
  loading.value = true
  try {
    const result = await api.pullRequest(projectId.value, repositoryId.value, id)
    if (current === requestId) {
      details.value = result
      // US-P3: a pull request big enough to get lost in opens on the proposal, not on the
      // tree. The tree is one click away and everything in it still works.
      if (walkWorthwhile.value) view.value = 'entry'
      void loadChecklist(projectId.value, repositoryId.value, id)
      void loadSavedSummary(projectId.value, repositoryId.value, id)
      void loadFileReviews(projectId.value, repositoryId.value, id)
      void loadThreads(projectId.value, repositoryId.value, id)
    }
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

function backToList() {
  ++requestId
  resetPullRequest()
  loading.value = false
  if (repositoryId.value && pullRequests.value.length > 0) {
    void loadChecklistProgress(requestId, projectId.value, repositoryId.value)
    void loadFileReviewProgress(requestId, projectId.value, repositoryId.value)
  }
}

async function setFilterIteration(value: number | null) {
  const current = ++filterRequestId
  filterIteration.value = value
  filterPaths.value = null
  filterError.value = ''
  if (value === null || !details.value) return
  const pullRequestId = details.value.id
  filterLoading.value = true
  try {
    const paths = await api.changedPathsSince(projectId.value, repositoryId.value, pullRequestId, value)
    if (current !== filterRequestId) return
    filterPaths.value = paths
  } catch (cause) {
    // A filter that silently shows everything would be worse than none: say it failed and
    // leave the list whole rather than pretending the narrowing happened.
    if (current !== filterRequestId) return
    filterIteration.value = null
    filterError.value = message(cause)
  } finally {
    if (current === filterRequestId) filterLoading.value = false
  }
}

// sinceIteration: a number compares against that iteration, null asks for the whole diff
// however the filter is set, and leaving it out follows the filter — so every existing
// caller keeps working and the tree gets the narrowed diff for free.
async function openFile(path: string, sinceIteration?: number | null) {
  if (!details.value) return
  const current = ++diffRequestId
  const since = sinceIteration === undefined ? filterIteration.value : sinceIteration
  diffSinceIteration.value = since
  const pullRequestId = details.value.id
  leaveFile()
  selectedFilePath.value = path
  fileOpened(pullRequestId, path, current)
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

const shortcutHelp = [
  { keys: 'j / n', label: 'Następny plik' },
  { keys: 'k / p', label: 'Poprzedni plik' },
  { keys: 'm', label: 'Obejrzałem i przejdź dalej' },
  { keys: '. / ]', label: 'Następna zmiana w pliku' },
  { keys: ', / [', label: 'Poprzednia zmiana w pliku' },
  { keys: '/', label: 'Szukaj pliku' },
  { keys: 's', label: 'Widok obok siebie / w linii' },
  { keys: 'f', label: 'Tryb skupienia' },
  { keys: 'g', label: 'Przejdź kursorem do kodu' },
  { keys: 'o', label: 'Opis PR i powrót do pliku' },
  { keys: 'e', label: 'Wyjaśnij ten plik' },
  { keys: 'a', label: 'Zapytaj o ten plik' },
  { keys: 'c', label: 'Widok komentarzy' },
  { keys: 'Esc', label: 'Zamknij pomoc albo wróć do listy' },
  { keys: '?', label: 'Ta pomoc' },
]

function goToDiff(target: 'next' | 'previous') {
  diffView.value?.goToDiff(target)
}

// Leaving a file for the description has to be reversible, so the path is kept and the
// briefing offers a way back. resetDiff() clears it first, hence the order here.
function showBriefing() {
  const path = selectedFilePath.value
  resetDiff()
  lastFilePath.value = path
}

function toggleBriefing() {
  if (selectedFilePath.value) showBriefing()
  else if (lastFilePath.value) void openFile(lastFilePath.value)
}

function focusSearch() {
  const input = document.getElementById('file-search') as HTMLInputElement | null
  input?.focus()
  input?.select()
}

function stepFile(offset: 1 | -1) {
  // In the walkthrough j/k move along the path, not along the tree — same keys, the list
  // under them is the one on screen.
  if (view.value === 'walk') {
    void goToWalkIndex(walkPosition.value + offset)
    return
  }
  const paths = orderedPaths.value
  if (paths.length === 0) return
  const index = paths.indexOf(selectedFilePath.value)
  if (index < 0) {
    void openFile(paths[0]!)
    return
  }
  const target = index + offset
  if (target < 0 || target >= paths.length) return
  void openFile(paths[target]!)
}

// toggleReviewed writes synchronously, so the computed below it re-evaluates against
// the new state on the next line. That ordering is what the keyboard test pins.
function markAndAdvance() {
  if (view.value === 'walk') {
    void walkAdvance(true)
    return
  }
  if (!selectedFilePath.value) return
  if (!fileDiff.value && !isReviewed(selectedFilePath.value)) return
  if (!isReviewed(selectedFilePath.value)) toggleReviewed()
  openNextUnreviewed()
}

// Esc peels one layer at a time. Leaving the pull request is the last of them — one key
// that drops the edit, the draft, the conversation and the whole PR at once is a trap.
function escapeLayer() {
  if (helpDialog.value?.open) closeHelp()
  else if (questionOpen.value) questionOpen.value = false
  else if (deleting.value !== null) deleting.value = null
  else if (editing.value) editing.value = null
  else if (draft.value) { draft.value = null; commentError.value = '' }
  else if (inlineThreadId.value !== null) closeInlineComments()
  else if (commentsOpen.value) commentsOpen.value = false
  else if (focusMode.value) focusMode.value = false
  else if (view.value === 'walk' || view.value === 'done') leaveWalkthrough()
  else if (view.value === 'entry') view.value = 'tree'
  else backToList()
}

function toggleHelp() {
  const dialog = helpDialog.value
  if (!dialog) return
  if (dialog.open) dialog.close?.()
  else dialog.showModal?.()
}

function closeHelp() {
  helpDialog.value?.close?.()
}

const shortcuts: Record<string, () => void> = {
  j: () => stepFile(1),
  n: () => stepFile(1),
  k: () => stepFile(-1),
  p: () => stepFile(-1),
  m: markAndAdvance,
  '.': () => goToDiff('next'),
  ']': () => goToDiff('next'),
  ',': () => goToDiff('previous'),
  '[': () => goToDiff('previous'),
  '/': focusSearch,
  s: () => { sideBySide.value = !sideBySide.value },
  f: () => { focusMode.value = !focusMode.value },
  g: () => diffView.value?.focusEditor(),
  o: toggleBriefing,
  e: explainFile,
  a: () => void openQuestions(),
  c: toggleComments,
}

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
    if (target.closest('.comment-draft')) escapeLayer()
    return
  }
  if (event.key === '?') {
    event.preventDefault()
    toggleHelp()
    return
  }
  if (!details.value) return
  if (event.key === 'Escape') {
    event.preventDefault()
    escapeLayer()
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

onMounted(loadProjects)
onMounted(async () => {
  // Swallows everything: the backend enforces the switch either way, so a failed probe
  // must not cost more than an offer to write that then fails.
  try { commentsEnabled.value = (await api.config()).commentsEnabled } catch { /* leave it on */ }
})
</script>

<template>
  <div class="shell">
    <header class="topbar" :class="{ 'topbar--details': details }">
      <div class="brand"><span class="brand-mark">PR</span><span>Cockpit</span></div>
    </header>

    <main :class="{ 'main--details': details }">
      <div v-if="!details" class="intro">
        <div>
          <p class="eyebrow">PULL REQUESTS</p>
          <h1>Aktywne Pull Requesty</h1>
          <p class="subtitle">Wybierz projekt i repozytorium, aby zobaczyć bieżące zmiany.</p>
        </div>
      </div>

      <section v-if="!details" class="filters" aria-label="Wybór źródła">
        <label>
          <span>Projekt</span>
          <select v-model="projectId" :disabled="loading && projects.length === 0" @change="loadRepositories">
            <option value="">Wybierz projekt</option>
            <option v-for="project in projects" :key="project.id" :value="project.id">{{ project.name }}</option>
          </select>
        </label>
        <label>
          <span>Repozytorium</span>
          <select v-model="repositoryId" :disabled="!projectId" @change="loadPullRequests">
            <option value="">Wybierz repozytorium</option>
            <option v-for="repository in repositories" :key="repository.id" :value="repository.id">{{ repository.name }}</option>
          </select>
        </label>
        <button class="refresh-button" type="button" :disabled="!repositoryId || loading" @click="loadPullRequests">Odśwież</button>
      </section>

      <p v-if="error" class="notice error" role="alert">{{ error }}</p>
      <p v-if="loading" class="notice" role="status">Pobieranie danych…</p>

      <section v-if="details" class="details">
        <div class="pr-header">
          <button class="back-button" type="button" @click="backToList">← Wróć</button>
          <h2 :title="details.title"><span class="pr-header-number">#{{ details.id }}</span> {{ details.title }}</h2>
          <span class="status">{{ details.status }}</span>
          <span class="pr-header-meta" :title="`${details.sourceBranch} → ${details.targetBranch}`">{{ details.sourceBranch }} → {{ details.targetBranch }}</span>
          <span class="pr-header-meta">{{ details.author }}</span>
          <span v-if="checklist" class="pr-header-progress">Checklista {{ checklistCompleted }} / 6</span>
          <!-- The rail is hidden in focus mode, so the header is the only place an
               unresolved conversation can stay visible. -->
          <button v-if="threads.length" type="button" class="pr-header-comments"
            :class="{ 'pr-header-comments--open': activeThreadCount > 0 }"
            :title="`${activeThreadCount} nierozwiązanych z ${threads.length} · widok komentarzy (c)`"
            :aria-label="`Komentarze: ${activeThreadCount} nierozwiązanych z ${threads.length}`"
            @click="toggleComments">💬 {{ activeThreadCount }}</button>
          <button class="shortcut-button" type="button" aria-label="Skróty klawiszowe" title="Skróty klawiszowe (?)"
            @click="toggleHelp">?</button>
        </div>

        <WalkEntry v-if="view === 'entry'" />
        <WalkDone v-else-if="view === 'done'" />

        <!-- Writing switched off in the backend: the actions are not disabled but absent,
             because a row of five greyed-out buttons is noise, not information. -->
        <div v-else class="pr-workspace" :class="{ 'pr-workspace--focus': focusMode, 'pr-workspace--walk': view === 'walk', 'pr-workspace--readonly': !commentsEnabled }">
          <div class="file-list-pane">
            <div class="file-list-head">
              <div class="file-review-heading">
                <h3>Zmienione pliki ({{ details.changedFilesCount }})</h3>
                <span>{{ reviewedPaths.length }} / {{ details.changedFiles.length }} obejrzanych</span>
              </div>
              <progress v-if="details.changedFiles.length" class="file-progress" :value="reviewedPaths.length"
                :max="details.changedFiles.length" aria-label="Postęp przeglądania plików" />
              <p v-if="stalePaths.length" class="file-stale-note">
                {{ stalePaths.length }} {{ stalePaths.length === 1 ? 'plik zmienił się' : 'plików zmieniło się' }} od czasu przeczytania.
              </p>
              <p v-if="fileReviewError" class="notice error file-review-error" role="alert">{{ fileReviewError }}</p>
              <p v-if="details.changedFiles.length && remainingCount === 0" class="review-complete" role="status">
                Wszystkie pliki obejrzane.<template v-if="remainingChecklist.length"> Zostało w checkliście: {{ remainingChecklist.join(', ') }}.</template>
              </p>
            </div>
            <p v-if="details.changedFiles.length === 0" class="file-list-empty">Brak zmienionych plików.</p>
            <template v-else>
              <label class="file-search-label" for="file-search">Szukaj pliku</label>
              <input id="file-search" v-model="fileSearch" class="file-search" type="search" placeholder="Nazwa lub ścieżka" autocomplete="off">
              <div class="file-filter" role="group" aria-label="Filtr plików">
                <button type="button" :aria-pressed="!onlyUnreviewed" :class="{ active: !onlyUnreviewed }" @click="onlyUnreviewed = false">Wszystkie</button>
                <button type="button" :aria-pressed="onlyUnreviewed" :class="{ active: onlyUnreviewed }" @click="onlyUnreviewed = true">Nieobejrzane</button>
              </div>
              <template v-if="iterationChoices.length">
                <label class="file-search-label" for="iteration-filter">Pokaż zmiany</label>
                <select id="iteration-filter" class="iteration-filter" :value="filterIteration ?? ''"
                  :disabled="filterLoading"
                  @change="setFilterIteration(($event.target as HTMLSelectElement).value === '' ? null : Number(($event.target as HTMLSelectElement).value))">
                  <option value="">Z całego PR</option>
                  <option v-for="choice in iterationChoices" :key="choice.id" :value="choice.id">{{ choice.label }}</option>
                </select>
                <p v-if="filterError" class="notice error" role="alert">{{ filterError }}</p>
                <p v-else-if="filterLoading" class="file-list-hint" role="status">Sprawdzam, co się zmieniło…</p>
                <p v-else-if="filterPaths" class="file-list-hint" role="status">
                  {{ filterPaths.length === 0 ? 'Po tej aktualizacji nic się nie zmieniło.' : `Zmienione po aktualizacji ${filterIteration}: ${filterPaths.length} z ${details.changedFiles.length}. Diff pokazuje tylko te zmiany.` }}
                </p>
              </template>
              <button v-if="walkWorthwhile" class="walk-enter-button" type="button" @click="enterWalkthrough">Prowadź mnie przez PR</button>
              <button class="next-file-button" type="button" :disabled="!nextUnreviewedPath" @click="openNextUnreviewed">Następny nieobejrzany →</button>
              <p v-if="matchingFiles.length === 0" class="file-list-empty">Nie znaleziono plików.</p>
              <p v-else-if="!nextUnreviewedPath && remainingCount > 0" class="file-list-hint">{{ unreviewedMatches.length === 0 ? 'Brak nieobejrzanych plików w wynikach wyszukiwania.' : 'To ostatni nieobejrzany plik. Oznacz go po przejrzeniu.' }}</p>
              <div v-if="codeFiles.length > 0" class="changed-files" aria-label="Zmienione pliki">
                <FileTree :node="fileTree" :show-ratio="!onlyUnreviewed" @open="openFile" @toggle-critical="toggleCritical" />
              </div>
              <details v-if="noiseFiles.length > 0" class="noise-section" :open="expandAll || noiseTree.containsSelected">
                <summary class="noise-summary">Szum ({{ noiseFiles.length }})</summary>
                <FileTree :node="noiseTree" :show-ratio="!onlyUnreviewed" @open="openFile" @toggle-critical="toggleCritical" />
              </details>
            </template>
          </div>

          <div ref="diffPanel" class="diff-panel">
            <!-- US-P4. Position in the path, not in the Azure DevOps file list: the
                 walkthrough counts what it walks. -->
            <div v-if="view === 'walk'" class="walk-bar">
              <span class="walk-progress">Krok {{ walkPosition + 1 }} z {{ walkPaths.length }}</span>
              <span class="walk-bar-file" :title="walkFilePath">{{ fileName(walkFilePath) }}<small>{{ fileDirectory(walkFilePath) }}</small></span>
              <span v-if="roleByPath.get(walkFilePath)" class="walk-bar-role">{{ roleByPath.get(walkFilePath) }}</span>
            </div>
            <!-- Nothing is generated by walking in here: an explanation costs a model run,
                 so it happens when asked for and not a file sooner. Asking lives in the
                 toolbar below and on `e`, so this section exists only once there is
                 something to read — an empty panel holding one button was two rows of
                 chrome for an action that was already on screen. -->
            <section v-if="view === 'walk' && (explanation || explanationError)" class="walk-explanation"
              aria-label="Wyjaśnienie pliku">
              <p v-if="explanation" class="walk-explanation-text">{{ explanation.sentences.join(' ') }}</p>
              <p v-else class="notice error" role="alert">
                {{ explanationError }}
                <button class="checklist-retry" type="button" :disabled="explanationLoading" @click="explainFile">Ponów</button>
              </p>
            </section>
            <CommentsView v-if="commentsOpen" />
            <template v-else-if="selectedFilePath">
              <div class="diff-toolbar">
                <div class="diff-toolbar-title" :title="selectedFilePath"><h4>{{ fileName(selectedFilePath) }}</h4><span>{{ fileDirectory(selectedFilePath) }}</span></div>
                <span v-if="filePosition" class="diff-position">Plik {{ filePosition }} z {{ details.changedFilesCount }}</span>
                <div class="diff-actions">
                  <span class="diff-actions-label">Plik</span>
                  <button type="button" title="Poprzedni plik (k)" aria-label="Poprzedni plik"
                    :disabled="!hasPreviousFile" @click="stepFile(-1)">‹</button>
                  <button type="button" title="Następny plik (j)" aria-label="Następny plik"
                    :disabled="!hasNextFile" @click="stepFile(1)">›</button>
                  <span class="diff-actions-label">Zmiana</span>
                  <button type="button" title="Poprzednia zmiana w pliku (,)" aria-label="Poprzednia zmiana w pliku" @click="goToDiff('previous')">‹</button>
                  <button type="button" title="Następna zmiana w pliku (.)" aria-label="Następna zmiana w pliku" @click="goToDiff('next')">›</button>
                  <button type="button" class="diff-layout-toggle" :aria-pressed="sideBySide" @click="sideBySide = !sideBySide">{{ sideBySide ? 'Obok siebie' : 'W linii' }}</button>
                  <button type="button" title="Opis PR (o)" @click="showBriefing">Opis PR</button>
                  <!-- Renderowany zawsze, choćby wyłączony: pod v-if kolejne przyciski
                       przeskakiwały w poziomie na każdym pliku z komentarzem. -->
                  <button type="button" class="comments-toggle" :disabled="!threadsInFile.length"
                    :aria-pressed="showComments" :title="showComments ? 'Ukryj komentarze w kodzie' : 'Pokaż komentarze w kodzie'"
                    @click="showComments = !showComments">{{ showComments && threadsInFile.length ? 'Ukryj komentarze' : `Pokaż komentarze (${threadsInFile.length})` }}</button>
                  <button type="button" class="comment-line-button" title="Skomentuj linię pod kursorem"
                    :disabled="!fileDiff || fileDiff.kind !== 'text'" @click="commentOnCursorLine">Skomentuj linię</button>
                  <button type="button" class="explain-button" title="Wyjaśnij ten plik (e)"
                    :disabled="explanationLoading" @click="explainFile">{{ explanationLoading ? 'Wyjaśniam…' : 'Wyjaśnij ten plik' }}</button>
                  <button type="button" class="ask-button" title="Zapytaj o ten plik (a)"
                    :aria-pressed="questionOpen" :disabled="!fileDiff" @click="openQuestions()">Zapytaj o plik<span
                      v-if="questionTurns.length"> ({{ questionTurns.length }})</span></button>
                </div>
                <label class="review-check"><input type="checkbox" :checked="isReviewed(selectedFilePath)" :disabled="!fileDiff && !isReviewed(selectedFilePath)" @change="toggleReviewed"> Obejrzałem</label>
              </div>
              <p v-if="diffSinceIteration" class="notice diff-since" role="status">
                Pokazuję wyłącznie zmiany od iteracji {{ diffSinceIteration }} — czyli to, co dopisano później.
                <button type="button" class="checklist-retry" @click="openFile(selectedFilePath, null)">Pokaż cały diff</button>
              </p>
              <p class="reading-status" aria-live="polite">{{ readingStatus }}</p>
              <!-- Every thread in this file, so comments are visible on arrival instead of
                   having to be found. A chip opens the conversation below it. -->
              <div v-if="threadsInFile.length" class="file-threads">
                <span class="file-threads-label">Komentarze w pliku:</span>
                <button v-for="thread in threadsInFile" :key="thread.id" type="button"
                  class="file-thread-chip" :class="{ 'file-thread-chip--resolved': isResolved(thread) }"
                  :aria-pressed="inlineThreadId === thread.id"
                  :title="thread.comments[0]?.content ? commentPreview(thread.comments[0].content) : ''"
                  @click="openThreadInFile(thread)">
                  {{ isResolved(thread) ? '✓' : '💬' }} {{ thread.rightLine ? `linia ${thread.rightLine}` : 'plik' }}
                </button>
              </div>
              <!-- The conversation for the clicked line, docked above the diff so the code
                   it is about stays on screen. Only where there is no editor to hold zones. -->
              <div v-if="!useZones && (inlineThread || lineDraft !== null)" class="inline-comments">
                <div class="inline-comments-head">
                  <strong>{{ inlineThread ? `Komentarz w linii ${inlineThread.rightLine ?? '—'}` : `Nowy komentarz do linii ${lineDraft}` }}</strong>
                  <template v-if="inlineThread && threadsInFile.length > 1">
                    <span class="inline-position">{{ inlineThreadIndex + 1 }} z {{ threadsInFile.length }}</span>
                    <button type="button" aria-label="Poprzedni komentarz w pliku" @click="stepThreadInFile(-1)">‹</button>
                    <button type="button" aria-label="Następny komentarz w pliku" @click="stepThreadInFile(1)">›</button>
                  </template>
                  <button type="button" class="inline-close" @click="closeInlineComments">Zamknij</button>
                </div>
                <p v-if="commentError" class="notice error" role="alert">{{ commentError }}</p>
                <CommentThread v-if="inlineThread" :thread="inlineThread" />
                <CommentDraft v-else-if="draft" label="Treść komentarza do linii" @cancel="closeInlineComments" />
              </div>
              <!-- Not in the walkthrough: there the explanation already has its own panel
                   above the diff, and the same few sentences twice on one screen is noise. -->
              <p v-if="explanationError && view !== 'walk'" class="notice error file-explanation-error" role="alert">{{ explanationError }}</p>
              <!-- Foldable like a comment: it is a few sentences you read once, and it was
                   holding the top of the panel for the rest of the file. -->
              <details v-if="explanation && view !== 'walk'" class="file-explanation" :open="explanationOpen" aria-label="Wyjaśnienie pliku"
                @toggle="explanationOpen = ($event.target as HTMLDetailsElement).open">
                <summary class="file-explanation-summary">Wyjaśnienie AI<span v-if="!explanationOpen"> · {{ explanation.sentences[0] }}</span></summary>
                <p v-for="(sentence, index) in explanation.sentences" :key="index">{{ sentence }}</p>
                <p class="file-explanation-note muted">Dla tej wersji pliku. Zapisane lokalnie.
                  <button type="button" class="explanation-dismiss" @click="resetExplanation">Ukryj</button>
                </p>
              </details>
              <!-- Shown in the walkthrough too — that is where you read code you did not
                   write, and the explanation's second panel has no equivalent here. -->
              <FileChat v-if="questionOpen" />
              <p v-if="diffLoading" class="diff-message muted" role="status">Pobieranie diffu…</p>
              <p v-else-if="diffError" class="diff-message notice error" role="alert">{{ diffError }}</p>
              <p v-else-if="fileDiff?.kind === 'binary'" class="diff-message muted">Plik binarny — diff tekstowy jest niedostępny.</p>
              <p v-else-if="fileDiff?.kind === 'tooLarge'" class="diff-message muted">Plik jest zbyt duży, aby pokazać diff (limit 256 KB na wersję lub 4000 linii łącznie).</p>
              <component :is="monacoComponent" v-else-if="fileDiff?.kind === 'text' && monacoComponent" ref="diffView" :path="fileDiff.path"
                :original-path="fileDiff.originalPath" :original-text="fileDiff.originalText" :modified-text="fileDiff.modifiedText"
                :side-by-side="sideBySide" :comment-lines="commentLinesForFile" :resolved-lines="resolvedLinesForFile"
                :zone-lines="zoneLines" :usages="usageSource" @open-line="openLineComments" @zones="zoneTargets = $event" @ask="openQuestions($event)" />
              <!-- Rendered between the lines of code, inside the containers Monaco made.
                   Ordinary Vue markup, so replying and resolving work the same as in the
                   comments view. -->
              <Teleport v-for="zone in zoneCards" :key="zone.line" :to="zone.el">
                <!-- The editor must not treat a click in the conversation as a click in the
                     code, but it must not be prevented from happening either. -->
                <div class="zone-card" :class="{ 'zone-card--draft': !zone.thread }" @mousedown.stop @click.stop>
                  <template v-if="zone.thread">
                    <!-- The whole header folds the block; the caret is a hint, not the only target. -->
                    <div class="zone-head" role="button" tabindex="0" :aria-expanded="!collapsedZones.has(zone.line)"
                      @click="toggleZone(zone.line)" @keydown.enter.prevent="toggleZone(zone.line)" @keydown.space.prevent="toggleZone(zone.line)">
                      <span class="zone-toggle">{{ collapsedZones.has(zone.line) ? '▸' : '▾' }}</span>
                      <span class="thread-author">{{ zone.thread.comments[0]?.author ?? 'Nieznany autor' }}</span><time v-if="zone.thread.comments[0]?.publishedAt" class="thread-date" :datetime="zone.thread.comments[0].publishedAt">{{ formatDate(zone.thread.comments[0].publishedAt) }}</time>
                      <span v-if="zone.thread.status && threadStatusLabels[zone.thread.status]" class="thread-status">{{ threadStatusLabels[zone.thread.status] }}</span>
                      <span v-if="zone.thread.comments.length > 1" class="thread-status">{{ zone.thread.comments.length }} wpisy</span>
                      <span v-if="collapsedZones.has(zone.line)" class="zone-preview">{{ commentPreview(zone.thread.comments[0]?.content ?? '') }}</span>
                    </div>
                    <template v-if="!collapsedZones.has(zone.line)">
                      <p v-if="commentError && inlineThreadId === zone.thread.id" class="notice error" role="alert">{{ commentError }}</p>
                      <CommentThread :thread="zone.thread" hide-first-author />
                    </template>
                  </template>
                  <template v-else-if="draft">
                    <div class="zone-head"><strong>Nowy komentarz do linii {{ zone.line }}</strong></div>
                    <p v-if="commentError" class="notice error" role="alert">{{ commentError }}</p>
                    <CommentDraft label="Treść komentarza do linii" @cancel="closeInlineComments" />
                  </template>
                </div>
              </Teleport>
            </template>
            <div v-else class="pr-briefing">
              <button v-if="lastFilePath" class="briefing-back" type="button" title="Wróć do pliku (o)"
                @click="openFile(lastFilePath)">← Wróć do {{ lastFileName }}</button>
              <h3>O co chodzi w tym PR</h3>
              <p v-if="!details.description" class="muted">Brak opisu.</p>
              <div v-else class="description markdown-body" v-html="descriptionHtml" />
              <p class="diff-placeholder">Wybierz plik z listy, aby zobaczyć jego diff.</p>
            </div>

            <!-- US-P4 zone 4: reachable without scrolling the diff, so the end of a file is
                 always one click away. -->
            <div v-if="view === 'walk'" class="walk-actions walk-actions--sticky">
              <button type="button" class="walk-primary" @click="walkAdvance(true)">
                {{ walkPosition + 1 >= walkPaths.length ? 'Przeczytane · zakończ przejście' : 'Przeczytane, dalej (m)' }}
              </button>
              <button type="button" title="Dalej bez oznaczania jako przeczytane" @click="walkAdvance(false)">Pomiń</button>
              <button type="button" title="Poprzedni krok (k)" :disabled="walkPosition === 0" @click="walkBack">← Wstecz</button>
              <button type="button" title="Wyjdź z przejścia (Esc)" @click="leaveWalkthrough">Wyjdź z przejścia</button>
            </div>
          </div>

          <ContextRail />
        </div>
      </section>

      <section v-else-if="repositoryId && !loading" class="card list-card">
        <div class="list-heading"><h2>Pull Requesty</h2><span>{{ pullRequests.length }} aktywnych</span></div>
        <p v-if="progressLoading" class="notice" role="status">Wczytywanie postępu checklist…</p>
        <div v-if="progressError" class="notice error" role="alert">Nie udało się wczytać postępu checklist: {{ progressError }} <button class="checklist-retry" type="button" @click="loadChecklistProgress(requestId, projectId, repositoryId)">Spróbuj ponownie</button></div>
        <p v-if="pullRequests.length === 0" class="empty">W tym repozytorium nie ma aktywnych Pull Requestów.</p>
        <button v-for="pr in pullRequests" :key="pr.id" class="pr-row" type="button" @click="openPullRequest(pr.id)">
          <span class="pr-number">#{{ pr.id }}</span>
          <span class="pr-title"><strong>{{ pr.title }}</strong><small>{{ pr.author }} · {{ pr.repository }}</small></span>
          <span class="status">{{ pr.status }}</span>
          <span class="pr-progress" :aria-label="`Postęp checklisty: ${progressLoading ? 'wczytywanie' : progressError ? 'błąd wczytywania' : `${checklistProgress[pr.id] ?? 0} z 6`}`">{{ progressLoading ? '…/6' : progressError ? '—/6' : `${checklistProgress[pr.id] ?? 0}/6` }}</span>
          <span class="pr-files">{{ fileReviewProgress[pr.id] ? `${fileReviewProgress[pr.id]!.reviewed}/${fileReviewProgress[pr.id]!.total} plików` : '' }}</span>
          <span class="pr-date">Utworzono {{ formatDate(pr.createdAt) }}</span>
          <span class="row-arrow">→</span>
        </button>
      </section>
      <section v-else-if="!loading" class="card empty">Wybierz projekt i repozytorium, aby rozpocząć.</section>
    </main>

    <dialog ref="helpDialog" class="shortcut-help" aria-label="Skróty klawiszowe">
      <h3>Skróty klawiszowe</h3>
      <dl>
        <div v-for="shortcut in shortcutHelp" :key="shortcut.keys"><dt>{{ shortcut.keys }}</dt><dd>{{ shortcut.label }}</dd></div>
      </dl>
      <button type="button" class="refresh-button" @click="closeHelp">Zamknij</button>
    </dialog>
  </div>
</template>

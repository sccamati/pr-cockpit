<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, type Component } from 'vue'
import FileTree from './FileTree.vue'
import { buildFileTree, flattenTree, type TreeFile } from './fileTree'
import { renderDescription } from './description'
import { api, type ChangedFile, type ChecklistItem, type FileExplanation, type ChecklistState, type FileDiff, type FileReviewEntry, type Project, type Repository, type PullRequestDetails, type PullRequestSummary, type SummaryResponse } from './api'

const projects = ref<Project[]>([])
const repositories = ref<Repository[]>([])
const pullRequests = ref<PullRequestSummary[]>([])
const details = ref<PullRequestDetails | null>(null)
const projectId = ref('')
const repositoryId = ref('')
const loading = ref(false)
const error = ref('')
const selectedFilePath = ref('')
const fileDiff = ref<FileDiff | null>(null)
const diffLoading = ref(false)
const diffError = ref('')
const summary = ref<SummaryResponse | null>(null)
const explanation = ref<FileExplanation | null>(null)
const explanationLoading = ref(false)
const explanationError = ref('')
const summaryLoading = ref(false)
const summaryError = ref('')
const summaryReadLoading = ref(false)
const summaryReadError = ref('')
const summarySavedAt = ref<string | null>(null)
const checklist = ref<ChecklistState | null>(null)
const checklistLoading = ref(false)
const checklistSaving = ref<ChecklistItem | null>(null)
// PRODUCT.md §9: the point is a few seconds of active thinking, not a grade. Nothing here
// checks the answer, and leaving it empty costs nothing.
// ponytail: one fixed question instead of the 1-3 generated failure scenarios §9 describes
// — it forces the same few seconds without a third AI path. Ceiling: if the fixed question
// turns out to be too weak, scenarios join the Summary schema.
const debugAnswer = ref('')
const debugSaving = ref(false)
const debugSaved = ref(false)
const showDebugHint = ref(false)
const checklistError = ref('')
const checklistProgress = ref<Record<number, number>>({})
const progressLoading = ref(false)
const progressError = ref('')
const fileSearch = ref('')
const onlyUnreviewed = ref(false)
// Server-backed and therefore per-PR by construction: loaded when a PR opens, cleared
// when it closes. The old tab-local dictionaries keyed by PR are gone, and so is the key.
const fileReviews = ref<Record<string, FileReviewEntry>>({})
const readingPath = ref<string[]>([])
const fileReviewSaving = ref<string | null>(null)
const fileReviewError = ref('')
const fileReviewProgress = ref<Record<number, { reviewed: number; total: number }>>({})
const diffPanel = ref<HTMLElement | null>(null)
const lastFilePath = ref('')
const monacoComponent = shallowRef<Component | null>(null)
const focusMode = ref(false)
const sideBySide = ref(false)
const helpDialog = ref<HTMLDialogElement | null>(null)
// ponytail: hand-written structural type for the exposed diff instance. MonacoDiff is a
// dynamic import, so InstanceType<typeof MonacoDiff> is not available here.
const diffView = ref<{ goToDiff(target: 'next' | 'previous'): void; focusEditor(): void } | null>(null)
let requestId = 0
let diffRequestId = 0
let summaryRequestId = 0
let checklistRequestId = 0
let reviewRequestId = 0

const checklistItems: { key: ChecklistItem; label: string }[] = [
  { key: 'aiReview', label: 'AI Review' },
  { key: 'quality', label: 'Quality' },
  { key: 'understand', label: 'Understand' },
  { key: 'architecture', label: 'Architecture' },
  { key: 'debug', label: 'Debug' },
  { key: 'ready', label: 'Ready' },
]
const checklistCompleted = computed(() => checklist.value
  ? checklistItems.filter(item => checklist.value![item.key]).length
  : 0)
const summaryFreshness = computed(() => {
  const savedSha = summary.value?.headCommitSha
  const currentSha = details.value?.headCommitSha
  if (!savedSha || !currentSha) return 'unknown'
  return savedSha.toLowerCase() === currentSha.toLowerCase() ? 'current' : 'stale'
})

const reviewedPaths = computed(() =>
  (details.value?.changedFiles ?? []).filter(file => reviewState(file.path) === 'current').map(file => file.path))
const stalePaths = computed(() =>
  (details.value?.changedFiles ?? []).filter(file => reviewState(file.path) === 'stale').map(file => file.path))
const reviewedPathSet = computed(() => new Set(reviewedPaths.value))
const stalePathSet = computed(() => new Set(stalePaths.value))
const criticalPaths = computed(() => {
  const currentPaths = new Set(details.value?.changedFiles.map(file => file.path) ?? [])
  return readingPath.value.filter(path => currentPaths.has(path))
})
const criticalPathSet = computed(() => new Set(criticalPaths.value))
const matchingFiles = computed(() => {
  const search = fileSearch.value.trim().toLocaleLowerCase()
  return details.value?.changedFiles.filter(file =>
    !search || file.path.toLocaleLowerCase().includes(search) ||
    file.originalPath?.toLocaleLowerCase().includes(search)) ?? []
})
const filteredFiles = computed(() => onlyUnreviewed.value
  ? matchingFiles.value.filter(file => !isReviewed(file.path))
  : matchingFiles.value)
const remainingCount = computed(() => (details.value?.changedFiles.length ?? 0) - reviewedPaths.value.length)
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
    criticalDisabled: !isCritical(file.path) && criticalPaths.value.length >= 10,
    selected: selectedFilePath.value === file.path,
    role: roleByPath.value.get(file.path) ?? null,
  }
}
// Lockfiles, snapshots and build output are classified by the backend and get their own
// collapsed group, so a 44-file pull request stops opening on twelve rows nobody reads.
// They stay listed and navigable — hiding a file would be a claim it does not exist.
// The AI ranking, kept apart from readingPath: PRODUCT.md §10 — a proposal is not a fact
// until the user accepts it, so nothing is written to the reading path behind their back.
const proposalDismissed = ref(false)
const criticalProposal = computed(() => {
  const paths = new Set(details.value?.changedFiles.map(file => file.path) ?? [])
  return (summary.value?.criticalFiles ?? []).filter(file => paths.has(file.path)).slice(0, 10)
})
const showProposal = computed(() =>
  criticalProposal.value.length > 0 && !proposalDismissed.value && criticalPaths.value.length === 0)
const roleByPath = computed(() => new Map(criticalProposal.value.map(file => [file.path, file.role])))
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
const lastFileName = computed(() => lastFilePath.value ? fileName(lastFilePath.value) : '')
const descriptionHtml = computed(() => details.value?.description
  ? renderDescription(details.value.description, details.value.workItems)
  : '')
const filePosition = computed(() => {
  const index = details.value?.changedFiles.findIndex(file => file.path === selectedFilePath.value) ?? -1
  return index < 0 ? null : index + 1
})
const remainingChecklist = computed(() => checklist.value
  ? checklistItems.filter(item => !checklist.value![item.key]).map(item => item.label)
  : [])
// j/k/m deliberately do not move focus, so a screen reader needs this spoken instead.
const readingStatus = computed(() => filePosition.value && details.value
  ? `Plik ${filePosition.value} z ${details.value.changedFilesCount} · ${selectedFilePath.value}`
  : '')
const nextUnreviewedPath = computed(() => {
  const paths = orderedPaths.value
  const selectedIndex = paths.indexOf(selectedFilePath.value)
  const afterSelected = paths.slice(selectedIndex + 1).find(path => !isReviewed(path))
  const beforeSelected = paths.slice(0, Math.max(selectedIndex, 0)).find(path => !isReviewed(path))
  return afterSelected ?? beforeSelected ?? null
})

// A marker goes stale only on positive evidence that the file changed: a blob id that no
// longer matches, or a head SHA that moved. With nothing to compare against we keep the
// mark, because nagging without cause is worse than a slightly optimistic tick.
// ponytail: the head SHA fallback is per-PR, so when Azure DevOps omits a blob id any new
// commit marks that file stale. Precise per-file tracking would need iteration diffing.
function reviewState(path: string): 'none' | 'current' | 'stale' {
  const entry = fileReviews.value[path]
  if (!entry) return 'none'
  const objectId = details.value?.changedFiles.find(file => file.path === path)?.objectId
  if (entry.blobId && objectId) return same(entry.blobId, objectId) ? 'current' : 'stale'
  const head = details.value?.headCommitSha
  if (entry.headSha && head) return same(entry.headSha, head) ? 'current' : 'stale'
  return 'current'
}

function same(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase()
}

function isReviewed(path: string): boolean {
  return reviewedPathSet.value.has(path)
}

function isStale(path: string): boolean {
  return stalePathSet.value.has(path)
}

function isCritical(path: string): boolean {
  return criticalPathSet.value.has(path)
}

function toggleCritical(path: string) {
  if (!details.value?.changedFiles.some(file => file.path === path)) return
  const selected = criticalPaths.value
  if (selected.includes(path)) saveReadingPath(selected.filter(item => item !== path))
  else if (selected.length < 10) saveReadingPath([...selected, path])
}

function moveCritical(path: string, offset: -1 | 1) {
  const selected = [...criticalPaths.value]
  const index = selected.indexOf(path)
  const target = index + offset
  if (index < 0 || target < 0 || target >= selected.length) return
  const moved = selected[index]!
  selected[index] = selected[target]!
  selected[target] = moved
  saveReadingPath(selected)
}

function acceptProposal() {
  if (!showProposal.value) return
  saveReadingPath(criticalProposal.value.map(file => file.path))
  proposalDismissed.value = true
}

// Optimistic with rollback, the same shape as setChecklistItem: the request id is captured
// without incrementing, because this is a mutation of the current generation, not a load.
async function saveReadingPath(paths: string[]) {
  if (!details.value) return
  const current = reviewRequestId
  const previous = readingPath.value
  const id = details.value.id
  const project = projectId.value
  const repository = repositoryId.value
  readingPath.value = paths
  fileReviewError.value = ''
  try {
    const result = await api.setReadingPath(project, repository, id, paths)
    if (current === reviewRequestId) readingPath.value = result.paths
  } catch (cause) {
    if (current === reviewRequestId) {
      readingPath.value = previous
      fileReviewError.value = message(cause)
    }
  }
}

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

async function setFileReviewed(path: string, reviewed: boolean) {
  if (!details.value) return
  const current = reviewRequestId
  const previous = fileReviews.value[path]
  const id = details.value.id
  const project = projectId.value
  const repository = repositoryId.value
  const file = details.value.changedFiles.find(entry => entry.path === path)
  const next = { ...fileReviews.value }
  if (reviewed) {
    next[path] = {
      path,
      blobId: file?.objectId ?? null,
      headSha: details.value.headCommitSha ?? null,
      updatedAt: new Date().toISOString(),
    }
  } else {
    delete next[path]
  }
  fileReviews.value = next
  fileReviewSaving.value = path
  fileReviewError.value = ''
  try {
    const result = await api.setFileReviewed(project, repository, id, {
      path,
      reviewed,
      blobId: file?.objectId ?? null,
      headCommitSha: details.value.headCommitSha ?? null,
      changedFilesCount: details.value.changedFilesCount,
    })
    if (current === reviewRequestId && result.entry) {
      fileReviews.value = { ...fileReviews.value, [path]: result.entry }
    }
  } catch (cause) {
    if (current === reviewRequestId) {
      const restored = { ...fileReviews.value }
      if (previous) restored[path] = previous
      else delete restored[path]
      fileReviews.value = restored
      fileReviewError.value = message(cause)
    }
  } finally {
    if (current === reviewRequestId) fileReviewSaving.value = null
  }
}

function resetDiff() {
  ++diffRequestId
  lastFilePath.value = ''
  selectedFilePath.value = ''
  fileDiff.value = null
  monacoComponent.value = null
  diffLoading.value = false
  diffError.value = ''
  resetExplanation()
  fileSearch.value = ''
  onlyUnreviewed.value = false
}

function resetSummary() {
  ++summaryRequestId
  summary.value = null
  summaryLoading.value = false
  summaryError.value = ''
  summaryReadLoading.value = false
  summaryReadError.value = ''
  summarySavedAt.value = null
  proposalDismissed.value = false
}

async function loadSavedSummary(project: string, repository: string, id: number) {
  const current = ++summaryRequestId
  summaryReadLoading.value = true
  summaryReadError.value = ''
  try {
    const stored = await api.savedSummary(project, repository, id)
    if (current === summaryRequestId && stored) {
      summary.value = stored.result
      summarySavedAt.value = stored.savedAt
    }
  } catch (cause) {
    if (current === summaryRequestId) summaryReadError.value = message(cause)
  } finally {
    if (current === summaryRequestId) summaryReadLoading.value = false
  }
}

function resetChecklist() {
  ++checklistRequestId
  checklist.value = null
  checklistLoading.value = false
  checklistSaving.value = null
  checklistError.value = ''
  debugAnswer.value = ''
  debugSaving.value = false
  debugSaved.value = false
  showDebugHint.value = false
}

function resetChecklistProgress() {
  checklistProgress.value = {}
  progressLoading.value = false
  progressError.value = ''
}

function resetFileReviews() {
  ++reviewRequestId
  fileReviews.value = {}
  readingPath.value = []
  fileReviewSaving.value = null
  fileReviewError.value = ''
}

function resetFileReviewProgress() {
  fileReviewProgress.value = {}
}

// Same shape as loadChecklist: capture the id, compare before every write including finally.
async function loadFileReviews(project: string, repository: string, id: number) {
  const current = ++reviewRequestId
  fileReviewError.value = ''
  try {
    const result = await api.fileReviews(project, repository, id)
    if (current !== reviewRequestId) return
    fileReviews.value = Object.fromEntries(result.files.map(entry => [entry.path, entry]))
    readingPath.value = result.readingPath
    resumeAtFirstUnread()
  } catch (cause) {
    if (current === reviewRequestId) fileReviewError.value = message(cause)
  }
}

// Picks up where the last session stopped instead of opening on an empty panel.
function resumeAtFirstUnread() {
  if (selectedFilePath.value || reviewedPaths.value.length === 0) return
  const next = nextUnreviewedPath.value
  if (next) void openFile(next)
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

async function loadChecklist(project: string, repository: string, id: number) {
  const current = ++checklistRequestId
  checklistLoading.value = true
  checklistError.value = ''
  try {
    const result = await api.checklist(project, repository, id)
    if (current === checklistRequestId) {
      checklist.value = result
      debugAnswer.value = result.debugNote ?? ''
    }
  } catch (cause) {
    if (current === checklistRequestId) checklistError.value = message(cause)
  } finally {
    if (current === checklistRequestId) checklistLoading.value = false
  }
}

// No optimistic write: this is the reviewer's own sentence, so what the field shows after
// a save is what the server stored, not what we hoped it stored.
async function saveDebugAnswer() {
  if (!details.value || debugSaving.value) return
  const current = checklistRequestId
  const project = projectId.value
  const repository = repositoryId.value
  const id = details.value.id
  debugSaving.value = true
  debugSaved.value = false
  checklistError.value = ''
  try {
    const result = await api.setDebugNote(project, repository, id, debugAnswer.value)
    if (current === checklistRequestId) {
      checklist.value = result
      debugAnswer.value = result.debugNote ?? ''
      debugSaved.value = true
    }
  } catch (cause) {
    if (current === checklistRequestId) checklistError.value = message(cause)
  } finally {
    if (current === checklistRequestId) debugSaving.value = false
  }
}

async function setChecklistItem(item: ChecklistItem, completed: boolean) {
  if (!details.value || !checklist.value || checklistSaving.value) return
  const current = checklistRequestId
  const project = projectId.value
  const repository = repositoryId.value
  const id = details.value.id
  const previous = checklist.value
  checklist.value = { ...previous, [item]: completed }
  checklistSaving.value = item
  checklistError.value = ''
  try {
    const result = await api.setChecklistItem(project, repository, id, item, completed)
    if (current === checklistRequestId) checklist.value = result
  } catch (cause) {
    if (current === checklistRequestId) {
      checklist.value = previous
      checklistError.value = message(cause)
    }
  } finally {
    if (current === checklistRequestId) checklistSaving.value = null
  }
}

function message(cause: unknown): string {
  return cause instanceof Error ? cause.message : 'Wystąpił nieoczekiwany błąd.'
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
  resetDiff()
  resetSummary()
  resetChecklist()
  resetChecklistProgress()
  resetFileReviews()
  resetFileReviewProgress()
  repositories.value = []
  repositoryId.value = ''
  pullRequests.value = []
  details.value = null
  error.value = ''
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
  resetDiff()
  resetSummary()
  resetChecklist()
  resetChecklistProgress()
  resetFileReviews()
  resetFileReviewProgress()
  pullRequests.value = []
  details.value = null
  error.value = ''
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
  resetDiff()
  resetSummary()
  resetChecklist()
  resetFileReviews()
  details.value = null
  error.value = ''
  loading.value = true
  try {
    const result = await api.pullRequest(projectId.value, repositoryId.value, id)
    if (current === requestId) {
      details.value = result
      void loadChecklist(projectId.value, repositoryId.value, id)
      void loadSavedSummary(projectId.value, repositoryId.value, id)
      void loadFileReviews(projectId.value, repositoryId.value, id)
    }
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

async function generateSummary() {
  if (!details.value || summaryLoading.value) return
  const current = ++summaryRequestId
  const id = details.value.id
  const project = projectId.value
  const repository = repositoryId.value
  summaryError.value = ''
  summaryReadError.value = ''
  summaryReadLoading.value = false
  summaryLoading.value = true
  try {
    const result = await api.generateSummary(project, repository, id)
    if (current === summaryRequestId) {
      summary.value = result
      summarySavedAt.value = null
    }
  } catch (cause) {
    if (current === summaryRequestId) summaryError.value = message(cause)
  } finally {
    if (current === summaryRequestId) summaryLoading.value = false
  }
}

function resetExplanation() {
  explanation.value = null
  explanationLoading.value = false
  explanationError.value = ''
}

// Rides the diff request id: switching files or leaving the PR invalidates an explanation
// still in flight, exactly like a late diff response.
async function explainFile() {
  if (!details.value || !selectedFilePath.value || explanationLoading.value) return
  const current = diffRequestId
  const pullRequestId = details.value.id
  const path = selectedFilePath.value
  explanationError.value = ''
  explanationLoading.value = true
  try {
    const result = await api.explainFile(projectId.value, repositoryId.value, pullRequestId, path)
    if (current !== diffRequestId) return
    explanation.value = result
  } catch (cause) {
    if (current === diffRequestId) explanationError.value = message(cause)
  } finally {
    if (current === diffRequestId) explanationLoading.value = false
  }
}

async function openFile(path: string) {
  if (!details.value) return
  const current = ++diffRequestId
  const pullRequestId = details.value.id
  selectedFilePath.value = path
  resetExplanation()
  fileDiff.value = null
  monacoComponent.value = null
  diffError.value = ''
  diffLoading.value = true
  await nextTick()
  if (current !== diffRequestId) return
  if (window.matchMedia('(max-width: 900px)').matches) {
    diffPanel.value?.scrollIntoView({ behavior: scrollBehavior(), block: 'start' })
  }
  try {
    const result = await api.fileDiff(projectId.value, repositoryId.value, pullRequestId, path)
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

function backToList() {
  ++requestId
  resetDiff()
  resetSummary()
  resetChecklist()
  resetFileReviews()
  details.value = null
  error.value = ''
  loading.value = false
  if (repositoryId.value && pullRequests.value.length > 0) {
    void loadChecklistProgress(requestId, projectId.value, repositoryId.value)
    void loadFileReviewProgress(requestId, projectId.value, repositoryId.value)
  }
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat('pl-PL', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function commitTitle(message: string): string {
  return message.split(/\r?\n/, 1)[0]?.trim() || 'Bez opisu'
}

function fileName(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1)
}

function fileDirectory(path: string): string {
  return path.slice(0, path.lastIndexOf('/') + 1)
}

function reviewerVote(vote: number): string {
  if (vote >= 5) return 'Zatwierdzono'
  if (vote < 0) return 'Zmiany wymagane'
  return 'Bez decyzji'
}

const changeLabels: Record<string, string> = {
  add: 'Dodano',
  edit: 'Zmieniono',
  delete: 'Usunięto',
  rename: 'Przeniesiono',
}

function changeLabel(changeType: string): string {
  return changeLabels[changeType.toLowerCase()] ?? changeType
}

const omissionLabels: Record<string, string> = {
  lockFile: 'plik zależności', snapshot: 'snapshot', generated: 'plik wygenerowany',
  minified: 'plik zminifikowany', buildOutput: 'wynik budowania', binary: 'plik binarny',
  sourceTooLarge: 'limit istniejącego diffu', fileCharacterLimit: 'limit na plik',
  pullRequestCharacterLimit: 'limit na PR',
}

function omissionLabel(reason: string): string {
  return omissionLabels[reason] ?? reason
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
  { keys: 'Esc', label: 'Zamknij pomoc albo wróć do listy' },
  { keys: '?', label: 'Ta pomoc' },
]

// A CSS prefers-reduced-motion block cannot override the JS `behavior` option, so the
// two smooth-scroll call sites have to ask for themselves.
function scrollBehavior(): ScrollBehavior {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth'
}

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
  if (!selectedFilePath.value) return
  if (!fileDiff.value && !isReviewed(selectedFilePath.value)) return
  if (!isReviewed(selectedFilePath.value)) toggleReviewed()
  openNextUnreviewed()
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
}

// Capture phase: Monaco stops propagation of the keys it owns, so a bubble-phase
// listener would never see them. Unmapped keys fall straight through to the editor,
// which is why arrows, PageUp/Down, Home/End and F-keys are deliberately absent.
function handleKey(event: KeyboardEvent) {
  if (event.ctrlKey || event.metaKey || event.altKey) return
  const target = event.target as HTMLElement | null
  if (target?.closest?.('input, textarea, select, [contenteditable="true"]')) return
  if (event.key === '?') {
    event.preventDefault()
    toggleHelp()
    return
  }
  if (!details.value) return
  if (event.key === 'Escape') {
    event.preventDefault()
    if (helpDialog.value?.open) closeHelp()
    else backToList()
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
</script>

<template>
  <div class="shell">
    <header class="topbar" :class="{ 'topbar--details': details }">
      <div class="brand"><span class="brand-mark">PR</span><span>Cockpit</span></div>
      <span class="topbar-caption">Azure DevOps / Pull Requests</span>
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
          <span class="pr-header-meta">{{ details.sourceBranch }} → {{ details.targetBranch }}</span>
          <span class="pr-header-meta">{{ details.author }}</span>
          <span v-if="checklist" class="pr-header-progress">Checklista {{ checklistCompleted }} / 6</span>
          <button class="shortcut-button" type="button" aria-label="Skróty klawiszowe" title="Skróty klawiszowe (?)"
            @click="toggleHelp">?</button>
        </div>

        <div class="pr-workspace" :class="{ 'pr-workspace--focus': focusMode }">
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
            <template v-if="selectedFilePath">
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
                  <button type="button" class="explain-button" title="Wyjaśnij ten plik (e)"
                    :disabled="explanationLoading" @click="explainFile">{{ explanationLoading ? 'Wyjaśniam…' : 'Wyjaśnij ten plik' }}</button>
                </div>
                <label class="review-check"><input type="checkbox" :checked="isReviewed(selectedFilePath)" :disabled="!fileDiff && !isReviewed(selectedFilePath)" @change="toggleReviewed"> Obejrzałem</label>
              </div>
              <p class="reading-status" aria-live="polite">{{ readingStatus }}</p>
              <p v-if="explanationError" class="notice error file-explanation-error" role="alert">{{ explanationError }}</p>
              <div v-if="explanation" class="file-explanation" aria-label="Wyjaśnienie pliku">
                <p v-for="(sentence, index) in explanation.sentences" :key="index">{{ sentence }}</p>
                <p class="file-explanation-note muted">Wyjaśnienie AI dla tej wersji pliku. Zapisane lokalnie.</p>
              </div>
              <p v-if="diffLoading" class="diff-message muted" role="status">Pobieranie diffu…</p>
              <p v-else-if="diffError" class="diff-message notice error" role="alert">{{ diffError }}</p>
              <p v-else-if="fileDiff?.kind === 'binary'" class="diff-message muted">Plik binarny — diff tekstowy jest niedostępny.</p>
              <p v-else-if="fileDiff?.kind === 'tooLarge'" class="diff-message muted">Plik jest zbyt duży, aby pokazać diff (limit 256 KB na wersję lub 4000 linii łącznie).</p>
              <component :is="monacoComponent" v-else-if="fileDiff?.kind === 'text' && monacoComponent" ref="diffView" :path="fileDiff.path"
                :original-path="fileDiff.originalPath" :original-text="fileDiff.originalText" :modified-text="fileDiff.modifiedText"
                :side-by-side="sideBySide" />
            </template>
            <div v-else class="pr-briefing">
              <button v-if="lastFilePath" class="briefing-back" type="button" title="Wróć do pliku (o)"
                @click="openFile(lastFilePath)">← Wróć do {{ lastFileName }}</button>
              <h3>O co chodzi w tym PR</h3>
              <p v-if="!details.description" class="muted">Brak opisu.</p>
              <div v-else class="description markdown-body" v-html="descriptionHtml" />
              <p class="diff-placeholder">Wybierz plik z listy, aby zobaczyć jego diff.</p>
            </div>
          </div>

          <aside class="context-rail" aria-label="Kontekst pull requesta">
            <div class="details-section summary-section">
              <div class="summary-heading"><div><h3>Summary</h3><p class="muted">Analiza korzysta z ograniczonego kontekstu PR i uruchamia się tylko po kliknięciu.</p></div>
                <button class="summary-button" type="button" :disabled="summaryLoading" @click="generateSummary">{{ summaryLoading ? 'Generowanie…' : summary ? 'Generuj ponownie' : 'Generuj Summary' }}</button>
              </div>
              <p v-if="summaryReadLoading" class="notice" role="status">Wczytywanie zapisanego Summary…</p>
              <div v-if="summaryReadError" class="notice error" role="alert">Nie udało się wczytać zapisanego Summary: {{ summaryReadError }} <button class="checklist-retry" type="button" :disabled="summaryLoading" @click="loadSavedSummary(projectId, repositoryId, details.id)">Spróbuj ponownie</button></div>
              <p v-if="summaryLoading" class="notice" role="status">Generowanie Summary…</p>
              <p v-if="summaryError" class="notice error" role="alert">{{ summaryError }}</p>
              <template v-if="summary">
                <div class="summary-meta"><span class="summary-saved">Zapisano lokalnie<template v-if="summarySavedAt"> · {{ formatDate(summarySavedAt) }}</template></span><span v-if="summaryFreshness === 'current'" class="summary-current">Aktualne dla tego PR</span></div>
                <p v-if="summaryFreshness === 'stale'" class="notice summary-stale" role="status">PR zmienił się od zapisania tego Summary. Wygeneruj je ponownie, aby uwzględnić aktualny commit.</p>
                <p v-else-if="summaryFreshness === 'unknown'" class="notice summary-stale" role="status">Nie można potwierdzić aktualności Summary, ponieważ brakuje SHA commita.</p>
                <div v-if="summary.sentences?.length" class="summary-text" aria-label="Podsumowanie PR"><p v-for="(sentence, index) in summary.sentences" :key="index" :class="{ 'summary-lead': index === 0 }">{{ sentence }}</p></div>
                <p v-else class="summary-text">{{ summary.summary }}</p>
                <p class="summary-report">Kontekst: {{ summary.contextReport.includedFiles }} / {{ summary.contextReport.changedFiles }} plików z diffem · {{ summary.contextReport.includedDiffCharacters }} znaków diffu<span v-if="summary.headCommitSha"> · commit {{ summary.headCommitSha.slice(0, 8) }}</span></p>
                <details v-if="summary.contextReport.wasLimited" class="summary-omissions">
                  <summary>Pominięto treść {{ summary.contextReport.omittedFiles.length }} plików</summary>
                  <ul><li v-for="file in summary.contextReport.omittedFiles" :key="file.path"><code>{{ file.path }}</code> — {{ omissionLabel(file.reason) }}</li></ul>
                </details>
              </template>
            </div>

            <details class="rail-block checklist-section" open>
              <summary>Checklista PR<span v-if="checklist"> · {{ checklistCompleted }} / 6</span></summary>
              <p class="muted">Zaznaczaj ręcznie po wykonaniu każdego kroku. Stan zapisuje się lokalnie.</p>
              <p v-if="checklistLoading" class="notice" role="status">Wczytywanie checklisty…</p>
              <p v-if="checklistError" class="notice error" role="alert">{{ checklistError }}</p>
              <div v-if="checklist" class="checklist-items">
                <label v-for="item in checklistItems" :key="item.key" class="checklist-item" :class="{ 'checklist-item--done': checklist[item.key] }">
                  <input type="checkbox" :checked="checklist[item.key]" :disabled="checklistSaving !== null" @change="setChecklistItem(item.key, ($event.target as HTMLInputElement).checked)">
                  <span>{{ item.label }}</span>
                </label>
              </div>
              <button v-else-if="!checklistLoading" class="checklist-retry" type="button" @click="loadChecklist(projectId, repositoryId, details.id)">Spróbuj ponownie</button>
            </details>

            <details class="rail-block debug-check" :open="remainingCount === 0">
              <summary>Debug Check</summary>
              <p class="debug-question">Gdyby ta zmiana nie zadziałała, gdzie zacząłbyś szukać?</p>
              <p v-if="remainingCount > 0" class="muted debug-hint-later">Pytanie ma sens po przeczytaniu PR — zostało {{ remainingCount }} {{ remainingCount === 1 ? 'plik' : 'plików' }}.</p>
              <label class="debug-label" for="debug-answer">Twoja odpowiedź</label>
              <textarea id="debug-answer" v-model="debugAnswer" class="debug-answer" rows="3"
                :maxlength="2000" placeholder="Jedno zdanie wystarczy."></textarea>
              <div class="debug-actions">
                <button type="button" class="debug-save" :disabled="debugSaving" @click="saveDebugAnswer">{{ debugSaving ? 'Zapisywanie…' : 'Zapisz' }}</button>
                <button v-if="criticalProposal.length > 0" type="button" @click="showDebugHint = !showDebugHint">{{ showDebugHint ? 'Ukryj podpowiedź' : 'Pokaż, gdzie patrzeć' }}</button>
                <span v-if="debugSaved" class="debug-saved" role="status">Zapisano</span>
              </div>
              <!-- "Show me" reuses the ranking the Summary already returned — no second
                   model run, and nothing here is scored against your answer. -->
              <ol v-if="showDebugHint" class="debug-hint" aria-label="Podpowiedź">
                <li v-for="file in criticalProposal" :key="file.path"><code>{{ file.path }}</code> — {{ file.why }}</li>
              </ol>
            </details>

            <details class="rail-block critical-section" :open="criticalPaths.length > 0 || showProposal">
              <summary>Ścieżka kluczowych plików<span> · {{ criticalPaths.length }} / 10</span></summary>
              <p class="muted">Wybierz do 10 plików i ustaw kolejność czytania. Zapisuje się lokalnie.</p>
              <div v-if="showProposal" class="critical-proposal">
                <p class="critical-proposal-heading">Propozycja AI ({{ criticalProposal.length }})</p>
                <ol class="critical-proposal-list" aria-label="Propozycja ścieżki czytania">
                  <li v-for="file in criticalProposal" :key="file.path">
                    <button class="critical-open" type="button" :title="file.path" @click="openCriticalFile(file.path)">{{ file.path }}</button>
                    <p class="critical-proposal-why">{{ file.why }}</p>
                  </li>
                </ol>
                <div class="critical-proposal-actions">
                  <button type="button" class="critical-accept" @click="acceptProposal">Przyjmij ścieżkę</button>
                  <button type="button" @click="proposalDismissed = true">Odrzuć</button>
                </div>
              </div>
              <p v-else-if="criticalPaths.length === 0" class="muted critical-empty">Dodaj pliki z listy zmian po lewej.</p>
              <ol v-else class="critical-list" aria-label="Ścieżka kluczowych plików">
                <li v-for="(path, index) in criticalPaths" :key="path">
                  <button class="critical-open" type="button" :title="path" @click="openCriticalFile(path)"><span class="critical-order">{{ index + 1 }}</span><span>{{ path }}</span></button>
                  <div class="critical-actions">
                    <button type="button" :disabled="index === 0" :aria-label="`Przesuń ${path} w górę`" @click="moveCritical(path, -1)">↑</button>
                    <button type="button" :disabled="index === criticalPaths.length - 1" :aria-label="`Przesuń ${path} w dół`" @click="moveCritical(path, 1)">↓</button>
                    <button type="button" :aria-label="`Usuń ${path} ze ścieżki`" @click="toggleCritical(path)">Usuń</button>
                  </div>
                </li>
              </ol>
            </details>

            <details class="rail-block">
              <summary>Commity ({{ details.commitsCount }})</summary>
              <p v-if="details.commits.length === 0" class="muted">Brak commitów.</p>
              <ul v-else class="commit-list">
                <li v-for="commit in details.commits" :key="commit.id">
                  <code class="commit-id" :title="commit.id">{{ commit.id.slice(0, 8) }}</code>
                  <span class="commit-info"><strong>{{ commitTitle(commit.message) }}</strong><small>{{ commit.author }}<template v-if="commit.authoredAt"> · {{ formatDate(commit.authoredAt) }}</template></small></span>
                </li>
              </ul>
            </details>

            <details class="rail-block">
              <summary>Szczegóły PR</summary>
              <div class="metadata">
                <div><span>Autor</span><strong>{{ details.author }}</strong></div>
                <div><span>Repozytorium</span><strong>{{ details.repository }}</strong></div>
                <div><span>Utworzono</span><strong>{{ formatDate(details.createdAt) }}</strong></div>
                <div><span>Gałąź źródłowa</span><strong>{{ details.sourceBranch }}</strong></div>
                <div><span>Gałąź docelowa</span><strong>{{ details.targetBranch }}</strong></div>
                <div><span>Zmienione pliki</span><strong>{{ details.changedFilesCount }}</strong></div>
              </div>
              <h4>Reviewerzy</h4>
              <p v-if="details.reviewers.length === 0" class="muted">Brak reviewerów.</p>
              <ul v-else class="plain-list"><li v-for="reviewer in details.reviewers" :key="reviewer.name">{{ reviewer.name }} <span class="muted">· {{ reviewerVote(reviewer.vote) }}</span></li></ul>
              <h4>Powiązane Work Items</h4>
              <p v-if="details.workItems.length === 0" class="muted">Brak powiązanych Work Items.</p>
              <ul v-else class="plain-list"><li v-for="item in details.workItems" :key="item.id">#{{ item.id }}</li></ul>
            </details>
          </aside>
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

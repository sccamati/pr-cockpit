import { computed, ref } from 'vue'
import { api, type FileReviewEntry } from './api'
import type { PrScope } from './cockpit'
import { message, same } from './format'
import { criticalFileLimit } from './useSummary'

/**
 * The per-file "Obejrzałem" markers and the stored reading path, with where a walkthrough
 * stands on it. Server-backed and therefore per-PR by construction: loaded when a PR
 * opens, cleared when it closes.
 */
export function useReviewProgress({ details, projectId, repositoryId, onLoaded }: PrScope & {
  // Runs inside the stale-response guard once the markers are in, because some decisions
  // (the second pass, resuming at the first unread file) can only be made from them.
  onLoaded: () => void
}) {
  const fileReviews = ref<Record<string, FileReviewEntry>>({})
  const readingPath = ref<string[]>([])
  const walkPosition = ref(0)
  // The pull request head at the moment the path was chosen. Null means the path was built
  // in the rail rather than by a walkthrough, and no resume is offered for it.
  const walkHeadSha = ref<string | null>(null)
  const fileReviewSaving = ref<string | null>(null)
  const fileReviewError = ref('')
  let reviewRequestId = 0

  // One lookup per path instead of a scan of the change list: reviewState runs once per
  // file inside several computeds, which made a 2000-file PR quadratic on every mark.
  const changedFileByPath = computed(() =>
    new Map((details.value?.changedFiles ?? []).map(file => [file.path, file])))

  // A marker goes stale only on positive evidence that the file changed: a blob id that no
  // longer matches, or a head SHA that moved. With nothing to compare against we keep the
  // mark, because nagging without cause is worse than a slightly optimistic tick.
  // The same rule lives in the backend as ContentFreshness (Domain), for the saved
  // explanation and the question history.
  // ponytail: the head SHA fallback is per-PR, so when Azure DevOps omits a blob id any new
  // commit marks that file stale. Precise per-file tracking would need iteration diffing.
  function reviewState(path: string): 'none' | 'current' | 'stale' {
    const entry = fileReviews.value[path]
    if (!entry) return 'none'
    const objectId = changedFileByPath.value.get(path)?.objectId
    if (entry.blobId && objectId) return same(entry.blobId, objectId) ? 'current' : 'stale'
    const head = details.value?.headCommitSha
    if (entry.headSha && head) return same(entry.headSha, head) ? 'current' : 'stale'
    return 'current'
  }

  const reviewedPaths = computed(() =>
    (details.value?.changedFiles ?? []).filter(file => reviewState(file.path) === 'current').map(file => file.path))
  const stalePaths = computed(() =>
    (details.value?.changedFiles ?? []).filter(file => reviewState(file.path) === 'stale').map(file => file.path))
  const reviewedPathSet = computed(() => new Set(reviewedPaths.value))
  const stalePathSet = computed(() => new Set(stalePaths.value))
  const remainingCount = computed(() => (details.value?.changedFiles.length ?? 0) - reviewedPaths.value.length)
  // The stored path, filtered to files that are still in the pull request, so a file
  // dropped by a new iteration leaves no dead row (US-P4).
  const criticalPaths = computed(() => readingPath.value.filter(path => changedFileByPath.value.has(path)))
  const criticalPathSet = computed(() => new Set(criticalPaths.value))
  // What the rail lets you add by hand — the shortlist rule, not the length of the stored
  // path, which a walkthrough of the whole pull request makes as long as the pull request.
  const manualCriticalLimit = computed(() => criticalFileLimit(details.value?.changedFiles.length ?? 0))

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
    if (!changedFileByPath.value.has(path)) return
    const selected = criticalPaths.value
    if (selected.includes(path)) saveReadingPath(selected.filter(item => item !== path))
    else if (selected.length < manualCriticalLimit.value) {
      saveReadingPath([...selected, path])
    }
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

  // Optimistic with rollback, the same shape as setChecklistItem: the request id is captured
  // without incrementing, because this is a mutation of the current generation, not a load.
  // The position travels with the path: editing the path from the rail is a new path, so it
  // starts from zero, while the walkthrough passes the place it has reached (US-P7).
  async function saveReadingPath(paths: string[], position = 0) {
    if (!details.value) return
    const current = reviewRequestId
    const previous = readingPath.value
    const id = details.value.id
    const project = projectId.value
    const repository = repositoryId.value
    readingPath.value = paths
    fileReviewError.value = ''
    try {
      const result = await api.setReadingPath(project, repository, id, paths, position, walkHeadSha.value)
      if (current === reviewRequestId) readingPath.value = result.paths
    } catch (cause) {
      if (current === reviewRequestId) {
        readingPath.value = previous
        fileReviewError.value = message(cause)
      }
    }
  }

  async function setFileReviewed(path: string, reviewed: boolean) {
    if (!details.value) return
    const current = reviewRequestId
    const previous = fileReviews.value[path]
    const id = details.value.id
    const project = projectId.value
    const repository = repositoryId.value
    const file = changedFileByPath.value.get(path)
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

  function resetFileReviews() {
    ++reviewRequestId
    fileReviews.value = {}
    readingPath.value = []
    walkPosition.value = 0
    walkHeadSha.value = null
    fileReviewSaving.value = null
    fileReviewError.value = ''
  }

  // Same shape as loadChecklist: capture the id, compare before every write including finally.
  async function loadFileReviews(project: string, repository: string, id: number) {
    const current = ++reviewRequestId
    fileReviewError.value = ''
    try {
      const result = await api.fileReviews(project, repository, id)
      if (current !== reviewRequestId) return
      fileReviews.value = Object.fromEntries(result.files.map(entry => [entry.path, entry]))
      readingPath.value = result.readingPath.paths
      walkPosition.value = result.readingPath.position
      walkHeadSha.value = result.readingPath.headCommitSha
      onLoaded()
    } catch (cause) {
      if (current === reviewRequestId) fileReviewError.value = message(cause)
    }
  }

  return {
    fileReviews, readingPath, walkPosition, walkHeadSha, fileReviewSaving, fileReviewError,
    changedFileByPath, reviewedPaths, stalePaths, remainingCount, criticalPaths, criticalPathSet,
    manualCriticalLimit, reviewState, isReviewed, isStale, isCritical,
    toggleCritical, moveCritical, saveReadingPath, setFileReviewed, resetFileReviews, loadFileReviews,
  }
}

import { computed, ref, type Ref } from 'vue'
import { api, type ChangedFile } from '@/api'
import type { PrScope } from '@/cockpit'
import { changeLabel, fileName, message } from '@/lib/format'
import type { useSummary } from '@/features/context/useSummary'
import type { useReviewProgress } from '@/features/walkthrough/useReviewProgress'
import { buildFileTree, flattenTree, type TreeFile } from './fileTree'

/**
 * The changed files as the tree shows them: search, the "unreviewed" switch, the
 * "changes since update N" filter, and the order that "next" means everywhere.
 */
export function useFileList({ details, projectId, repositoryId, selectedFilePath, summary, review, threadsByFile }: PrScope & {
  selectedFilePath: Ref<string>
  summary: ReturnType<typeof useSummary>
  review: ReturnType<typeof useReviewProgress>
  // A getter, because the comments feature is created after this one and needs its order.
  threadsByFile: () => Map<string, { total: number; unresolved: number }>
}) {
  const { roleByPath } = summary
  const { criticalPaths, manualCriticalLimit, isReviewed, isStale, isCritical } = review

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
    const threads = threadsByFile().get(file.path)
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
      comments: threads?.total ?? 0,
      unresolvedComments: threads?.unresolved ?? 0,
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

  function resetFileList() {
    fileSearch.value = ''
    onlyUnreviewed.value = false
    ++filterRequestId
    filterIteration.value = null
    filterPaths.value = null
    filterLoading.value = false
    filterError.value = ''
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

  return {
    fileSearch, onlyUnreviewed, filterIteration, filterPaths, filterLoading, filterError,
    lastIteration, matchingFiles, unreviewedMatches, noiseFiles, codeFiles, expandAll, fileTree, noiseTree,
    orderedPaths, hasPreviousFile, hasNextFile, nextUnreviewedPath, iterationChoices,
    resetFileList, setFilterIteration,
  }
}

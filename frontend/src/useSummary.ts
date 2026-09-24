import { computed, ref } from 'vue'
import { api, type SummaryResponse } from './api'
import type { PrScope } from './cockpit'
import { message } from './format'

// ponytail: the brief calls this configuration. A one-person local tool has no settings
// file, so it is a named rule here; a settings screen is the upgrade path.
// Ten files is enough to open a pull request of twenty and far too few to open one of
// eighty — at that size a ranking of ten is a sample, not a starting point, so the ranking
// grows with the change. It is the same rule the backend applies
// (SummaryContract.CriticalFileLimit); the walkthrough's path grows more slowly
// (walkPathLength in useWalkthrough.ts).
export function criticalFileLimit(changedFiles: number): number {
  return Math.min(25, Math.max(10, Math.ceil(changedFiles / 4)))
}

/** The saved or freshly generated whole-PR Summary, and the AI ranking it carries. */
export function useSummary({ details, projectId, repositoryId }: PrScope) {
  const summary = ref<SummaryResponse | null>(null)
  const summaryLoading = ref(false)
  const summaryError = ref('')
  const summaryReadLoading = ref(false)
  const summaryReadError = ref('')
  const summarySavedAt = ref<string | null>(null)
  // The AI ranking, kept apart from readingPath: PRODUCT.md §10 — a proposal is not a fact
  // until the user accepts it, so nothing is written to the reading path behind their back.
  const proposalDismissed = ref(false)
  let summaryRequestId = 0

  const summaryFreshness = computed(() => {
    const savedSha = summary.value?.headCommitSha
    const currentSha = details.value?.headCommitSha
    if (!savedSha || !currentSha) return 'unknown'
    return savedSha.toLowerCase() === currentSha.toLowerCase() ? 'current' : 'stale'
  })

  const criticalProposal = computed(() => {
    // US-P3: a file classified as noise never enters the AI proposal. It can still be added
    // by hand from the tree, which is the whole difference between a proposal and a rule.
    const paths = new Map((details.value?.changedFiles ?? []).map(file => [file.path, file]))
    return (summary.value?.criticalFiles ?? [])
      .filter(file => paths.get(file.path) && !paths.get(file.path)!.category)
      .slice(0, criticalFileLimit(details.value?.changedFiles.length ?? 0))
  })
  // The other half of the same ranking: every file of the pull request, in the order the AI
  // put them in, noise last because the backend sank it there. Noise is not filtered out the
  // way it is from the shortlist — "wszystkie pliki" means all of them, and a lockfile at the
  // end is one tick, not a detour. Empty when the saved Summary predates the field.
  const fullProposal = computed(() => {
    const paths = new Set((details.value?.changedFiles ?? []).map(file => file.path))
    return (summary.value?.readingOrder ?? []).filter(path => paths.has(path))
  })
  const roleByPath = computed(() => new Map(criticalProposal.value.map(file => [file.path, file.role])))

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
      if (current !== summaryRequestId) return
      if (stored) {
        summary.value = stored.result
        summarySavedAt.value = stored.savedAt
      }
    } catch (cause) {
      if (current === summaryRequestId) summaryReadError.value = message(cause)
    } finally {
      if (current === summaryRequestId) summaryReadLoading.value = false
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

  return {
    summary, summaryLoading, summaryError, summaryReadLoading, summaryReadError, summarySavedAt,
    proposalDismissed, summaryFreshness, criticalProposal, fullProposal, roleByPath,
    resetSummary, loadSavedSummary, generateSummary,
  }
}

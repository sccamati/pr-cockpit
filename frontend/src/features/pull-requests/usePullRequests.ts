import { ref } from 'vue'
import { api, type Project, type PullRequestDetails, type PullRequestSummary, type Repository } from '@/api'
import { message } from '@/lib/format'

/**
 * Picking a project, a repository and a pull request, the list with its progress counters,
 * and opening one pull request. One request counter guards all of it, so switching the
 * project also drops a pull request that was still opening.
 */
export function usePullRequests({ onReset, onOpened }: {
  // Clears everything that belongs to the open pull request (App's resetPullRequest).
  onReset: () => void
  // Runs inside the stale-response guard once the details are in.
  onOpened: (project: string, repository: string, id: number) => void
}) {
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

  function resetPullRequest() {
    onReset()
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

  function retryChecklistProgress() {
    void loadChecklistProgress(requestId, projectId.value, repositoryId.value)
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
        onOpened(projectId.value, repositoryId.value, id)
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

  return {
    projects, repositories, pullRequests, details, projectId, repositoryId, loading, error,
    checklistProgress, progressLoading, progressError, fileReviewProgress,
    loadProjects, loadRepositories, loadPullRequests, openPullRequest, backToList, retryChecklistProgress,
  }
}

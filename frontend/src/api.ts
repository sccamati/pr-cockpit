export interface Project { id: string; name: string }
export interface Repository { id: string; name: string }
export interface Reviewer { name: string; vote: number }
export interface WorkItem { id: string; url: string }
export interface ChangedFile { path: string; changeType: string; originalPath: string | null }
interface FileDiffBase { path: string; originalPath: string | null }
export type FileDiff = FileDiffBase & (
  { kind: 'text'; originalText: string; modifiedText: string } |
  { kind: 'binary' | 'tooLarge'; originalText: null; modifiedText: null }
)

export interface PullRequestSummary {
  id: number
  title: string
  author: string
  repository: string
  status: string
  createdAt: string
}

export interface PullRequestDetails extends PullRequestSummary {
  description: string | null
  sourceBranch: string
  targetBranch: string
  reviewers: Reviewer[]
  changedFilesCount: number
  changedFiles: ChangedFile[]
  commitsCount: number
  workItems: WorkItem[]
}

async function get<T>(path: string): Promise<T> {
  const response = await fetch(`/api${path}`)
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string } | null
    throw new Error(problem?.detail ?? `Żądanie nie powiodło się (${response.status}).`)
  }
  return response.json() as Promise<T>
}

const location = (project: string, repository: string) =>
  `/projects/${encodeURIComponent(project)}/repositories/${encodeURIComponent(repository)}`

export const api = {
  projects: () => get<Project[]>('/projects'),
  repositories: (project: string) => get<Repository[]>(`/projects/${encodeURIComponent(project)}/repositories`),
  pullRequests: (project: string, repository: string) =>
    get<PullRequestSummary[]>(`${location(project, repository)}/pull-requests`),
  pullRequest: (project: string, repository: string, id: number) =>
    get<PullRequestDetails>(`${location(project, repository)}/pull-requests/${id}`),
  fileDiff: (project: string, repository: string, id: number, path: string) =>
    get<FileDiff>(`${location(project, repository)}/pull-requests/${id}/diff?path=${encodeURIComponent(path)}`),
}

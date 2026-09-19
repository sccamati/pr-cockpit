export interface Project { id: string; name: string }
export interface Repository { id: string; name: string }
export interface Reviewer { name: string; vote: number }
export interface WorkItem { id: string; url: string }
export interface ChangedFile { path: string; changeType: string; originalPath: string | null }
export interface Commit { id: string; message: string; author: string; authoredAt: string | null }
interface FileDiffBase { path: string; originalPath: string | null }
export type FileDiff = FileDiffBase & (
  { kind: 'text'; originalText: string; modifiedText: string } |
  { kind: 'binary' | 'tooLarge'; originalText: null; modifiedText: null }
)
export interface CSharpHoverEntry {
  startLine: number
  startColumn: number
  endLine: number
  endColumn: number
  signature: string
}
export interface CSharpSemanticToken {
  line: number
  startColumn: number
  endColumn: number
  kind: string
}
export interface CSharpHovers {
  original: CSharpHoverEntry[]
  modified: CSharpHoverEntry[]
  originalTokens: CSharpSemanticToken[]
  modifiedTokens: CSharpSemanticToken[]
}

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
  commits: Commit[]
  workItems: WorkItem[]
  baseCommitSha?: string | null
  headCommitSha?: string | null
}

export interface SummaryResponse {
  schemaVersion: number
  summary: string
  sentences: string[]
  baseCommitSha: string | null
  headCommitSha: string | null
  contextReport: {
    changedFiles: number
    includedFiles: number
    includedDiffCharacters: number
    wasLimited: boolean
    omittedFiles: { path: string; reason: string }[]
  }
}
export interface StoredSummary { result: SummaryResponse; savedAt: string }

export type ChecklistItem = 'aiReview' | 'quality' | 'understand' | 'architecture' | 'debug' | 'ready'
export interface ChecklistState extends Record<ChecklistItem, boolean> { updatedAt: string | null }
export interface ChecklistProgress { pullRequestId: number; completedCount: number }

async function get<T>(path: string): Promise<T> {
  const response = await fetch(`/api${path}`)
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string } | null
    throw new Error(problem?.detail ?? `Żądanie nie powiodło się (${response.status}).`)
  }
  return response.json() as Promise<T>
}

async function post<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method: 'POST',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string } | null
    throw new Error(problem?.detail ?? `Żądanie nie powiodło się (${response.status}).`)
  }
  return response.json() as Promise<T>
}

async function put<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
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
  checklistProgress: (project: string, repository: string) =>
    get<ChecklistProgress[]>(`${location(project, repository)}/pull-requests/checklist-progress`),
  pullRequest: (project: string, repository: string, id: number) =>
    get<PullRequestDetails>(`${location(project, repository)}/pull-requests/${id}`),
  fileDiff: (project: string, repository: string, id: number, path: string) =>
    get<FileDiff>(`${location(project, repository)}/pull-requests/${id}/diff?path=${encodeURIComponent(path)}`),
  csharpHovers: (originalText: string, modifiedText: string, signal?: AbortSignal) =>
    post<CSharpHovers>('/csharp/hovers', { originalText, modifiedText }, signal),
  generateSummary: (project: string, repository: string, id: number) =>
    post<SummaryResponse>(`${location(project, repository)}/pull-requests/${id}/summary`),
  savedSummary: (project: string, repository: string, id: number) =>
    get<{ stored: StoredSummary | null }>(`${location(project, repository)}/pull-requests/${id}/summary`)
      .then(response => response.stored),
  checklist: (project: string, repository: string, id: number) =>
    get<ChecklistState>(`${location(project, repository)}/pull-requests/${id}/checklist`),
  setChecklistItem: (project: string, repository: string, id: number, item: ChecklistItem, completed: boolean) =>
    put<ChecklistState>(`${location(project, repository)}/pull-requests/${id}/checklist/${item === 'aiReview' ? 'ai-review' : item}`, { completed }),
}

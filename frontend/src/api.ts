export interface Project { id: string; name: string }
export interface Repository { id: string; name: string }
export interface Reviewer { name: string; vote: number }
export interface WorkItem { id: string; url: string }
// category is computed by the backend: null for ordinary code, otherwise the kind of
// noise (lockFile, snapshot, generated, minified, buildOutput).
export interface ChangedFile { path: string; changeType: string; originalPath: string | null; objectId?: string | null; category?: string | null }
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

// Everything except id is optional on purpose — the backend maps Azure DevOps threads
// tolerantly, because a system thread or a deleted comment must not break the list.
export interface PrComment {
  id: number
  author: string | null
  content: string | null
  commentType: string | null
  publishedAt: string | null
  authorId: string | null
  // Azure DevOps only allows editing and deleting your own comment.
  isMine: boolean
}
// Writing comments is off unless the backend says otherwise, so the UI asks once instead
// of finding out from a 503 after the comment has been typed.
export interface AppConfig { commentsEnabled: boolean }

export interface PrCommentThread {
  id: number
  status: string | null
  filePath: string | null
  rightLine: number | null
  leftLine: number | null
  isSystem: boolean
  comments: PrComment[]
  // The pull request iteration the comment was written against, when it has one.
  iterationId: number | null
}
export interface PrIteration { id: number; sourceCommitSha: string | null }

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
  iterations?: PrIteration[] | null
}

export interface CriticalFile { path: string; role: string; why: string }

export interface SummaryResponse {
  schemaVersion: number
  summary: string
  sentences: string[]
  criticalFiles: CriticalFile[]
  // The whole pull request in reading order, noise last. Missing on a Summary generated
  // before the field existed, which is exactly what the full walkthrough checks for.
  readingOrder?: string[] | null
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

export interface FileExplanation {
  schemaVersion: number
  path: string
  headCommitSha: string | null
  sentences: string[]
}

export interface FileReviewEntry { path: string; blobId: string | null; headSha: string | null; updatedAt: string }
export interface FileReviewState {
  files: FileReviewEntry[]
  readingPath: ReadingPathState
  updatedAt: string | null
}
export interface FileReviewUpdate {
  path: string
  reviewed: boolean
  blobId?: string | null
  headCommitSha?: string | null
  changedFilesCount?: number
}
export interface FileReviewResult { entry: FileReviewEntry | null; reviewedCount: number }
// The reading path plus where the walkthrough of it stopped (US-P7). position === paths.length
// means it was walked to the end, so there is nothing to offer resuming.
export interface ReadingPathState {
  paths: string[]
  position: number
  headCommitSha: string | null
  updatedAt: string | null
}
export interface FileReviewProgress { pullRequestId: number; reviewedCount: number; changedFilesCount: number }

export type ChecklistItem = 'aiReview' | 'quality' | 'understand' | 'architecture' | 'debug' | 'ready'
export interface ChecklistState extends Record<ChecklistItem, boolean> { updatedAt: string | null; debugNote: string | null }
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

async function patch<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string } | null
    throw new Error(problem?.detail ?? `Żądanie nie powiodło się (${response.status}).`)
  }
  return response.json() as Promise<T>
}

async function del<T>(path: string): Promise<T> {
  const response = await fetch(`/api${path}`, { method: 'DELETE' })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string } | null
    throw new Error(problem?.detail ?? `Żądanie nie powiodło się (${response.status}).`)
  }
  return response.json() as Promise<T>
}

const location = (project: string, repository: string) =>
  `/projects/${encodeURIComponent(project)}/repositories/${encodeURIComponent(repository)}`

export const api = {
  config: () => get<AppConfig>('/config'),
  projects: () => get<Project[]>('/projects'),
  repositories: (project: string) => get<Repository[]>(`/projects/${encodeURIComponent(project)}/repositories`),
  pullRequests: (project: string, repository: string) =>
    get<PullRequestSummary[]>(`${location(project, repository)}/pull-requests`),
  checklistProgress: (project: string, repository: string) =>
    get<ChecklistProgress[]>(`${location(project, repository)}/pull-requests/checklist-progress`),
  pullRequest: (project: string, repository: string, id: number) =>
    get<PullRequestDetails>(`${location(project, repository)}/pull-requests/${id}`),
  fileDiff: (project: string, repository: string, id: number, path: string, sinceIteration?: number) =>
    get<FileDiff>(`${location(project, repository)}/pull-requests/${id}/diff?path=${encodeURIComponent(path)}` +
      (sinceIteration ? `&sinceIteration=${sinceIteration}` : '')),
  // Which files an iteration and everything after it touched — the list behind the
  // "changes since update N" filter. Paths only; the rows themselves are already here.
  changedPathsSince: (project: string, repository: string, id: number, sinceIteration: number) =>
    get<string[]>(`${location(project, repository)}/pull-requests/${id}/changed-paths?sinceIteration=${sinceIteration}`),
  commentThreads: (project: string, repository: string, id: number) =>
    get<PrCommentThread[]>(`${location(project, repository)}/pull-requests/${id}/threads`),
  createThread: (project: string, repository: string, id: number, thread: { content: string; filePath: string | null; line: number | null }) =>
    post<PrCommentThread>(`${location(project, repository)}/pull-requests/${id}/threads`, thread),
  replyToThread: (project: string, repository: string, id: number, threadId: number, content: string) =>
    post<PrCommentThread>(`${location(project, repository)}/pull-requests/${id}/threads/${threadId}/comments`, { content }),
  editComment: (project: string, repository: string, id: number, threadId: number, commentId: number, content: string) =>
    patch<PrCommentThread>(`${location(project, repository)}/pull-requests/${id}/threads/${threadId}/comments/${commentId}`, { content }),
  deleteComment: (project: string, repository: string, id: number, threadId: number, commentId: number) =>
    del<PrCommentThread>(`${location(project, repository)}/pull-requests/${id}/threads/${threadId}/comments/${commentId}`),
  setThreadStatus: (project: string, repository: string, id: number, threadId: number, status: string) =>
    patch<PrCommentThread>(`${location(project, repository)}/pull-requests/${id}/threads/${threadId}`, { status }),
  csharpHovers: (originalText: string, modifiedText: string, signal?: AbortSignal) =>
    post<CSharpHovers>('/csharp/hovers', { originalText, modifiedText }, signal),
  generateSummary: (project: string, repository: string, id: number) =>
    post<SummaryResponse>(`${location(project, repository)}/pull-requests/${id}/summary`),
  explainFile: (project: string, repository: string, id: number, path: string, signal?: AbortSignal) =>
    post<FileExplanation>(`${location(project, repository)}/pull-requests/${id}/summary/file`, { path }, signal),
  savedSummary: (project: string, repository: string, id: number) =>
    get<{ stored: StoredSummary | null }>(`${location(project, repository)}/pull-requests/${id}/summary`)
      .then(response => response.stored),
  checklist: (project: string, repository: string, id: number) =>
    get<ChecklistState>(`${location(project, repository)}/pull-requests/${id}/checklist`),
  fileReviews: (project: string, repository: string, id: number) =>
    get<FileReviewState>(`${location(project, repository)}/pull-requests/${id}/file-reviews`),
  setFileReviewed: (project: string, repository: string, id: number, update: FileReviewUpdate) =>
    put<FileReviewResult>(`${location(project, repository)}/pull-requests/${id}/file-reviews`, update),
  setReadingPath: (project: string, repository: string, id: number, paths: string[],
    position?: number, headCommitSha?: string | null) =>
    put<ReadingPathState>(`${location(project, repository)}/pull-requests/${id}/reading-path`,
      { paths, position, headCommitSha }),
  fileReviewProgress: (project: string, repository: string) =>
    get<FileReviewProgress[]>(`${location(project, repository)}/pull-requests/file-review-progress`),
  setDebugNote: (project: string, repository: string, id: number, note: string) =>
    put<ChecklistState>(`${location(project, repository)}/pull-requests/${id}/checklist/debug-note`, { note }),
  setChecklistItem: (project: string, repository: string, id: number, item: ChecklistItem, completed: boolean) =>
    put<ChecklistState>(`${location(project, repository)}/pull-requests/${id}/checklist/${item === 'aiReview' ? 'ai-review' : item}`, { completed }),
}

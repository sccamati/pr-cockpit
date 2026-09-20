// @vitest-environment jsdom
import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../src/App.vue'

const api = vi.hoisted(() => ({
  projects: vi.fn(),
  repositories: vi.fn(),
  pullRequests: vi.fn(),
  checklistProgress: vi.fn(),
  pullRequest: vi.fn(),
  fileDiff: vi.fn(),
  generateSummary: vi.fn(),
  explainFile: vi.fn(),
  savedSummary: vi.fn(),
  checklist: vi.fn(),
  fileReviews: vi.fn(),
  setFileReviewed: vi.fn(),
  setReadingPath: vi.fn(),
  setDebugNote: vi.fn(),
  fileReviewProgress: vi.fn(),
  commentThreads: vi.fn(),
  createThread: vi.fn(),
  replyToThread: vi.fn(),
  setThreadStatus: vi.fn(),
}))

vi.mock('../src/api', () => ({ api }))
vi.mock('../src/MonacoDiff.vue', () => ({
  default: {
    name: 'MonacoDiff',
    props: ['path', 'originalPath', 'originalText', 'modifiedText', 'sideBySide', 'commentLines'],
    emits: ['openLine'],
    template: '<div class="test-diff">Diff</div>',
  },
}))

const details = {
  id: 123,
  title: 'A long pull request title',
  author: 'Anna',
  repository: 'Repo A',
  status: 'active',
  createdAt: '2026-09-01T12:00:00Z',
  description: 'Description',
  sourceBranch: 'feature',
  targetBranch: 'main',
  reviewers: [],
  changedFilesCount: 3,
  changedFiles: [
    { path: '/src/first.cs', changeType: 'edit', originalPath: null },
    { path: '/src/second.cs', changeType: 'add', originalPath: null },
    { path: '/tests/third.cs', changeType: 'edit', originalPath: null },
  ],
  commitsCount: 2,
  commits: [
    { id: 'aaaaaaaaa', message: 'First change\nBody', author: 'Anna', authoredAt: '2026-09-01T12:00:00Z' },
    { id: 'bbbbbbbbb', message: 'Add tests', author: 'Jan', authoredAt: null },
  ],
  workItems: [],
}

beforeEach(() => {
  vi.resetAllMocks()
  api.projects.mockResolvedValue([{ id: 'project', name: 'Project' }])
  api.repositories.mockResolvedValue([{ id: 'repo-a', name: 'Repo A' }])
  api.pullRequests.mockResolvedValue([details])
  api.checklistProgress.mockResolvedValue([])
  api.pullRequest.mockResolvedValue(details)
  api.savedSummary.mockResolvedValue(null)
  api.checklist.mockResolvedValue({
    aiReview: false, quality: false, understand: false,
    architecture: false, debug: false, ready: false, updatedAt: null, debugNote: null,
  })
  api.setDebugNote.mockImplementation(async (_project, _repository, _id, note) => ({
    aiReview: false, quality: false, understand: false, architecture: false,
    debug: false, ready: false, updatedAt: '2026-09-01T12:00:00Z', debugNote: note.trim() || null,
  }))
  api.fileDiff.mockImplementation(async (_project, _repository, _id, path) => ({
    kind: 'text', path, originalPath: null, originalText: 'old', modifiedText: 'new',
  }))
  api.generateSummary.mockResolvedValue({
    schemaVersion: 2,
    criticalFiles: [],
    summary: 'Zmieniono przepływ faktur. Dodano testy.',
    baseCommitSha: 'a'.repeat(40),
    headCommitSha: 'b'.repeat(40),
    contextReport: {
      changedFiles: 3, includedFiles: 2, includedDiffCharacters: 20,
      wasLimited: true, omittedFiles: [{ path: '/tests/third.cs', reason: 'fileCharacterLimit' }],
    },
  })
  api.explainFile.mockImplementation(async (_project, _repository, _id, path) => ({
    schemaVersion: 2, path, headCommitSha: 'b'.repeat(40), sentences: [`Wyjaśnienie dla ${path}.`],
  }))
  api.fileReviews.mockResolvedValue({ files: [], readingPath: [], updatedAt: null })
  api.fileReviewProgress.mockResolvedValue([])
  api.commentThreads.mockResolvedValue([])
  api.setReadingPath.mockImplementation(async (_project, _repository, _id, paths) => ({ paths, updatedAt: null }))
  api.setFileReviewed.mockImplementation(async (_project, _repository, _id, update) => ({
    entry: update.reviewed
      ? { path: update.path, blobId: update.blobId ?? null, headSha: update.headCommitSha ?? null, updatedAt: '2026-09-01T12:00:00Z' }
      : null,
    reviewedCount: update.reviewed ? 1 : 0,
  }))
  window.matchMedia = vi.fn().mockReturnValue({ matches: false })
})

describe('PR review', () => {
  it('shows commits and moves through search matches without marking files automatically', async () => {
    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()

    expect(wrapper.findAll('.commit-list li').map(item => item.text())).toEqual([
      expect.stringContaining('aaaaaaaaFirst change'),
      expect.stringContaining('bbbbbbbbAdd tests'),
    ])
    expect(wrapper.text()).toContain('0 / 3 obejrzanych')

    await wrapper.find('.file-filter button:nth-child(2)').trigger('click')
    await wrapper.find('#file-search').setValue('/src/')
    expect(wrapper.findAll('.changed-files li')).toHaveLength(2)
    await wrapper.find('.next-file-button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('first.cs')
    expect(wrapper.find('.diff-toolbar-title').attributes('title')).toBe('/src/first.cs')
    expect(wrapper.find('.review-check input').element.checked).toBe(false)
    expect(wrapper.text()).toContain('0 / 3 obejrzanych')

    await wrapper.find('.review-check input').setValue(true)
    expect(wrapper.findAll('.changed-files li')).toHaveLength(1)
    expect(wrapper.text()).toContain('1 / 3 obejrzanych')
    await wrapper.find('.next-file-button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('second.cs')
    expect(wrapper.find('.diff-toolbar-title').attributes('title')).toBe('/src/second.cs')
    expect(api.fileDiff).toHaveBeenCalledWith('project', 'repo-a', 123, '/src/second.cs', undefined)

    await wrapper.find('.review-check input').setValue(true)
    expect(wrapper.find('.next-file-button').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Brak nieobejrzanych plików w wynikach wyszukiwania.')
    await wrapper.find('#file-search').setValue('')
    await wrapper.find('.next-file-button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('third.cs')
    expect(wrapper.find('.diff-toolbar-title').attributes('title')).toBe('/tests/third.cs')
    await wrapper.find('.review-check input').setValue(true)
    expect(wrapper.text()).toContain('Wszystkie pliki obejrzane.')
    wrapper.unmount()
  })

  it('folds noise files into their own group and walks them after the code', async () => {
    const withNoise = {
      ...details,
      changedFilesCount: 5,
      changedFiles: [
        ...details.changedFiles,
        { path: '/package-lock.json', changeType: 'edit', originalPath: null, category: 'lockFile' },
        { path: '/dist/app.js', changeType: 'add', originalPath: null, category: 'buildOutput' },
      ],
    }
    api.pullRequests.mockResolvedValue([withNoise])
    api.pullRequest.mockResolvedValue(withNoise)

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()

    // The code tree holds only the three source files; the noise sits in a collapsed group.
    expect(wrapper.findAll('.changed-files li')).toHaveLength(3)
    expect(wrapper.find('.noise-summary').text()).toBe('Szum (2)')
    expect(wrapper.findAll('.noise-section li')).toHaveLength(2)
    expect(wrapper.find('.noise-section').attributes('open')).toBeUndefined()

    // "Next unreviewed" clears the code before it offers a lockfile. Inside the noise
    // group the tree order applies, so /dist/app.js comes before the file at the root.
    for (const expected of ['first.cs', 'second.cs', 'third.cs', 'app.js', 'package-lock.json']) {
      await wrapper.find('.next-file-button').trigger('click')
      await flushPromises()
      expect(wrapper.find('.diff-toolbar h4').text()).toBe(expected)
      await wrapper.find('.review-check input').setValue(true)
    }
    expect(wrapper.text()).toContain('Wszystkie pliki obejrzane.')
    wrapper.unmount()
  })

  it('offers the AI ranking as a proposal and writes the reading path only once accepted', async () => {
    api.generateSummary.mockResolvedValue({
      schemaVersion: 2,
      summary: 'Zmieniono przepływ faktur. Dodano testy.',
      sentences: ['Zmieniono przepływ faktur.', 'Dodano testy.'],
      baseCommitSha: 'a'.repeat(40),
      headCommitSha: 'b'.repeat(40),
      criticalFiles: [
        { path: '/src/second.cs', role: 'Wejście do wysyłki.', why: 'Początek całego flow.' },
        { path: '/src/first.cs', role: 'Walidacja danych.', why: 'Tu jest reguła biznesowa.' },
      ],
      contextReport: { changedFiles: 3, includedFiles: 3, includedDiffCharacters: 20, wasLimited: false, omittedFiles: [] },
    })

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()

    // Nothing is proposed before the summary exists, and nothing is ever written on its own.
    expect(wrapper.find('.critical-proposal').exists()).toBe(false)
    await wrapper.find('.summary-button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.critical-proposal').exists()).toBe(true)
    expect(api.setReadingPath).not.toHaveBeenCalled()
    expect(wrapper.findAll('.critical-list li')).toHaveLength(0)

    // The ranking also labels the rows in the file tree.
    expect(wrapper.findAll('.file-role').map(role => role.text()))
      .toEqual(['Walidacja danych.', 'Wejście do wysyłki.'])

    // Accepting keeps the model's order, and the proposal gives way to the real path.
    await wrapper.find('.critical-accept').trigger('click')
    await flushPromises()
    expect(api.setReadingPath).toHaveBeenCalledWith('project', 'repo-a', 123, ['/src/second.cs', '/src/first.cs'])
    expect(wrapper.find('.critical-proposal').exists()).toBe(false)
    expect(wrapper.findAll('.critical-list li')).toHaveLength(2)
    wrapper.unmount()
  })

  it('leaves a reading path the user already built alone', async () => {
    api.fileReviews.mockResolvedValue({ files: [], readingPath: ['/tests/third.cs'], updatedAt: null })
    api.generateSummary.mockResolvedValue({
      schemaVersion: 2,
      summary: 'Zmieniono przepływ faktur. Dodano testy.',
      sentences: ['Zmieniono przepływ faktur.', 'Dodano testy.'],
      baseCommitSha: 'a'.repeat(40),
      headCommitSha: 'b'.repeat(40),
      criticalFiles: [{ path: '/src/first.cs', role: 'Walidacja danych.', why: 'Reguła biznesowa.' }],
      contextReport: { changedFiles: 3, includedFiles: 3, includedDiffCharacters: 20, wasLimited: false, omittedFiles: [] },
    })

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()
    await wrapper.find('.summary-button').trigger('click')
    await flushPromises()

    expect(wrapper.find('.critical-proposal').exists()).toBe(false)
    expect(wrapper.findAll('.critical-list li')).toHaveLength(1)
    // The label still shows — it informs without overwriting anything.
    expect(wrapper.find('.file-role').text()).toBe('Walidacja danych.')
    wrapper.unmount()
  })

  it('explains the open file on demand and drops an explanation that arrives after a file switch', async () => {
    const wrapper = mount(App, { attachTo: document.body })
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()
    await wrapper.find('.next-file-button').trigger('click')
    await flushPromises()

    // Nothing is explained until asked, and the diff is never replaced by the explanation.
    expect(wrapper.find('.file-explanation').exists()).toBe(false)
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'e' }))
    await flushPromises()
    expect(api.explainFile).toHaveBeenCalledWith('project', 'repo-a', 123, '/src/first.cs')
    expect(wrapper.find('.file-explanation').text()).toContain('Wyjaśnienie dla /src/first.cs.')
    expect(wrapper.find('.test-diff').exists()).toBe(true)

    // An answer for the file you just left must not appear over the file you moved to.
    let resolveLate!: (value: unknown) => void
    api.explainFile.mockReturnValueOnce(new Promise(resolve => { resolveLate = resolve }))
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'e' }))
    await wrapper.find('.next-file-button').trigger('click')
    await flushPromises()
    resolveLate({ schemaVersion: 2, path: '/src/first.cs', headCommitSha: null, sentences: ['Spóźnione.'] })
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('second.cs')
    expect(wrapper.find('.file-explanation').exists()).toBe(false)
    wrapper.unmount()
  })

  it('saves the Debug Check answer without ticking anything for you', async () => {
    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()

    expect(wrapper.find('.debug-question').text()).toBe('Gdyby ta zmiana nie zadziałała, gdzie zacząłbyś szukać?')
    await wrapper.find('#debug-answer').setValue('  Od kolejki wysyłki.  ')
    await wrapper.find('.debug-save').trigger('click')
    await flushPromises()

    expect(api.setDebugNote).toHaveBeenCalledWith('project', 'repo-a', 123, '  Od kolejki wysyłki.  ')
    // The field shows what the server stored, and no checklist box moved.
    expect((wrapper.find('#debug-answer').element as HTMLTextAreaElement).value).toBe('Od kolejki wysyłki.')
    expect(wrapper.text()).toContain('Zapisano')
    expect(wrapper.findAll('.checklist-item input').every(box => !(box.element as HTMLInputElement).checked)).toBe(true)
    wrapper.unmount()
  })

  it('lists comment threads and jumps to the file a thread is anchored to', async () => {
    api.commentThreads.mockResolvedValue([
      {
        id: 1, status: 'active', filePath: '/src/second.cs', rightLine: 42, leftLine: null, isSystem: false,
        comments: [
          { id: 1, author: 'Jan', content: 'Czy to na pewno tutaj?', commentType: 'text', publishedAt: null },
          { id: 2, author: 'Anna', content: null, commentType: 'text', publishedAt: null },
        ],
      },
      { id: 2, status: null, filePath: null, rightLine: null, leftLine: null, isSystem: false,
        comments: [{ id: 1, author: 'Jan', content: 'Ogólna uwaga.', commentType: 'text', publishedAt: null }] },
    ])

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()

    const locations = wrapper.findAll('.thread-location')
    expect(locations.map(item => item.text())).toEqual(['second.cs:42', 'Cały PR'])
    // A thread with no file has nothing to jump to.
    expect(locations[1]!.attributes('disabled')).toBeDefined()

    // The rail is a summary; the full conversation, including a deleted comment keeping
    // its place, lives in the comments view.
    await wrapper.find('.comments-open').trigger('click')
    expect(wrapper.text()).toContain('(komentarz usunięty)')
    await wrapper.find('.comments-close').trigger('click')

    await wrapper.findAll('.thread-location')[0]!.trigger('click')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('second.cs')
    wrapper.unmount()
  })

  it('writes a comment only on the second step and shows what the server returned', async () => {
    const thread = {
      id: 5, status: 'active', filePath: '/src/second.cs', rightLine: 7, leftLine: null, isSystem: false,
      comments: [{ id: 1, author: 'Jan', content: 'Czy to na pewno tutaj?', commentType: 'text', publishedAt: null }],
    }
    api.commentThreads.mockResolvedValue([thread])
    api.replyToThread.mockResolvedValue(thread)

    const wrapper = mount(App, { attachTo: document.body })
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'c' }))
    await flushPromises()

    expect(wrapper.find('.comments-view').exists()).toBe(true)
    expect(wrapper.find('.comments-counts').text()).toBe('1 aktywnych z 1')

    // Opening a draft sends nothing, and an empty draft cannot be sent.
    await wrapper.find('.thread-actions button').trigger('click')
    expect(api.replyToThread).not.toHaveBeenCalled()
    expect(wrapper.find('.comment-send').attributes('disabled')).toBeDefined()

    await wrapper.find('.comment-draft textarea').setValue('Sprawdziłem, jest dobrze.')
    await wrapper.find('.comment-send').trigger('click')
    await flushPromises()

    expect(api.replyToThread).toHaveBeenCalledWith('project', 'repo-a', 123, 5, 'Sprawdziłem, jest dobrze.')
    // Nothing optimistic: the list is read back from Azure DevOps after the write.
    expect(api.commentThreads).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('finds comments by content and groups them by file', async () => {
    api.commentThreads.mockResolvedValue([
      { id: 1, status: 'active', filePath: '/src/second.cs', rightLine: 7, leftLine: null, isSystem: false,
        comments: [{ id: 1, author: 'Jan', content: 'Literówka w nazwie.', commentType: 'text', publishedAt: null }] },
      { id: 2, status: 'fixed', filePath: '/tests/third.cs', rightLine: 3, leftLine: null, isSystem: false,
        comments: [{ id: 1, author: 'Anna', content: 'Brakuje przypadku granicznego.', commentType: 'text', publishedAt: null }] },
    ])

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()
    await wrapper.find('.comments-open').trigger('click')
    await flushPromises()

    expect(wrapper.findAll('.thread-group-head').map(head => head.text()))
      .toEqual(['/src/second.cs', '/tests/third.cs'])

    await wrapper.find('#thread-search').setValue('graniczn')
    expect(wrapper.findAll('.thread--full')).toHaveLength(1)
    expect(wrapper.find('.thread-group-head').text()).toBe('/tests/third.cs')

    await wrapper.find('#thread-search').setValue('')
    await wrapper.findAll('.comments-toolbar .file-filter button')[1]!.trigger('click')
    expect(wrapper.findAll('.thread--full')).toHaveLength(1)
    expect(wrapper.find('.thread-group-head').text()).toBe('/src/second.cs')
    wrapper.unmount()
  })

  it('flags a thread whose code moved on and shows only what changed since it', async () => {
    const withIterations = { ...details, iterations: [{ id: 1, sourceCommitSha: 'a'.repeat(40) }, { id: 3, sourceCommitSha: 'b'.repeat(40) }] }
    api.pullRequests.mockResolvedValue([withIterations])
    api.pullRequest.mockResolvedValue(withIterations)
    api.commentThreads.mockResolvedValue([
      { id: 1, status: 'active', filePath: '/src/first.cs', rightLine: 4, leftLine: null, isSystem: false, iterationId: 1,
        comments: [{ id: 1, author: 'Jan', content: 'Do poprawy.', commentType: 'text', publishedAt: null }] },
      { id: 2, status: 'active', filePath: '/src/second.cs', rightLine: 9, leftLine: null, isSystem: false, iterationId: 3,
        comments: [{ id: 1, author: 'Anna', content: 'Świeża uwaga.', commentType: 'text', publishedAt: null }] },
    ])

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()
    await wrapper.find('.comments-open').trigger('click')
    await flushPromises()

    // Only the thread left on an older iteration is flagged.
    const moved = wrapper.findAll('.thread-moved')
    expect(moved).toHaveLength(1)
    expect(moved[0]!.text()).toContain('iteracja 1 → 3')

    await wrapper.find('.thread-since').trigger('click')
    await flushPromises()
    expect(api.fileDiff).toHaveBeenCalledWith('project', 'repo-a', 123, '/src/first.cs', 1)
    expect(wrapper.find('.diff-since').exists()).toBe(true)

    // Going back to the whole change asks for the plain diff again.
    await wrapper.find('.diff-since button').trigger('click')
    await flushPromises()
    expect(api.fileDiff).toHaveBeenLastCalledWith('project', 'repo-a', 123, '/src/first.cs', undefined)
    expect(wrapper.find('.diff-since').exists()).toBe(false)
    wrapper.unmount()
  })

  it('starts a line comment from the diff without leaving the diff', async () => {
    const existing = {
      id: 5, status: 'active', filePath: '/src/first.cs', rightLine: 4, leftLine: null, isSystem: false, iterationId: null,
      comments: [{ id: 1, author: 'Jan', content: 'Czy to na pewno tutaj?', commentType: 'text', publishedAt: null }],
    }
    api.commentThreads.mockResolvedValue([existing])
    api.createThread.mockResolvedValue({ ...existing, id: 6, rightLine: 11 })

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()
    await wrapper.find('.next-file-button').trigger('click')
    await flushPromises()

    const diff = wrapper.findComponent({ name: 'MonacoDiff' })
    // The lines that already carry a thread are handed to the editor as markers.
    expect(diff.props('commentLines')).toEqual([4])

    // A line without a thread opens a draft, and the diff stays on screen.
    diff.vm.$emit('openLine', 11)
    await flushPromises()
    expect(wrapper.find('.inline-comments').text()).toContain('Nowy komentarz do linii 11')
    expect(wrapper.find('.comments-view').exists()).toBe(false)
    expect(wrapper.find('.test-diff').exists()).toBe(true)
    expect(api.createThread).not.toHaveBeenCalled()

    await wrapper.find('.inline-comments textarea').setValue('Tu brakuje sprawdzenia.')
    await wrapper.find('.inline-comments .comment-send').trigger('click')
    await flushPromises()
    expect(api.createThread).toHaveBeenCalledWith('project', 'repo-a', 123,
      { content: 'Tu brakuje sprawdzenia.', filePath: '/src/first.cs', line: 11 })

    // A line that already has one opens that conversation instead.
    diff.vm.$emit('openLine', 4)
    await flushPromises()
    expect(wrapper.find('.inline-comments').text()).toContain('Czy to na pewno tutaj?')
    wrapper.unmount()
  })

  it('ignores a late PR list from the previously selected repository', async () => {
    let resolveOld!: (value: typeof details[]) => void
    const oldResponse = new Promise<typeof details[]>(resolve => { resolveOld = resolve })
    api.repositories.mockResolvedValue([
      { id: 'repo-a', name: 'Repo A' },
      { id: 'repo-b', name: 'Repo B' },
    ])
    api.pullRequests.mockImplementation((_project, repository) => repository === 'repo-a'
      ? oldResponse
      : Promise.resolve([{ ...details, id: 456, title: 'Current repo PR' }]))

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.findAll('select')[1]!.setValue('repo-a')
    await wrapper.findAll('select')[1]!.setValue('repo-b')
    await flushPromises()
    expect(wrapper.find('.pr-row').text()).toContain('Current repo PR')

    resolveOld([details])
    await flushPromises()
    expect(wrapper.findAll('.pr-row')).toHaveLength(1)
    expect(wrapper.find('.pr-row').text()).toContain('Current repo PR')
    wrapper.unmount()
  })

  it('runs Summary only on click and shows the context limitation', async () => {
    const wrapper = mount(App)
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()

    expect(api.generateSummary).not.toHaveBeenCalled()
    await wrapper.find('.summary-button').trigger('click')
    await flushPromises()

    expect(api.generateSummary).toHaveBeenCalledExactlyOnceWith('project', 'repo-a', 123)
    expect(wrapper.find('.summary-text').text()).toBe('Zmieniono przepływ faktur. Dodano testy.')
    expect(wrapper.find('.summary-report').text()).toContain('2 / 3 plików z diffem')
    expect(wrapper.find('.summary-omissions').text()).toContain('/tests/third.cs')
    expect(wrapper.find('.summary-omissions').text()).toContain('limit na plik')
    wrapper.unmount()
  })

  it('shows an analysis error and ignores a late result after changing PR', async () => {
    let resolveOld!: (value: unknown) => void
    api.generateSummary.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve }))
    api.pullRequests.mockResolvedValue([details, { ...details, id: 456, title: 'Another PR' }])
    api.pullRequest.mockImplementation(async (_project, _repo, id) => ({ ...details, id }))

    const wrapper = mount(App)
    await flushPromises()
    await wrapper.findAll('.pr-row')[0]!.trigger('click')
    await flushPromises()
    await wrapper.find('.summary-button').trigger('click')
    expect(wrapper.find('.summary-button').attributes('disabled')).toBeDefined()
    await wrapper.find('.back-button').trigger('click')
    await wrapper.findAll('.pr-row')[1]!.trigger('click')
    await flushPromises()
    resolveOld({ summary: 'Old PR summary' })
    await flushPromises()
    expect(wrapper.find('.summary-text').exists()).toBe(false)

    api.generateSummary.mockRejectedValueOnce(new Error('AI CLI timed out.'))
    await wrapper.find('.summary-button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.summary-section [role="alert"]').text()).toContain('AI CLI timed out.')
    wrapper.unmount()
  })
})

describe('Skróty klawiszowe', () => {
  // Attached to the document on purpose: the keydown listener lives on window, and a
  // detached tree never propagates events to it — which would make these tests vacuous.
  async function openFirstFile() {
    const wrapper = mount(App, { attachTo: document.body })
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()
    await wrapper.find('.next-file-button').trigger('click')
    await flushPromises()
    return wrapper
  }

  function press(key: string, target: EventTarget = window) {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
  }

  it('marks the open file and moves to the next one on "m"', async () => {
    const wrapper = await openFirstFile()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('first.cs')
    expect(wrapper.text()).toContain('0 / 3 obejrzanych')

    press('m')
    await flushPromises()

    expect(wrapper.text()).toContain('1 / 3 obejrzanych')
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('second.cs')
    wrapper.unmount()
  })

  it('ignores shortcuts while the file search has focus', async () => {
    const wrapper = await openFirstFile()
    const search = wrapper.find('#file-search').element

    press('m', search)
    await flushPromises()

    expect(wrapper.text()).toContain('0 / 3 obejrzanych')
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('first.cs')

    // Same key from a non-typing element must still work — otherwise the assertion
    // above would pass simply because the event never reached the window listener.
    press('m', wrapper.find('.diff-panel').element)
    await flushPromises()
    expect(wrapper.text()).toContain('1 / 3 obejrzanych')
    wrapper.unmount()
  })

  it('walks the visible file list with j and k', async () => {
    const wrapper = await openFirstFile()

    press('j')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('second.cs')

    press('k')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('first.cs')
    wrapper.unmount()
  })

  it('goes to the description and back to the file that was open', async () => {
    const wrapper = await openFirstFile()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('first.cs')

    press('o')
    await flushPromises()

    // The way back has to be visible, not only bound to a key.
    expect(wrapper.find('.pr-briefing').exists()).toBe(true)
    expect(wrapper.find('.briefing-back').text()).toContain('first.cs')

    press('o')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('first.cs')
    wrapper.unmount()
  })

  it('returns to the file by clicking the button in the description', async () => {
    const wrapper = await openFirstFile()
    press('o')
    await flushPromises()

    await wrapper.find('.briefing-back').trigger('click')
    await flushPromises()

    expect(wrapper.find('.diff-toolbar h4').text()).toBe('first.cs')
    wrapper.unmount()
  })

  it('separates file navigation from change navigation in the readbar', async () => {
    const wrapper = await openFirstFile()
    const labels = wrapper.findAll('.diff-actions-label').map(node => node.text())
    expect(labels).toEqual(['Plik', 'Zmiana'])

    // The first pair walks files; the second pair walks hunks inside the open file.
    const buttons = wrapper.findAll('.diff-actions button')
    expect(buttons[0]!.attributes('disabled')).toBeDefined()   // first file, nothing before it
    await buttons[1]!.trigger('click')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('second.cs')

    await buttons[0]!.trigger('click')
    await flushPromises()
    expect(wrapper.find('.diff-toolbar h4').text()).toBe('first.cs')
    wrapper.unmount()
  })

  it('stops listening once the app is unmounted', async () => {
    const wrapper = await openFirstFile()
    wrapper.unmount()

    // A leaked capture-phase listener would throw or mutate state after teardown.
    expect(() => press('m')).not.toThrow()
  })
})

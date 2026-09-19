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
  savedSummary: vi.fn(),
  checklist: vi.fn(),
}))

vi.mock('../src/api', () => ({ api }))
vi.mock('../src/MonacoDiff.vue', () => ({
  default: { template: '<div class="test-diff">Diff</div>' },
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
    architecture: false, debug: false, ready: false, updatedAt: null,
  })
  api.fileDiff.mockImplementation(async (_project, _repository, _id, path) => ({
    kind: 'text', path, originalPath: null, originalText: 'old', modifiedText: 'new',
  }))
  api.generateSummary.mockResolvedValue({
    schemaVersion: 1,
    summary: 'Zmieniono przepływ faktur. Dodano testy.',
    baseCommitSha: 'a'.repeat(40),
    headCommitSha: 'b'.repeat(40),
    contextReport: {
      changedFiles: 3, includedFiles: 2, includedDiffCharacters: 20,
      wasLimited: true, omittedFiles: [{ path: '/tests/third.cs', reason: 'fileCharacterLimit' }],
    },
  })
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
    expect(api.fileDiff).toHaveBeenCalledWith('project', 'repo-a', 123, '/src/second.cs')

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

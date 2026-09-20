// @vitest-environment jsdom
// The guided walkthrough (przejście): US-P3 to US-P7. What is pinned here is the logic the
// brief names — picking the path, counting progress along it, and the prefetch — not layout.
import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../src/App.vue'

const api = vi.hoisted(() => ({
  projects: vi.fn(), repositories: vi.fn(), pullRequests: vi.fn(), checklistProgress: vi.fn(),
  pullRequest: vi.fn(), fileDiff: vi.fn(), generateSummary: vi.fn(), explainFile: vi.fn(),
  savedSummary: vi.fn(), checklist: vi.fn(), fileReviews: vi.fn(), setFileReviewed: vi.fn(),
  setReadingPath: vi.fn(), setDebugNote: vi.fn(), fileReviewProgress: vi.fn(),
  commentThreads: vi.fn(), createThread: vi.fn(), replyToThread: vi.fn(), setThreadStatus: vi.fn(),
  editComment: vi.fn(), deleteComment: vi.fn(), config: vi.fn(),
}))

vi.mock('../src/api', () => ({ api }))
vi.mock('../src/MonacoDiff.vue', () => ({
  default: {
    name: 'MonacoDiff',
    props: ['path', 'originalPath', 'originalText', 'modifiedText', 'sideBySide', 'commentLines', 'resolvedLines', 'zoneLines'],
    emits: ['openLine', 'zones'],
    methods: { revealLine: () => {}, goToDiff: () => {}, focusEditor: () => {}, cursorLine: () => null },
    template: '<div class="test-diff">Diff</div>',
  },
}))

const HEAD = 'b'.repeat(40)
const OTHER_HEAD = 'c'.repeat(40)
// Eight sources plus a lockfile: over the five-file threshold, and the noise has to stay
// out of the proposal on its own.
const paths = ['/src/a.cs', '/src/b.cs', '/src/c.cs', '/src/d.cs', '/src/e.cs', '/src/f.cs', '/src/g.cs', '/src/h.cs']
const details = {
  id: 123, title: 'Big change', author: 'Anna', repository: 'Repo A', status: 'active',
  createdAt: '2026-09-01T12:00:00Z', description: 'Opis', sourceBranch: 'feature', targetBranch: 'main',
  reviewers: [], commitsCount: 1, commits: [], workItems: [],
  headCommitSha: HEAD,
  changedFilesCount: paths.length + 1,
  changedFiles: [
    ...paths.map((path, index) => ({ path, changeType: 'edit', originalPath: null, objectId: String(index).repeat(40) })),
    { path: '/package-lock.json', changeType: 'edit', originalPath: null, category: 'lockFile' },
  ],
}

// The ranking: six sources in the order the model wants them read, plus the lockfile it
// must never be allowed to propose.
const ranking = [
  ...paths.slice(0, 6).map((path, index) => ({ path, role: `Rola ${index + 1}.`, why: `Powód ${index + 1}.` })),
  { path: '/package-lock.json', role: 'Zależności.', why: 'Zmiana wersji.' },
]

function summary(criticalFiles = ranking) {
  return {
    schemaVersion: 2, summary: 'Zmieniono przepływ faktur.',
    sentences: ['Zmieniono przepływ faktur.'],
    baseCommitSha: 'a'.repeat(40), headCommitSha: HEAD, criticalFiles,
    contextReport: { changedFiles: 9, includedFiles: 9, includedDiffCharacters: 20, wasLimited: false, omittedFiles: [] },
  }
}

function readingPath(overrides: Partial<{ paths: string[]; position: number; headCommitSha: string | null }> = {}) {
  return { paths: [] as string[], position: 0, headCommitSha: null as string | null, updatedAt: null, ...overrides }
}

beforeEach(() => {
  vi.resetAllMocks()
  api.projects.mockResolvedValue([{ id: 'project', name: 'Project' }])
  api.repositories.mockResolvedValue([{ id: 'repo-a', name: 'Repo A' }])
  api.pullRequests.mockResolvedValue([details])
  api.pullRequest.mockResolvedValue(details)
  api.checklistProgress.mockResolvedValue([])
  api.fileReviewProgress.mockResolvedValue([])
  api.commentThreads.mockResolvedValue([])
  api.config.mockResolvedValue({ commentsEnabled: true })
  api.savedSummary.mockResolvedValue(null)
  api.generateSummary.mockResolvedValue(summary())
  api.checklist.mockResolvedValue({
    aiReview: false, quality: false, understand: false, architecture: false,
    debug: false, ready: false, updatedAt: null, debugNote: null,
  })
  api.setDebugNote.mockImplementation(async (_p, _r, _i, note) => ({
    aiReview: false, quality: false, understand: false, architecture: false, debug: false,
    ready: false, updatedAt: '2026-09-01T12:00:00Z', debugNote: note.trim() || null,
  }))
  api.fileDiff.mockImplementation(async (_p, _r, _i, path) => ({
    kind: 'text', path, originalPath: null, originalText: 'old', modifiedText: 'new',
  }))
  api.explainFile.mockImplementation(async (_p, _r, _i, path) => ({
    schemaVersion: 2, path, headCommitSha: HEAD, sentences: [`Wyjaśnienie ${path}.`],
  }))
  api.fileReviews.mockResolvedValue({ files: [], readingPath: readingPath(), updatedAt: null })
  api.setReadingPath.mockImplementation(async (_p, _r, _i, list, position, headCommitSha) =>
    readingPath({ paths: list, position: position ?? 0, headCommitSha: headCommitSha ?? null }))
  api.setFileReviewed.mockImplementation(async (_p, _r, _i, update) => ({
    entry: update.reviewed
      ? { path: update.path, blobId: update.blobId ?? null, headSha: update.headCommitSha ?? null, updatedAt: '2026-09-01T12:00:00Z' }
      : null,
    reviewedCount: update.reviewed ? 1 : 0,
  }))
  window.matchMedia = vi.fn().mockReturnValue({ matches: false })
})

async function openPr() {
  const wrapper = mount(App)
  await flushPromises()
  await wrapper.find('.pr-row').trigger('click')
  await flushPromises()
  return wrapper
}

function pickedPaths(wrapper: ReturnType<typeof mount>): (string | undefined)[] {
  const inputs = wrapper.findAll('.walk-file-pick input')
  const labels = wrapper.findAll('.walk-file-path')
  return inputs
    .map((input, index) => ({ on: (input.element as HTMLInputElement).checked, index }))
    .filter(entry => entry.on)
    .map(entry => labels[entry.index]!.attributes('title'))
}

describe('US-P3 — ekran wejścia', () => {
  it('opens on the proposal, keeps noise out of it and never writes the path by itself', async () => {
    const wrapper = await openPr()

    expect(wrapper.find('.walk-entry').exists()).toBe(true)
    expect(pickedPaths(wrapper)).toEqual(paths.slice(0, 6))
    // The lockfile is not offered, however the model ranked it.
    expect(wrapper.find('.walk-entry').text()).not.toContain('package-lock.json')
    expect(wrapper.find('.walk-entry-rest').text()).toContain('3')
    expect(api.setReadingPath).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('takes the edited list rather than the proposed one', async () => {
    const wrapper = await openPr()

    await wrapper.findAll('.walk-file-pick input')[0]!.setValue(false)
    expect(pickedPaths(wrapper)).toEqual(paths.slice(1, 6))

    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()
    expect(api.setReadingPath).toHaveBeenCalledWith('project', 'repo-a', 123, paths.slice(1, 6), 0, HEAD)
    wrapper.unmount()
  })

  it('leaves files that are already read out of the walkthrough, but offers them', async () => {
    api.fileReviews.mockResolvedValue({
      files: [{ path: '/src/a.cs', blobId: '0'.repeat(40), headSha: HEAD, updatedAt: '2026-09-01T12:00:00Z' }],
      readingPath: readingPath(), updatedAt: null,
    })

    const wrapper = await openPr()

    expect(pickedPaths(wrapper)).toEqual(paths.slice(1, 6))
    expect(wrapper.find('.walk-file-read').text()).toBe('przeczytany')
    // Offered, not hidden: one click puts it back in.
    await wrapper.findAll('.walk-file-pick input')[0]!.setValue(true)
    expect(pickedPaths(wrapper)).toContain('/src/a.cs')
    wrapper.unmount()
  })

  it('never shows up for a small pull request', async () => {
    const small = { ...details, changedFilesCount: 3, changedFiles: details.changedFiles.slice(0, 3) }
    api.pullRequests.mockResolvedValue([small])
    api.pullRequest.mockResolvedValue(small)

    const wrapper = await openPr()

    expect(wrapper.find('.walk-entry').exists()).toBe(false)
    expect(wrapper.find('.pr-workspace').exists()).toBe(true)
    wrapper.unmount()
  })

  it('says the proposal is unavailable and still hands over the tree', async () => {
    api.generateSummary.mockRejectedValue(new Error('AI CLI timed out.'))

    const wrapper = await openPr()

    expect(wrapper.find('.walk-entry [role="alert"]').text()).toContain('AI CLI timed out.')
    await wrapper.findAll('.walk-actions button').at(-1)!.trigger('click')
    expect(wrapper.find('.pr-workspace').exists()).toBe(true)
    wrapper.unmount()
  })
})

describe('US-P4 — przejście plik po pliku', () => {
  async function startWalk() {
    const wrapper = await openPr()
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()
    return wrapper
  }

  it('counts along the path, not along the Azure DevOps file list', async () => {
    const wrapper = await startWalk()

    expect(wrapper.find('.walk-progress').text()).toBe('Plik 1 z 6')
    expect(wrapper.find('.walk-bar-file').attributes('title')).toBe('/src/a.cs')
    expect(wrapper.find('.walk-bar-role').text()).toBe('Rola 1.')
    // The rail and the tree are gone; the diff has the window.
    expect(wrapper.find('.pr-workspace').classes()).toContain('pr-workspace--walk')
    expect(wrapper.find('.test-diff').exists()).toBe(true)
    wrapper.unmount()
  })

  it('marks a file read on the way to the next one', async () => {
    const wrapper = await startWalk()

    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()

    expect(api.setFileReviewed).toHaveBeenCalledWith('project', 'repo-a', 123,
      expect.objectContaining({ path: '/src/a.cs', reviewed: true }))
    expect(wrapper.find('.walk-progress').text()).toBe('Plik 2 z 6')
    expect(api.setReadingPath).toHaveBeenLastCalledWith('project', 'repo-a', 123, paths.slice(0, 6), 1, HEAD)
    wrapper.unmount()
  })

  it('skips without marking, and going back leaves the earlier mark alone', async () => {
    const wrapper = await startWalk()

    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')   // a.cs read
    await flushPromises()
    await wrapper.findAll('.walk-actions--sticky button')[1]!.trigger('click')   // b.cs skipped
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Plik 3 z 6')
    expect(api.setFileReviewed).toHaveBeenCalledTimes(1)

    await wrapper.findAll('.walk-actions--sticky button')[2]!.trigger('click')   // back to b.cs
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Plik 2 z 6')
    expect(api.setFileReviewed).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('leaves for the tree and comes back where it stopped', async () => {
    const wrapper = await startWalk()
    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()

    await wrapper.find('.walk-bar-exit').trigger('click')
    expect(wrapper.find('.pr-workspace').classes()).not.toContain('pr-workspace--walk')
    expect(wrapper.findAll('.changed-files li').length).toBeGreaterThan(0)

    await wrapper.find('.walk-enter-button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-resume').text()).toContain('plik 2 z 6')
    wrapper.unmount()
  })
})

describe('US-P5 — wyjaśnienia gotowe przed plikiem', () => {
  it('generates the path explanations in the background and shows them without waiting', async () => {
    const wrapper = await openPr()
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()

    expect(api.explainFile.mock.calls.map(call => call[3])).toEqual(paths.slice(0, 6))
    expect(wrapper.find('.walk-explanation-text').text()).toBe('Wyjaśnienie /src/a.cs.')

    api.explainFile.mockClear()
    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()
    // Already in hand, so entering the file asks for nothing and shows it at once.
    expect(api.explainFile).not.toHaveBeenCalled()
    expect(wrapper.find('.walk-explanation-text').text()).toBe('Wyjaśnienie /src/b.cs.')
    wrapper.unmount()
  })

  it('carries on past a file the model failed on and offers that one a retry', async () => {
    api.explainFile.mockImplementation(async (_p: string, _r: string, _i: number, path: string) => {
      if (path === '/src/a.cs') throw new Error('AI CLI timed out.')
      return { schemaVersion: 2, path, headCommitSha: HEAD, sentences: [`Wyjaśnienie ${path}.`] }
    })

    const wrapper = await openPr()
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()

    expect(wrapper.find('.walk-explanation [role="alert"]').text()).toContain('AI CLI timed out.')
    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-explanation-text').text()).toBe('Wyjaśnienie /src/b.cs.')
    wrapper.unmount()
  })
})

describe('US-P6 — domknięcie', () => {
  it('ends on the numbers, the skipped files and one question', async () => {
    const wrapper = await openPr()
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()

    for (let index = 0; index < 6; index++) {
      const buttons = wrapper.findAll('.walk-actions--sticky button')
      await (index === 2 ? buttons[1]! : buttons[0]!).trigger('click')
      await flushPromises()
    }

    const done = wrapper.find('.walk-done')
    expect(done.exists()).toBe(true)
    expect(done.find('.walk-done-counts').text()).toContain('5')
    expect(done.find('.walk-skipped').text()).toContain('c.cs')

    await done.find('#walk-debug-answer').setValue('Zacząłbym od kolejki.')
    await done.findAll('.debug-actions button')[0]!.trigger('click')
    await flushPromises()
    expect(api.setDebugNote).toHaveBeenCalledWith('project', 'repo-a', 123, 'Zacząłbym od kolejki.')
    // Nothing on the checklist ticks itself.
    expect(wrapper.text()).toContain('Checklista 0 / 6')

    // One action returns to a file that was skipped.
    await wrapper.find('.walk-skipped .critical-open').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Plik 3 z 6')
    wrapper.unmount()
  })
})

describe('US-P7 — wznowienie', () => {
  it('offers the interrupted walkthrough back, from the file it stopped on', async () => {
    api.fileReviews.mockResolvedValue({
      files: [], readingPath: readingPath({ paths: paths.slice(0, 6), position: 4, headCommitSha: HEAD }), updatedAt: null,
    })

    const wrapper = await openPr()

    expect(wrapper.find('.walk-resume').text()).toContain('plik 5 z 6')
    expect(wrapper.find('.walk-changed').exists()).toBe(false)
    await wrapper.find('.walk-resume .walk-primary').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Plik 5 z 6')
    expect(wrapper.find('.walk-bar-file').attributes('title')).toBe('/src/e.cs')
    wrapper.unmount()
  })

  it('says the PR moved on and deletes nothing until asked', async () => {
    api.fileReviews.mockResolvedValue({
      files: [], readingPath: readingPath({ paths: paths.slice(0, 6), position: 2, headCommitSha: OTHER_HEAD }), updatedAt: null,
    })

    const wrapper = await openPr()

    expect(wrapper.find('.walk-changed').exists()).toBe(true)
    expect(api.setReadingPath).not.toHaveBeenCalled()

    // Recomputing is a choice, and only then is a fresh proposal on screen.
    await wrapper.findAll('.walk-resume .walk-actions button')[1]!.trigger('click')
    expect(wrapper.find('.walk-resume').exists()).toBe(false)
    expect(pickedPaths(wrapper)).toEqual(paths.slice(0, 6))
    expect(api.setReadingPath).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('offers nothing once the path has been walked to its end', async () => {
    api.fileReviews.mockResolvedValue({
      files: [], readingPath: readingPath({ paths: paths.slice(0, 6), position: 6, headCommitSha: HEAD }), updatedAt: null,
    })

    const wrapper = await openPr()

    expect(wrapper.find('.walk-resume').exists()).toBe(false)
    expect(wrapper.find('.walk-entry').exists()).toBe(false)
    expect(wrapper.find('.pr-workspace').exists()).toBe(true)
    wrapper.unmount()
  })

  it('drops a file that left the pull request from the stored path', async () => {
    api.fileReviews.mockResolvedValue({
      files: [],
      readingPath: readingPath({ paths: ['/src/a.cs', '/src/gone.cs', '/src/b.cs'], position: 1, headCommitSha: HEAD }),
      updatedAt: null,
    })

    const wrapper = await openPr()

    // Two files left, so the resume offer counts two and points at the second.
    expect(wrapper.find('.walk-resume').text()).toContain('plik 2 z 2')
    await wrapper.find('.walk-resume .walk-primary').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-bar-file').attributes('title')).toBe('/src/b.cs')
    wrapper.unmount()
  })
})

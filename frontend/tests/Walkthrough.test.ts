// @vitest-environment jsdom
// The guided walkthrough (przejście): US-P3 to US-P7. What is pinned here is the logic the
// brief names — picking the path, counting progress along it, and the prefetch — not layout.
import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '@/App.vue'

const api = vi.hoisted(() => ({
  projects: vi.fn(), repositories: vi.fn(), pullRequests: vi.fn(), quizProgress: vi.fn(),
  pullRequest: vi.fn(), fileDiff: vi.fn(), generateSummary: vi.fn(), explainFile: vi.fn(),
  askAboutFile: vi.fn(), fileQuestions: vi.fn(),
  savedSummary: vi.fn(), savedQuiz: vi.fn(), composeQuiz: vi.fn(), answerQuiz: vi.fn(), fileReviews: vi.fn(), setFileReviewed: vi.fn(),
  setReadingPath: vi.fn(), fileReviewProgress: vi.fn(),
  commentThreads: vi.fn(), createThread: vi.fn(), replyToThread: vi.fn(), setThreadStatus: vi.fn(),
  editComment: vi.fn(), deleteComment: vi.fn(), config: vi.fn(),
}))

vi.mock('@/api', () => ({ api }))
vi.mock('@/features/diff/MonacoDiff.vue', () => ({
  default: {
    name: 'MonacoDiff',
    props: ['path', 'originalPath', 'originalText', 'modifiedText', 'sideBySide', 'commentLines', 'resolvedLines', 'zoneLines'],
    emits: ['openLine', 'zones'],
    methods: { revealLine: () => {}, goToDiff: () => {}, focusEditor: () => {} },
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

// The other half of the same answer: every file of the pull request in reading order, as
// the backend hands it back — the model's order kept, the lockfile sunk to the end.
const fullOrder = ['/src/c.cs', '/src/a.cs', '/src/b.cs', '/src/d.cs', '/src/e.cs',
  '/src/f.cs', '/src/g.cs', '/src/h.cs', '/package-lock.json']

function summary(criticalFiles = ranking, readingOrder: string[] | undefined = fullOrder) {
  return {
    schemaVersion: 2, summary: 'Zmieniono przepływ faktur.',
    sentences: ['Zmieniono przepływ faktur.'],
    baseCommitSha: 'a'.repeat(40), headCommitSha: HEAD, criticalFiles, readingOrder,
    contextReport: { changedFiles: 9, includedFiles: 9, includedDiffCharacters: 20, wasLimited: false, omittedFiles: [] },
  }
}

// Two questions, as the backend hands them out: no answer key until a question is passed.
function quiz(overrides: { passed?: string[]; wrong?: string } = {}) {
  const passed = overrides.passed ?? []
  const questions = [
    { id: 'q1', kind: 'intent', question: 'Po co ta zmiana?', files: ['/src/b.cs'] },
    { id: 'q2', kind: 'runtime', question: 'Czego potrzebuje na środowisku?', files: ['/src/h.cs'] },
  ].map(question => ({
    ...question,
    expectedPoints: passed.includes(question.id) ? ['Wzorcowy punkt.'] : null,
    passed: passed.includes(question.id),
    attemptCount: passed.includes(question.id) || overrides.wrong === question.id ? 1 : 0,
    lastAttempt: passed.includes(question.id)
      ? { answer: 'Dobra.', correct: true, feedback: ['Zgadza się.'], answeredAt: '2026-09-01T12:00:00Z' }
      : overrides.wrong === question.id
        ? { answer: 'Zła.', correct: false, feedback: ['Brakuje konfiguracji, zajrzyj do h.cs.'], answeredAt: '2026-09-01T12:00:00Z' }
        : null,
  }))
  return {
    headCommitSha: HEAD, createdAt: '2026-09-01T12:00:00Z',
    correct: passed.length, total: 2, passed: passed.length === 2, questions,
  }
}

// The fake grader: an answer saying "dobrze" passes, anything else is wrong, and what
// passed before stays passed.
const graded: string[] = []
function answered(questionId: string, answer: string) {
  if (answer.includes('dobrze')) graded.push(questionId)
  return quiz({ passed: [...graded], wrong: answer.includes('dobrze') ? undefined : questionId })
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
  api.quizProgress.mockResolvedValue([])
  api.fileReviewProgress.mockResolvedValue([])
  api.commentThreads.mockResolvedValue([])
  api.fileQuestions.mockResolvedValue([])
  api.askAboutFile.mockImplementation(async (_project, _repository, _id, path, question, selection) => ({
    schemaVersion: 2, path, question, selection: selection ?? null,
    sentences: [`Odpowiedź na: ${question}.`], blobId: null, headCommitSha: null,
    askedAt: '2026-09-01T12:00:00Z',
  }))
  api.config.mockResolvedValue({ commentsEnabled: true })
  // Summary is never generated on its own, so the ordinary case is one already paid for
  // and read back from the local database for free.
  api.savedSummary.mockResolvedValue({ result: summary(), savedAt: '2026-09-01T12:00:00Z' })
  api.generateSummary.mockResolvedValue(summary())
  graded.length = 0
  api.savedQuiz.mockResolvedValue(null)
  api.composeQuiz.mockResolvedValue(quiz())
  api.answerQuiz.mockImplementation(async (_p, _r, _i, questionId, answer) => answered(questionId, answer))
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

  // The model costs money, so the entry screen asks before spending any — even though the
  // proposal is the whole reason the screen exists.
  it('spends nothing until asked, then proposes', async () => {
    api.savedSummary.mockResolvedValue(null)

    const wrapper = await openPr()

    expect(api.generateSummary).not.toHaveBeenCalled()
    expect(wrapper.find('.walk-no-summary').exists()).toBe(true)
    expect(wrapper.find('.walk-file-list').exists()).toBe(false)

    await wrapper.find('.walk-no-summary .walk-primary').trigger('click')
    await flushPromises()
    expect(api.generateSummary).toHaveBeenCalledExactlyOnceWith('project', 'repo-a', 123)
    expect(pickedPaths(wrapper)).toEqual(paths.slice(0, 6))
    wrapper.unmount()
  })

  it('says the proposal is unavailable and still hands over the tree', async () => {
    api.savedSummary.mockResolvedValue(null)
    api.generateSummary.mockRejectedValue(new Error('AI CLI timed out.'))

    const wrapper = await openPr()
    await wrapper.find('.walk-no-summary .walk-primary').trigger('click')
    await flushPromises()

    expect(wrapper.find('.walk-entry [role="alert"]').text()).toContain('AI CLI timed out.')
    await wrapper.findAll('.walk-actions button').at(-1)!.trigger('click')
    expect(wrapper.find('.pr-workspace').exists()).toBe(true)
    wrapper.unmount()
  })

  // A saved ranking from an older commit is still a ranking; recomputing is money, so it
  // is offered rather than taken.
  it('shows a stale proposal with the recompute offer instead of recomputing', async () => {
    api.savedSummary.mockResolvedValue({
      result: { ...summary(), headCommitSha: OTHER_HEAD }, savedAt: '2026-09-01T12:00:00Z',
    })

    const wrapper = await openPr()

    expect(wrapper.find('.walk-stale').exists()).toBe(true)
    expect(api.generateSummary).not.toHaveBeenCalled()
    expect(pickedPaths(wrapper)).toEqual(paths.slice(0, 6))
    wrapper.unmount()
  })
})

// Ten named files open a pull request of twenty and only sample one of eighty, so both
// the ranking and the default path grow with the change.
// Tryb "wszystkie pliki": ta sama kolejność od AI, tylko bez skracania do skrótu.
describe('tryb wszystkich plików', () => {
  async function switchToAll() {
    const wrapper = await openPr()
    await wrapper.findAll('.walk-mode-option')[1]!.trigger('click')
    return wrapper
  }

  it('offers every file of the pull request in the order the model gave, noise last', async () => {
    const wrapper = await switchToAll()

    const offered = wrapper.findAll('.walk-file-path').map(node => node.attributes('title'))
    expect(offered).toEqual(fullOrder)
    // Nothing is left out of a mode whose whole point is leaving nothing out.
    expect(pickedPaths(wrapper)).toEqual(fullOrder)
    expect(wrapper.find('.walk-entry-rest').text()).toContain('Poza przejściem zostaje 0')
    wrapper.unmount()
  })

  it('writes the whole pull request as the reading path once started', async () => {
    const wrapper = await switchToAll()
    await wrapper.find('.walk-actions .walk-primary').trigger('click')
    await flushPromises()

    expect(api.setReadingPath).toHaveBeenCalledWith('project', 'repo-a', 123, fullOrder, 0, HEAD)
    expect(wrapper.find('.walk-progress').text()).toBe('Krok 1 z 9')
    wrapper.unmount()
  })

  it('goes back to the shortlist when the mode is switched back', async () => {
    const wrapper = await switchToAll()
    await wrapper.findAll('.walk-mode-option')[0]!.trigger('click')

    expect(pickedPaths(wrapper)).toEqual(paths.slice(0, 6))
    wrapper.unmount()
  })

  // A Summary paid for before this feature existed has a ranking but no order of the whole
  // pull request. Making one up would look the same on screen and be a different thing.
  it('asks for a recompute instead of inventing an order the Summary does not have', async () => {
    // Exactly what a row written before this feature reads back as: a ranking, no order.
    const { readingOrder: _missing, ...older } = summary()
    api.savedSummary.mockResolvedValue({ result: older, savedAt: '2026-09-01T12:00:00Z' })

    const wrapper = await switchToAll()

    expect(wrapper.find('.walk-entry-files .notice').text()).toContain('zanim doszła kolejność całego PR')
    expect(wrapper.findAll('.walk-file-pick input')).toHaveLength(0)
    expect(api.setReadingPath).not.toHaveBeenCalled()
    expect(api.generateSummary).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})

describe('duży PR dostaje dłuższą propozycję', () => {
  const many = Array.from({ length: 84 }, (_unused, index) => `/src/file${index}.cs`)
  const test = '/tests/file0.test.ts'

  beforeEach(() => {
    const big = {
      ...details, changedFilesCount: many.length + 1,
      changedFiles: [test, ...many].map((path, index) => ({
        path, changeType: 'edit', originalPath: null, objectId: String(index % 10).repeat(40),
      })),
    }
    api.pullRequests.mockResolvedValue([big])
    api.pullRequest.mockResolvedValue(big)
    // 85 changed files allow a ranking of 40; the model names all of them, and a test first
    // that never reaches the proposal.
    api.savedSummary.mockResolvedValue({
      result: summary([test, ...many.slice(0, 40)].map((path, index) => ({
        path, role: `Rola ${index + 1}.`, why: `Powód ${index + 1}.`,
      })), [test, ...many]),
      savedAt: '2026-09-01T12:00:00Z',
    })
  })

  it('offers half of a big pull request and leaves the tests out', async () => {
    const wrapper = await openPr()

    // The whole ranking is on offer and picked by default, the test file is not.
    expect(wrapper.findAll('.walk-file-pick input')).toHaveLength(40)
    expect(pickedPaths(wrapper)).toEqual(many.slice(0, 40))

    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Krok 1 z 40')
    wrapper.unmount()
  })

  // Walking 84 files must not mean buying 84 explanations nobody asked for.
  it('walks a big pull request without asking for a single explanation', async () => {
    const wrapper = await openPr()
    await wrapper.findAll('.walk-mode-option')[1]!.trigger('click')
    await wrapper.find('.walk-actions .walk-primary').trigger('click')
    await flushPromises()
    for (const _step of [1, 2, 3]) {
      await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
      await flushPromises()
    }

    expect(api.explainFile).not.toHaveBeenCalled()
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

    expect(wrapper.find('.walk-progress').text()).toBe('Krok 1 z 6')
    expect(wrapper.find('.walk-bar-file').attributes('title')).toBe('/src/a.cs')
    expect(wrapper.find('.walk-bar-role').text()).toBe('Rola 1.')
    // The rail and the tree are gone; the diff has the window.
    expect(wrapper.find('.pr-workspace').classes()).toContain('pr-workspace--walk')
    expect(wrapper.find('.test-diff').exists()).toBe(true)
    wrapper.unmount()
  })

  // The walkthrough is exactly where you read code you did not write, so the question box
  // has to be reachable there. It was not: the panel copied the explanation's walk guard,
  // and the explanation has a second panel up in the walk bar that the conversation has not.
  it('opens the question box in the walkthrough too', async () => {
    const wrapper = await startWalk()

    await wrapper.find('.ask-button').trigger('click')
    await flushPromises()

    expect(wrapper.find('.file-chat').exists()).toBe(true)
    await wrapper.find('.file-chat textarea').setValue('Po co to jest?')
    await wrapper.find('.file-chat .comment-send').trigger('click')
    await flushPromises()
    expect(api.askAboutFile).toHaveBeenCalledWith(
      'project', 'repo-a', 123, '/src/a.cs', 'Po co to jest?', null)
    wrapper.unmount()
  })

  it('marks a file read on the way to the next one', async () => {
    const wrapper = await startWalk()

    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()

    expect(api.setFileReviewed).toHaveBeenCalledWith('project', 'repo-a', 123,
      expect.objectContaining({ path: '/src/a.cs', reviewed: true }))
    expect(wrapper.find('.walk-progress').text()).toBe('Krok 2 z 6')
    expect(api.setReadingPath).toHaveBeenLastCalledWith('project', 'repo-a', 123, paths.slice(0, 6), 1, HEAD)
    wrapper.unmount()
  })

  it('skips without marking, and going back leaves the earlier mark alone', async () => {
    const wrapper = await startWalk()

    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')   // a.cs read
    await flushPromises()
    await wrapper.findAll('.walk-actions--sticky button')[1]!.trigger('click')   // b.cs skipped
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Krok 3 z 6')
    expect(api.setFileReviewed).toHaveBeenCalledTimes(1)

    await wrapper.findAll('.walk-actions--sticky button')[2]!.trigger('click')   // back to b.cs
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Krok 2 z 6')
    expect(api.setFileReviewed).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('leaves for the tree and comes back where it stopped', async () => {
    const wrapper = await startWalk()
    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()

    const exit = wrapper.findAll('.walk-actions--sticky button')
      .find(button => button.text().includes('Wyjdź'))!
    await exit.trigger('click')
    expect(wrapper.find('.pr-workspace').classes()).not.toContain('pr-workspace--walk')
    expect(wrapper.findAll('.changed-files li').length).toBeGreaterThan(0)

    await wrapper.find('.walk-enter-button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-resume').text()).toContain('krok 2 z 6')
    wrapper.unmount()
  })
})

// US-P5. An explanation is a paid model run, so nothing here happens on its own: the
// walkthrough offers the button and spends only when it is pressed.
describe('US-P5 — wyjaśnienie pliku tylko na prośbę', () => {
  it('spends nothing by walking and only asks when the button is pressed', async () => {
    const wrapper = await openPr()
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()

    // Entering the walkthrough and stepping through it costs nothing.
    expect(api.explainFile).not.toHaveBeenCalled()
    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()
    expect(api.explainFile).not.toHaveBeenCalled()

    await wrapper.find('.explain-button').trigger('click')
    await flushPromises()
    expect(api.explainFile.mock.calls.map(call => call[3])).toEqual(['/src/b.cs'])
    expect(wrapper.find('.walk-explanation-text').text()).toBe('Wyjaśnienie /src/b.cs.')
    // Once on the screen, not twice: the diff panel's own block stays out of the walkthrough.
    expect(wrapper.find('.file-explanation').exists()).toBe(false)
    wrapper.unmount()
  })

  it('does not pay twice for a file it was already asked about', async () => {
    const wrapper = await openPr()
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()
    await wrapper.find('.explain-button').trigger('click')
    await flushPromises()

    api.explainFile.mockClear()
    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()
    const back = wrapper.findAll('.walk-actions--sticky button')
      .find(button => button.text().includes('Wstecz'))!
    await back.trigger('click')
    await flushPromises()

    expect(api.explainFile).not.toHaveBeenCalled()
    expect(wrapper.find('.walk-explanation-text').text()).toBe('Wyjaśnienie /src/a.cs.')
    wrapper.unmount()
  })

  it('keeps a failed explanation at its own file and offers a retry', async () => {
    api.explainFile.mockRejectedValueOnce(new Error('AI CLI timed out.'))

    const wrapper = await openPr()
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()
    await wrapper.find('.explain-button').trigger('click')
    await flushPromises()

    expect(wrapper.find('.walk-explanation [role="alert"]').text()).toContain('AI CLI timed out.')
    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()
    // The next file starts clean: no error carried over, no panel, and still nothing bought.
    expect(wrapper.find('.walk-explanation').exists()).toBe(false)
    expect(wrapper.find('.explain-button').exists()).toBe(true)
    wrapper.unmount()
  })
})

describe('US-P6 — domknięcie', () => {
  it('ends on the numbers, the skipped files and the quiz', async () => {
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

    // The quiz composed in the background when the walkthrough started is waiting here.
    expect(done.findAll('.quiz-question')).toHaveLength(2)
    expect(done.find('.walk-finish').text()).toBe('Wróć do listy (PR niezakończony)')

    // One action returns to a file that was skipped.
    await wrapper.find('.walk-skipped .critical-open').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Krok 3 z 6')
    wrapper.unmount()
  })
})

describe('quiz na koniec przeglądu', () => {
  async function walkToTheEnd(wrapper: ReturnType<typeof mount>) {
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()
    for (let index = 0; index < 6; index++) {
      await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
      await flushPromises()
    }
  }

  async function answer(wrapper: ReturnType<typeof mount>, questionId: string, text: string) {
    await wrapper.find(`#quiz-${questionId}`).setValue(text)
    await wrapper.find(`#quiz-${questionId}`).element.closest('li')!
      .querySelector<HTMLButtonElement>('.quiz-check')!.click()
    await flushPromises()
  }

  // US-P1 still holds: opening costs nothing. Starting the walkthrough is the click that
  // spends, and the path it saved is what the backend composes from.
  it('costs nothing on opening and starts in the background with the walkthrough', async () => {
    const wrapper = await openPr()
    expect(api.composeQuiz).not.toHaveBeenCalled()

    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()

    expect(api.composeQuiz).toHaveBeenCalledTimes(1)
    expect(api.composeQuiz).toHaveBeenCalledWith('project', 'repo-a', 123, false)
    expect(api.setReadingPath.mock.invocationCallOrder[0]!).toBeLessThan(api.composeQuiz.mock.invocationCallOrder[0]!)
    wrapper.unmount()
  })

  it('answers a wrong answer with a hint and a way back into the code, never the key', async () => {
    const wrapper = await openPr()
    await walkToTheEnd(wrapper)

    await answer(wrapper, 'q2', 'Nic.')

    expect(api.answerQuiz).toHaveBeenCalledWith('project', 'repo-a', 123, 'q2', 'Nic.')
    expect(wrapper.find('.walk-done .quiz-wrong').text()).toContain('Brakuje konfiguracji')
    expect(wrapper.find('.walk-done').text()).not.toContain('Wzorcowo')
    expect(wrapper.find('.walk-finish').text()).toBe('Wróć do listy (PR niezakończony)')

    // h.cs is not on the reading path, so it opens in the tree — where the rail keeps the quiz.
    const links = wrapper.findAll('.walk-done .quiz-files .critical-open')
    await links[1]!.trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-done').exists()).toBe(false)
    expect(api.fileDiff.mock.lastCall?.[3]).toBe('/src/h.cs')
    expect(wrapper.find('.quiz-section .quiz-wrong').exists()).toBe(true)
    wrapper.unmount()
  })

  it('returns into the walkthrough for a file on the path', async () => {
    const wrapper = await openPr()
    await walkToTheEnd(wrapper)

    await wrapper.findAll('.walk-done .quiz-files .critical-open')[0]!.trigger('click')
    await flushPromises()

    expect(wrapper.find('.walk-progress').text()).toBe('Krok 2 z 6')
    wrapper.unmount()
  })

  it('calls the pull request done only when every question is passed', async () => {
    const wrapper = await openPr()
    await walkToTheEnd(wrapper)

    await answer(wrapper, 'q1', 'Jest dobrze.')
    expect(wrapper.findAll('.walk-done .quiz-question--passed')).toHaveLength(1)
    expect(wrapper.find('.walk-done').text()).toContain('Wzorcowo')
    expect(wrapper.find('.pr-header-progress').text()).toBe('Quiz 1 / 2')
    expect(wrapper.find('.walk-finish').text()).toBe('Wróć do listy (PR niezakończony)')

    await answer(wrapper, 'q2', 'Też dobrze.')
    expect(wrapper.find('.quiz-status--passed').text()).toContain('PR zakończony')
    expect(wrapper.find('.walk-finish').text()).toBe('Zakończ ten PR ✓')
    expect(wrapper.find('.pr-header-progress').text()).toBe('Quiz ✓')
    wrapper.unmount()
  })

  // The model may still be composing when the reviewer leaves and comes back. The request
  // is not aborted — the backend saves its result — and coming back joins it, never pays twice.
  it('joins a quiz still being composed instead of starting a second one', async () => {
    let finish!: (value: ReturnType<typeof quiz>) => void
    api.composeQuiz.mockReturnValue(new Promise(resolve => { finish = resolve }))
    const wrapper = await openPr()
    await wrapper.find('.walk-primary').trigger('click')
    await flushPromises()

    await wrapper.find('.back-button').trigger('click')
    await flushPromises()
    await wrapper.find('.pr-row').trigger('click')
    await flushPromises()
    expect(wrapper.find('.pr-header-progress').text()).toBe('Quiz…')

    finish(quiz())
    await flushPromises()
    expect(api.composeQuiz).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.pr-header-progress').text()).toBe('Quiz 0 / 2')
    wrapper.unmount()
  })
})

describe('US-P7 — wznowienie', () => {
  it('offers the interrupted walkthrough back, from the file it stopped on', async () => {
    api.fileReviews.mockResolvedValue({
      files: [], readingPath: readingPath({ paths: paths.slice(0, 6), position: 4, headCommitSha: HEAD }), updatedAt: null,
    })

    const wrapper = await openPr()

    expect(wrapper.find('.walk-resume').text()).toContain('krok 5 z 6')
    expect(wrapper.find('.walk-changed').exists()).toBe(false)
    await wrapper.find('.walk-resume .walk-primary').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-progress').text()).toBe('Krok 5 z 6')
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
    expect(wrapper.find('.walk-resume').text()).toContain('krok 2 z 2')
    await wrapper.find('.walk-resume .walk-primary').trigger('click')
    await flushPromises()
    expect(wrapper.find('.walk-bar-file').attributes('title')).toBe('/src/b.cs')
    wrapper.unmount()
  })
})


// The second pass, after the author pushed fixes. Everything here is computed from markers
// the browser already holds, so what is pinned hardest is that none of it calls the model.
describe('runda po poprawkach', () => {
  const ITERATIONS = [{ id: 1, sourceCommitSha: OTHER_HEAD }, { id: 2, sourceCommitSha: HEAD }]
  const withIterations = { ...details, iterations: ITERATIONS }
  // Read at the previous push: a.cs has moved since (its blob no longer matches), b.cs has not.
  const markers = [
    { path: '/src/a.cs', blobId: 'f'.repeat(40), headSha: OTHER_HEAD, updatedAt: '2026-09-01T12:00:00Z' },
    { path: '/src/b.cs', blobId: '1'.repeat(40), headSha: OTHER_HEAD, updatedAt: '2026-09-01T12:00:00Z' },
  ]

  function withMarkers(files = markers) {
    api.pullRequests.mockResolvedValue([withIterations])
    api.pullRequest.mockResolvedValue(withIterations)
    api.fileReviews.mockResolvedValue({ files, readingPath: readingPath(), updatedAt: null })
  }

  it('opens on what moved or was never read, and leaves an unchanged file out', async () => {
    withMarkers()

    const wrapper = await openPr()

    expect(wrapper.find('.walk-round').exists()).toBe(true)
    // a.cs is back because its content moved; b.cs is not, because it did not. The lockfile
    // is noise and stays out of the default the way it does in every other mode.
    expect(pickedPaths(wrapper)).toEqual(['/src/a.cs', ...paths.slice(2)])
    expect(wrapper.find('.walk-round-counts').text()).toContain('1 plik')
    expect(wrapper.find('.walk-entry').text()).not.toContain('package-lock.json')
    expect(api.setReadingPath).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('is not offered for a first pass that is merely unfinished', async () => {
    // Same markers, stamped at the head the pull request still has: nobody pushed anything,
    // so there is no second round — just a first one left in the middle.
    withMarkers([{ path: '/src/a.cs', blobId: '0'.repeat(40), headSha: HEAD, updatedAt: '2026-09-01T12:00:00Z' }])

    const wrapper = await openPr()

    expect(wrapper.find('.walk-round').exists()).toBe(false)
    expect(pickedPaths(wrapper)).toEqual(paths.slice(1, 6))
    wrapper.unmount()
  })

  it('opens each file on the changes that arrived after you last saw it', async () => {
    withMarkers()

    const wrapper = await openPr()
    await wrapper.find('.walk-actions .walk-primary').trigger('click')
    await flushPromises()

    // a.cs was last seen at iteration 1, so the diff is asked for against iteration 1.
    expect(api.fileDiff).toHaveBeenLastCalledWith('project', 'repo-a', 123, '/src/a.cs', 1)

    await wrapper.find('.walk-actions--sticky .walk-primary').trigger('click')
    await flushPromises()
    // c.cs has no marker, so there is nothing to measure from and the whole diff is asked for.
    expect(api.fileDiff).toHaveBeenLastCalledWith('project', 'repo-a', 123, '/src/c.cs', undefined)
    wrapper.unmount()
  })

  it('needs no Summary and never calls the model', async () => {
    withMarkers()
    api.savedSummary.mockResolvedValue(null)

    const wrapper = await openPr()

    expect(wrapper.find('.walk-round').exists()).toBe(true)
    expect(pickedPaths(wrapper)).toEqual(['/src/a.cs', ...paths.slice(2)])
    // The "no Summary yet" prompt belongs to the first pass; the round has nothing to buy.
    expect(wrapper.find('.walk-no-summary').exists()).toBe(false)

    await wrapper.find('.walk-actions .walk-primary').trigger('click')
    await flushPromises()
    expect(api.generateSummary).not.toHaveBeenCalled()
    expect(api.explainFile).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})

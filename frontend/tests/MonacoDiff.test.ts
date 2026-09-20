// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as monaco from 'monaco-editor'
import MonacoDiff from '../src/MonacoDiff.vue'

const mocks = vi.hoisted(() => ({
  csharpHovers: vi.fn(),
  models: [] as { uri: { toString: () => string }; dispose: ReturnType<typeof vi.fn> }[],
  registerHoverProvider: vi.fn(),
  provider: null as null | { provideHover: (model: unknown, position: { lineNumber: number; column: number }) => unknown },
  disposeProvider: vi.fn(),
  registerSemanticProvider: vi.fn(),
  semanticProvider: null as null | { getLegend: () => { tokenTypes: string[] }; provideDocumentSemanticTokens: (model: unknown) => { data: Uint32Array } },
  disposeSemanticProvider: vi.fn(),
  defineTheme: vi.fn(),
  setTheme: vi.fn(),
  originalUpdate: vi.fn(),
  modifiedUpdate: vi.fn(),
  diffUpdateOptions: vi.fn(),
  goToDiff: vi.fn(),
  revealFirstDiff: vi.fn(),
  onDidUpdateDiff: vi.fn(),
  onMouseDown: vi.fn(),
  decorationsSet: vi.fn(),
  decorationsClear: vi.fn(),
  disposeGlyphListener: vi.fn(),
}))

vi.mock('../src/api', () => ({ api: { csharpHovers: mocks.csharpHovers } }))
vi.mock('monaco-editor', () => ({
  editor: {
    defineTheme: mocks.defineTheme,
    setTheme: mocks.setTheme,
    createModel: vi.fn(() => {
      const id = mocks.models.length
      const model = { uri: { toString: () => `model-${id}` }, dispose: vi.fn() }
      mocks.models.push(model)
      return model
    }),
    TrackedRangeStickiness: { NeverGrowsWhenTypingAtEdges: 1 },
    MouseTargetType: { GUTTER_GLYPH_MARGIN: 2 },
    createDiffEditor: vi.fn(() => ({
      setModel: vi.fn(), layout: vi.fn(), dispose: vi.fn(),
      updateOptions: mocks.diffUpdateOptions,
      goToDiff: mocks.goToDiff,
      revealFirstDiff: mocks.revealFirstDiff,
      onDidUpdateDiff: mocks.onDidUpdateDiff,
      getOriginalEditor: () => ({ updateOptions: mocks.originalUpdate }),
      getModifiedEditor: () => ({
        updateOptions: mocks.modifiedUpdate,
        focus: vi.fn(),
        getPosition: () => ({ lineNumber: 12 }),
        onMouseDown: mocks.onMouseDown,
        createDecorationsCollection: () => ({ set: mocks.decorationsSet, clear: mocks.decorationsClear }),
      }),
    })),
  },
  languages: {
    getLanguages: () => [{ id: 'csharp', extensions: ['.cs'] }, { id: 'typescript', extensions: ['.ts'] }],
    registerHoverProvider: mocks.registerHoverProvider,
    registerDocumentSemanticTokensProvider: mocks.registerSemanticProvider,
  },
  Range: class {
    constructor(public startLineNumber: number, public startColumn: number,
      public endLineNumber: number, public endColumn: number) {}
  },
}))

beforeEach(() => {
  vi.resetAllMocks()
  mocks.models.length = 0
  mocks.provider = null
  mocks.semanticProvider = null
  mocks.csharpHovers.mockResolvedValue({
    original: [],
    modified: [{ startLine: 1, startColumn: 5, endLine: 1, endColumn: 10, signature: 'string value' }],
    originalTokens: [],
    modifiedTokens: [
      { line: 2, startColumn: 3, endColumn: 7, kind: 'method' },
      { line: 1, startColumn: 5, endColumn: 10, kind: 'variable' },
    ],
  })
  mocks.registerHoverProvider.mockImplementation((_language, provider) => {
    mocks.provider = provider
    return { dispose: mocks.disposeProvider }
  })
  mocks.registerSemanticProvider.mockImplementation((_language, provider) => {
    mocks.semanticProvider = provider
    return { dispose: mocks.disposeSemanticProvider }
  })
  mocks.onDidUpdateDiff.mockReturnValue({ dispose: vi.fn() })
  mocks.onMouseDown.mockReturnValue({ dispose: mocks.disposeGlyphListener })
  window.matchMedia = vi.fn().mockReturnValue({
    matches: false,
    addEventListener() {},
    removeEventListener() {},
  }) as unknown as typeof window.matchMedia
  globalThis.ResizeObserver = class {
    observe() {}
    disconnect() {}
    unobserve() {}
  }
})

describe('Monaco C# hover', () => {
  it('shows the type only on the matching diff model and disposes the provider', async () => {
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/Sample.cs', originalPath: null, originalText: 'old', modifiedText: 'new' },
    })
    await flushPromises()

    expect(mocks.csharpHovers).toHaveBeenCalledWith('old', 'new', expect.any(AbortSignal))
    expect(mocks.defineTheme).toHaveBeenCalledWith('pr-cockpit-code', expect.objectContaining({
      rules: expect.arrayContaining([
        expect.objectContaining({ token: 'type.identifier.ts' }),
        expect.objectContaining({ token: 'identifier.ts' }),
        expect.objectContaining({ token: 'identifier.cs' }),
      ]),
    }))
    expect(mocks.registerHoverProvider).toHaveBeenCalledWith('csharp', expect.any(Object))
    expect(mocks.registerSemanticProvider).toHaveBeenCalledWith('csharp', expect.any(Object))
    expect(mocks.originalUpdate).toHaveBeenCalledWith({ 'semanticHighlighting.enabled': true })
    expect(mocks.modifiedUpdate).toHaveBeenCalledWith({ 'semanticHighlighting.enabled': true })
    const provider = mocks.provider!
    expect(provider.provideHover(mocks.models[0], { lineNumber: 1, column: 6 })).toBeNull()
    expect(provider.provideHover(mocks.models[1], { lineNumber: 1, column: 6 })).toEqual({
      range: { startLineNumber: 1, startColumn: 5, endLineNumber: 1, endColumn: 10 },
      contents: [{ value: '```csharp\nstring value\n```' }],
    })
    const semantic = mocks.semanticProvider!
    expect(Array.from(semantic.provideDocumentSemanticTokens(mocks.models[0]).data)).toEqual([])
    expect(Array.from(semantic.provideDocumentSemanticTokens(mocks.models[1]).data)).toEqual([
      0, 4, 5, semantic.getLegend().tokenTypes.indexOf('variable'), 0,
      1, 2, 4, semantic.getLegend().tokenTypes.indexOf('method'), 0,
    ])

    wrapper.unmount()
    expect(mocks.disposeProvider).toHaveBeenCalledOnce()
    expect(mocks.disposeSemanticProvider).toHaveBeenCalledOnce()
  })

  it('does not request C# analysis for TypeScript files', () => {
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/sample.ts', originalPath: null, originalText: 'old', modifiedText: 'new' },
    })

    expect(mocks.csharpHovers).not.toHaveBeenCalled()
    expect(mocks.registerSemanticProvider).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('does not register a late response after closing the file', async () => {
    let finish!: (value: unknown) => void
    mocks.csharpHovers.mockReturnValue(new Promise(resolve => { finish = resolve }))
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/Sample.cs', originalPath: null, originalText: 'old', modifiedText: 'new' },
    })
    const signal = mocks.csharpHovers.mock.calls[0]![2] as AbortSignal

    wrapper.unmount()
    finish({ original: [], modified: [], originalTokens: [], modifiedTokens: [] })
    await flushPromises()

    expect(signal.aborted).toBe(true)
    expect(mocks.registerHoverProvider).not.toHaveBeenCalled()
    expect(mocks.registerSemanticProvider).not.toHaveBeenCalled()
  })
})

describe('Monaco reading options', () => {
  it('collapses unchanged regions and opens on the first change', () => {
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/sample.ts', originalPath: null, originalText: 'old', modifiedText: 'new' },
    })

    const options = vi.mocked(monaco.editor.createDiffEditor).mock.calls[0]![1]!
    expect(options.hideUnchangedRegions)
      .toEqual({ enabled: true, revealLineCount: 20, minimumLineCount: 6, contextLineCount: 3 })
    expect(options.renderSideBySide).toBe(false)

    // The one-shot listener must release itself so later recomputes cannot move the view.
    const onFirstDiff = mocks.onDidUpdateDiff.mock.calls[0]![0] as () => void
    onFirstDiff()
    expect(mocks.revealFirstDiff).toHaveBeenCalledOnce()

    wrapper.unmount()
  })

  it('switches between inline and side-by-side without recreating the editor', async () => {
    const wrapper = mount(MonacoDiff, {
      props: {
        path: '/src/sample.ts', originalPath: null,
        originalText: 'old', modifiedText: 'new', sideBySide: false,
      },
    })

    await wrapper.setProps({ sideBySide: true })

    expect(mocks.diffUpdateOptions).toHaveBeenCalledWith({ renderSideBySide: true })
    expect(vi.mocked(monaco.editor.createDiffEditor)).toHaveBeenCalledOnce()
    wrapper.unmount()
  })

  it('defines both themes and picks the light one when the system is light', () => {
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/sample.ts', originalPath: null, originalText: 'old', modifiedText: 'new' },
    })

    const themes = mocks.defineTheme.mock.calls.map(call => call[0])
    expect(themes).toContain('pr-cockpit-code')
    expect(themes).toContain('pr-cockpit-code-dark')
    expect(vi.mocked(monaco.editor.createDiffEditor).mock.calls[0]![1]!.theme).toBe('pr-cockpit-code')
    wrapper.unmount()
  })

  it('marks commented lines on the gutter and reports a click on the marker', async () => {
    const wrapper = mount(MonacoDiff, {
      props: {
        path: '/src/a.ts', originalPath: null, originalText: 'a', modifiedText: 'b',
        commentLines: [4, 9],
      },
    })
    await flushPromises()

    const decorations = mocks.decorationsSet.mock.calls[0]![0] as { range: { startLineNumber: number } }[]
    expect(decorations.map(entry => entry.range.startLineNumber)).toEqual([4, 9])

    // Only the glyph margin counts: a click in the code itself must not open a comment.
    const handler = mocks.onMouseDown.mock.calls[0]![0] as (event: unknown) => void
    handler({ target: { type: 6, position: { lineNumber: 4 } } })
    expect(wrapper.emitted('openLine')).toBeUndefined()
    handler({ target: { type: 2, position: { lineNumber: 4 } } })
    expect(wrapper.emitted('openLine')).toEqual([[4]])

    // Anchoring is right-hand side only, so the cursor line comes from the modified editor.
    expect((wrapper.vm as unknown as { cursorLine(): number }).cursorLine()).toBe(12)

    wrapper.unmount()
    expect(mocks.disposeGlyphListener).toHaveBeenCalled()
  })
})

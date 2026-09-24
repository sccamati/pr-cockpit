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
  onMouseMove: vi.fn(),
  onMouseLeave: vi.fn(),
  hoverSet: vi.fn(),
  setPosition: vi.fn(),
  addZone: vi.fn(),
  removeZone: vi.fn(),
  layoutZone: vi.fn(),
  originalAddZone: vi.fn(),
  originalRemoveZone: vi.fn(),
  originalLayoutZone: vi.fn(),
  resizeCallbacks: [] as (() => void)[],
  revealLineInCenter: vi.fn(),
  modifiedEditor: null as unknown,
  decorationsSet: vi.fn(),
  decorationsClear: vi.fn(),
  disposeGlyphListener: vi.fn(),
  addAction: vi.fn(),
  selection: null as unknown,
  selectedText: '',
}))

vi.mock('../src/api', () => ({ api: { csharpHovers: mocks.csharpHovers } }))
vi.mock('monaco-editor', () => ({
  editor: {
    defineTheme: mocks.defineTheme,
    setTheme: mocks.setTheme,
    createModel: vi.fn(() => {
      const id = mocks.models.length
      const model = { uri: { toString: () => `model-${id}` }, dispose: vi.fn(), getLineCount: () => 500 }
      mocks.models.push(model)
      return model
    }),
    TrackedRangeStickiness: { NeverGrowsWhenTypingAtEdges: 1 },
    MouseTargetType: { GUTTER_LINE_DECORATIONS: 2, GUTTER_LINE_NUMBERS: 3 },
    createDiffEditor: vi.fn(() => ({
      setModel: vi.fn(), layout: vi.fn(), dispose: vi.fn(),
      updateOptions: mocks.diffUpdateOptions,
      goToDiff: mocks.goToDiff,
      revealFirstDiff: mocks.revealFirstDiff,
      onDidUpdateDiff: mocks.onDidUpdateDiff,
      getLineChanges: () => [],
      getOriginalEditor: () => ({
        updateOptions: mocks.originalUpdate, addAction: mocks.addAction,
        getSelection: () => null, getModel: () => null,
        // The comment blocks are mirrored here as blank spacers, so this pane needs the
        // zone accessor too.
        changeViewZones: (change: (accessor: unknown) => void) => change({
          addZone: mocks.originalAddZone,
          removeZone: mocks.originalRemoveZone,
          layoutZone: mocks.originalLayoutZone,
        }),
      }),
      // One stable modified editor per diff editor: the component keeps two decoration
      // collections, and a fresh object per call would hand it two fresh ones each time.
      getModifiedEditor: () => mocks.modifiedEditor,
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
  mocks.addAction.mockReturnValue({ dispose: vi.fn() })
  mocks.selection = null
  mocks.selectedText = ''
  let collections = 0
  mocks.modifiedEditor = {
    updateOptions: mocks.modifiedUpdate,
    addAction: mocks.addAction,
    getSelection: () => mocks.selection,
    getModel: () => ({ getValueInRange: () => mocks.selectedText }),
    focus: vi.fn(),
    getPosition: () => ({ lineNumber: 12 }),
    setPosition: mocks.setPosition,
    // Monaco's zone accessor, reduced to what the component actually calls.
    changeViewZones: (change: (accessor: unknown) => void) => change({
      addZone: mocks.addZone,
      removeZone: mocks.removeZone,
      layoutZone: mocks.layoutZone,
    }),
    revealLineInCenter: mocks.revealLineInCenter,
    onMouseDown: mocks.onMouseDown,
    onMouseMove: mocks.onMouseMove,
    onMouseLeave: mocks.onMouseLeave,
    // First collection is the comment markers, second is the hover "+".
    createDecorationsCollection: () => ({
      set: collections++ === 0 ? mocks.decorationsSet : mocks.hoverSet,
      clear: vi.fn(),
    }),
  }
  mocks.addZone.mockImplementation(() => `zone-${mocks.addZone.mock.calls.length}`)
  mocks.originalAddZone.mockImplementation(() => `twin-${mocks.originalAddZone.mock.calls.length}`)
  mocks.resizeCallbacks = []
  mocks.onMouseDown.mockReturnValue({ dispose: mocks.disposeGlyphListener })
  mocks.onMouseMove.mockReturnValue({ dispose: vi.fn() })
  mocks.onMouseLeave.mockReturnValue({ dispose: vi.fn() })
  window.matchMedia = vi.fn().mockReturnValue({
    matches: false,
    addEventListener() {},
    removeEventListener() {},
  }) as unknown as typeof window.matchMedia
  globalThis.ResizeObserver = class {
    constructor(callback: () => void) { mocks.resizeCallbacks.push(callback) }
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

  it('puts a comment container between the lines and emits it for the parent to fill', async () => {
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/a.ts', originalPath: null, originalText: 'a', modifiedText: 'b', zoneLines: [12] },
    })
    await flushPromises()

    const zone = mocks.addZone.mock.calls[0]![0] as { afterLineNumber: number; domNode: HTMLElement; suppressMouseDown: boolean }
    expect(zone.afterLineNumber).toBe(12)
    // Must stay false: suppressMouseDown makes the editor preventDefault the mousedown,
    // which kills focus in the textarea and every button click inside the block.
    expect(zone.suppressMouseDown).toBe(false)
    expect(wrapper.emitted('zones')?.at(-1)?.[0]).toEqual([{ line: 12, el: zone.domNode.firstElementChild }])

    // A line that no longer has a comment loses its container.
    await wrapper.setProps({ zoneLines: [] })
    await flushPromises()
    expect(mocks.removeZone).toHaveBeenCalled()
    expect(wrapper.emitted('zones')?.at(-1)?.[0]).toEqual([])

    wrapper.unmount()
  })

  // Monaco keeps both panes at the same scroll position and clamps each to its own
  // content height, so a block that only exists on the right makes the tail of the file
  // unreachable. The blank twin on the left is what buys those pixels back.
  it('mirrors a comment block into the original editor, at the same height', async () => {
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/a.ts', originalPath: null, originalText: 'a', modifiedText: 'b', zoneLines: [12] },
    })
    await flushPromises()

    const twin = mocks.originalAddZone.mock.calls[0]![0] as { afterLineNumber: number; heightInPx: number }
    expect(twin.afterLineNumber).toBe(12)

    const zone = mocks.addZone.mock.calls[0]![0] as { domNode: HTMLElement; heightInPx: number }
    const content = zone.domNode.firstElementChild!
    Object.defineProperty(content, 'offsetHeight', { value: 180, configurable: true })
    mocks.resizeCallbacks.forEach(callback => callback())
    expect(zone.heightInPx).toBe(180)
    expect(twin.heightInPx).toBe(180)
    expect(mocks.originalLayoutZone).toHaveBeenCalled()

    // Folding the comment has to give the space back. Monaco pins the outer node's height,
    // so only the inner one can report that it got smaller.
    Object.defineProperty(content, 'offsetHeight', { value: 30, configurable: true })
    mocks.resizeCallbacks.forEach(callback => callback())
    expect(zone.heightInPx).toBe(30)
    expect(twin.heightInPx).toBe(30)

    await wrapper.setProps({ zoneLines: [] })
    await flushPromises()
    expect(mocks.originalRemoveZone).toHaveBeenCalledWith('twin-1')

    wrapper.unmount()
  })

  it('scrolls a commented line into the middle of the view', async () => {
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/a.ts', originalPath: null, originalText: 'a', modifiedText: 'b', commentLines: [56] },
    })
    await flushPromises()

    ;(wrapper.vm as unknown as { revealLine(line: number): void }).revealLine(56)

    // Centred rather than merely scrolled to, so the code around the comment is visible.
    expect(mocks.setPosition).toHaveBeenCalledWith({ lineNumber: 56, column: 1 })
    expect(mocks.revealLineInCenter).toHaveBeenCalledWith(56)
    wrapper.unmount()
  })

  it('offers a plus on the hovered line, but not where a comment already is', async () => {
    const wrapper = mount(MonacoDiff, {
      props: {
        path: '/src/a.ts', originalPath: null, originalText: 'a', modifiedText: 'b',
        commentLines: [4],
      },
    })
    await flushPromises()
    const move = mocks.onMouseMove.mock.calls[0]![0] as (event: unknown) => void
    const leave = mocks.onMouseLeave.mock.calls[0]![0] as () => void

    move({ target: { position: { lineNumber: 8 } } })
    const shown = mocks.hoverSet.mock.calls.at(-1)![0] as { range: { startLineNumber: number } }[]
    expect(shown.map(entry => entry.range.startLineNumber)).toEqual([8])

    // Line 4 already has a thread, so it keeps its marker instead of offering a second one.
    move({ target: { position: { lineNumber: 4 } } })
    expect(mocks.hoverSet.mock.calls.at(-1)![0]).toEqual([])

    move({ target: { position: { lineNumber: 8 } } })
    leave()
    expect(mocks.hoverSet.mock.calls.at(-1)![0]).toEqual([])
    wrapper.unmount()
  })

  it('marks commented lines in the decorations strip and reports a click on the marker', async () => {
    const wrapper = mount(MonacoDiff, {
      props: {
        path: '/src/a.ts', originalPath: null, originalText: 'a', modifiedText: 'b',
        commentLines: [4, 9],
      },
    })
    await flushPromises()

    const decorations = mocks.decorationsSet.mock.calls[0]![0] as { range: { startLineNumber: number } }[]
    expect(decorations.map(entry => entry.range.startLineNumber)).toEqual([4, 9])

    // The decorations strip and the line number both open a comment; the code does not.
    const handler = mocks.onMouseDown.mock.calls[0]![0] as (event: unknown) => void
    handler({ target: { type: 6, position: { lineNumber: 4 } } })
    expect(wrapper.emitted('openLine')).toBeUndefined()
    handler({ target: { type: 2, position: { lineNumber: 4 } } })
    handler({ target: { type: 3, position: { lineNumber: 7 } } })
    expect(wrapper.emitted('openLine')).toEqual([[4], [7]])

    // Anchoring is right-hand side only, so the cursor line comes from the modified editor.
    expect((wrapper.vm as unknown as { cursorLine(): number }).cursorLine()).toBe(12)

    wrapper.unmount()
    expect(mocks.disposeGlyphListener).toHaveBeenCalled()
  })

  it('offers the ask action on both panes and emits the selected snippet', async () => {
    const wrapper = mount(MonacoDiff, {
      props: { path: '/src/a.ts', originalPath: null, originalText: 'old', modifiedText: 'new' },
    })
    await flushPromises()

    // Both panes, because in side-by-side the old version on the left is just as readable.
    expect(mocks.addAction).toHaveBeenCalledTimes(2)
    const action = mocks.addAction.mock.calls[0]![0] as { label: string; run: () => void }
    expect(action.label).toBe('Zapytaj AI o zaznaczenie')

    // Nothing selected is nothing to ask about.
    action.run()
    expect(wrapper.emitted('ask')).toBeUndefined()

    mocks.selection = { startLineNumber: 1, startColumn: 1, endLineNumber: 1, endColumn: 8 }
    mocks.selectedText = 'const x'
    action.run()
    expect(wrapper.emitted('ask')).toEqual([['const x']])
    wrapper.unmount()
  })
})

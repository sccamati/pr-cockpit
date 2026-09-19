// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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
  originalUpdate: vi.fn(),
  modifiedUpdate: vi.fn(),
}))

vi.mock('../src/api', () => ({ api: { csharpHovers: mocks.csharpHovers } }))
vi.mock('monaco-editor', () => ({
  editor: {
    defineTheme: mocks.defineTheme,
    createModel: vi.fn(() => {
      const id = mocks.models.length
      const model = { uri: { toString: () => `model-${id}` }, dispose: vi.fn() }
      mocks.models.push(model)
      return model
    }),
    createDiffEditor: vi.fn(() => ({
      setModel: vi.fn(), layout: vi.fn(), dispose: vi.fn(),
      getOriginalEditor: () => ({ updateOptions: mocks.originalUpdate }),
      getModifiedEditor: () => ({ updateOptions: mocks.modifiedUpdate }),
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

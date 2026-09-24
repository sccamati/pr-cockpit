<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as monaco from 'monaco-editor'
import EditorWorker from 'monaco-editor/editor/editor.worker?worker'
import JsonWorker from 'monaco-editor/languages/features/json/json.worker?worker'
import CssWorker from 'monaco-editor/languages/features/css/css.worker?worker'
import HtmlWorker from 'monaco-editor/languages/features/html/html.worker?worker'
import TsWorker from 'monaco-editor/languages/features/typescript/ts.worker?worker'
import { api, type CodeDeclaration, type CSharpHoverEntry, type CSharpSemanticToken, type UsageSource } from '@/api'
import { usageLabel } from '@/lib/format'

const props = defineProps<{
  path: string
  originalPath: string | null
  originalText: string
  modifiedText: string
  sideBySide?: boolean
  // Lines of this file that already carry a comment thread, as Azure DevOps numbers them,
  // split so a resolved conversation stops shouting for attention.
  commentLines?: number[]
  resolvedLines?: number[]
  // Lines that should carry a comment block rendered between the code, in place.
  zoneLines?: number[]
  // Where the declarations of this file are used. Absent, the editor shows no usage counts.
  usages?: UsageSource
}>()
const emit = defineEmits<{
  openLine: [line: number]
  // The containers Monaco created for those lines; the parent teleports its own markup in,
  // so the conversation stays ordinary Vue instead of hand-built DOM.
  zones: [zones: { line: number; el: HTMLElement }[]]
  // The snippet the reader right-clicked. This component only knows what is selected; the
  // parent owns the conversation.
  ask: [text: string]
}>()
const container = ref<HTMLElement | null>(null)
let editor: monaco.editor.IStandaloneDiffEditor | null = null
let askActions: monaco.IDisposable[] = []
let originalModel: monaco.editor.ITextModel | null = null
let modifiedModel: monaco.editor.ITextModel | null = null
let resizeObserver: ResizeObserver | null = null
let hoverRegistration: monaco.IDisposable | null = null
let semanticRegistration: monaco.IDisposable | null = null
let hoverAbort: AbortController | null = null
let firstDiffListener: monaco.IDisposable | null = null
let diffZoneListener: monaco.IDisposable | null = null
let glyphListener: monaco.IDisposable | null = null
let hoverMoveListener: monaco.IDisposable | null = null
let hoverLeaveListener: monaco.IDisposable | null = null
let glyphs: monaco.editor.IEditorDecorationsCollection | null = null
const zoneIds = new Map<number, string>()
const zoneNodes = new Map<number, HTMLElement>()
const zoneObservers = new Map<number, ResizeObserver>()
const twinIds = new Map<number, string>()
const twinZones = new Map<number, monaco.editor.IViewZone>()
let hoverGlyphs: monaco.editor.IEditorDecorationsCollection | null = null
let hoveredLine: number | null = null
let usageAbort: AbortController | null = null
let usageRegistrations: monaco.IDisposable[] = []
// Files a usage points into, opened read-only so the peek can preview them. Keyed by path.
const referenceModels = new Map<string, monaco.editor.ITextModel>()

const semanticTokenTypes = [
  'namespace', 'class', 'interface', 'struct', 'enum', 'delegate', 'typeParameter',
  'method', 'property', 'field', 'event', 'variable', 'parameter', 'enumMember',
]
const semanticLegend = { tokenTypes: semanticTokenTypes, tokenModifiers: [] }
let codeThemeConfigured = false

function encodeSemanticTokens(tokens: CSharpSemanticToken[]): Uint32Array {
  const data: number[] = []
  let previousLine = 0
  let previousColumn = 0
  for (const token of [...tokens].sort((a, b) => a.line - b.line || a.startColumn - b.startColumn)) {
    const type = semanticTokenTypes.indexOf(token.kind)
    if (type < 0 || token.endColumn <= token.startColumn) continue
    const line = token.line - 1
    const column = token.startColumn - 1
    data.push(line - previousLine, line === previousLine ? column - previousColumn : column,
      token.endColumn - token.startColumn, type, 0)
    previousLine = line
    previousColumn = column
  }
  return new Uint32Array(data)
}

// Light and dark foregrounds for the same semantic token, defined once so the two
// themes cannot drift apart. Dark values follow VS Code Dark+ so C# reads familiar.
const codeTokens: { scope: string; light: string; dark: string }[] = [
  { scope: 'identifier.cs', light: '294257', dark: 'C3D2E0' },
  { scope: 'identifier.ts', light: '294257', dark: 'C3D2E0' },
  { scope: 'namespace', light: '466B8A', dark: '8FB3CE' },
  { scope: 'class', light: '087589', dark: '4EC9B0' },
  { scope: 'interface', light: '087589', dark: '4EC9B0' },
  { scope: 'struct', light: '087589', dark: '4EC9B0' },
  { scope: 'enum', light: '98540D', dark: 'D7A05A' },
  { scope: 'delegate', light: '087589', dark: '4EC9B0' },
  { scope: 'typeParameter', light: '087589', dark: '4EC9B0' },
  { scope: 'method', light: '79522A', dark: 'DCC08A' },
  { scope: 'property', light: '174F8A', dark: '8FC0EE' },
  { scope: 'field', light: '174F8A', dark: '8FC0EE' },
  { scope: 'event', light: '79522A', dark: 'DCC08A' },
  { scope: 'variable', light: '243B53', dark: 'CBD7E4' },
  { scope: 'parameter', light: '36536B', dark: 'A9BED0' },
  { scope: 'enumMember', light: '98540D', dark: 'D7A05A' },
  { scope: 'type.identifier.ts', light: '087589', dark: '4EC9B0' },
]

// ponytail: the component owns theme detection instead of taking it as a prop, so App.vue
// never has to listen on a media query. Ceiling: no manual light/dark override.
const darkQuery = typeof window.matchMedia === 'function'
  ? window.matchMedia('(prefers-color-scheme: dark)')
  : null

function themeName(): string {
  return darkQuery?.matches ? 'pr-cockpit-code-dark' : 'pr-cockpit-code'
}

function applyTheme() {
  monaco.editor.setTheme(themeName())
}

function configureCodeThemes() {
  if (codeThemeConfigured) return
  monaco.editor.defineTheme('pr-cockpit-code', {
    base: 'vs',
    inherit: true,
    rules: codeTokens.map(entry => ({ token: entry.scope, foreground: entry.light })),
    colors: {},
  })
  monaco.editor.defineTheme('pr-cockpit-code-dark', {
    base: 'vs-dark',
    inherit: true,
    rules: codeTokens.map(entry => ({ token: entry.scope, foreground: entry.dark })),
    colors: {},
  })
  codeThemeConfigured = true
}

Object.assign(self, {
  MonacoEnvironment: {
    getWorker(_id: string, label: string): Worker {
      if (label === 'json') return new JsonWorker()
      if (['css', 'scss', 'less'].includes(label)) return new CssWorker()
      if (['html', 'handlebars', 'razor'].includes(label)) return new HtmlWorker()
      if (label === 'typescript' || label === 'javascript') return new TsWorker()
      return new EditorWorker()
    },
  },
})

function languageForPath(path: string): string {
  const name = path.split('/').pop()?.toLowerCase() ?? ''
  if (name.endsWith('.vue')) return 'html'
  const extension = name.includes('.') ? `.${name.split('.').pop()}` : ''
  const language = monaco.languages.getLanguages().find(entry =>
    entry.filenames?.some(filename => filename.toLowerCase() === name) ||
    entry.extensions?.some(candidate => candidate.toLowerCase() === extension))
  return language?.id ?? 'plaintext'
}

async function loadCSharpHovers() {
  if (!originalModel || !modifiedModel) return
  const original = originalModel
  const modified = modifiedModel
  hoverAbort = new AbortController()
  try {
    const hovers = await api.csharpHovers(props.originalText, props.modifiedText, hoverAbort.signal)
    if (hoverAbort.signal.aborted) return
    const byModel = new Map([
      [original.uri.toString(), hovers.original],
      [modified.uri.toString(), hovers.modified],
    ])
    const tokensByModel = new Map([
      [original.uri.toString(), hovers.originalTokens],
      [modified.uri.toString(), hovers.modifiedTokens],
    ])
    hoverRegistration = monaco.languages.registerHoverProvider('csharp', {
      provideHover(model, position) {
        const entries = byModel.get(model.uri.toString())
        const entry = entries?.find((item: CSharpHoverEntry) =>
          item.startLine === position.lineNumber &&
          item.startColumn <= position.column && position.column < item.endColumn)
        if (!entry) return null
        return {
          range: new monaco.Range(entry.startLine, entry.startColumn, entry.endLine, entry.endColumn),
          contents: [{ value: `\`\`\`csharp\n${entry.signature}\n\`\`\`` }],
        }
      },
    })
    semanticRegistration = monaco.languages.registerDocumentSemanticTokensProvider('csharp', {
      getLegend: () => semanticLegend,
      provideDocumentSemanticTokens(model) {
        return { data: encodeSemanticTokens(tokensByModel.get(model.uri.toString()) ?? []) }
      },
      releaseDocumentSemanticTokens() {},
    })
  } catch (error) {
    if (!(error instanceof DOMException && error.name === 'AbortError')) {
      console.error('Nie udało się pobrać podpowiedzi C#.', error)
    }
  }
}

// Command ids are global in Monaco, so each mounted editor registers its own.
let usageCommands = 0
const usagePaths = /\.(cs|tsx?|m?js|vue)$/i

/**
 * A count over every declaration of the modified file, the way VS Code shows references:
 * CodeLens for the count, a reference provider so the built-in peek lists the places, and
 * the same provider serves "Go to References" from the context menu.
 */
async function loadUsages(language: string) {
  if (!props.usages || !modifiedModel) return
  const source = props.usages
  const modified = modifiedModel
  usageAbort = new AbortController()
  const signal = usageAbort.signal
  try {
    const result = await source.load(signal)
    if (signal.aborted) return
    const commandId = `prcockpit.usages.${++usageCommands}`
    usageRegistrations.push(monaco.editor.registerCommand(commandId,
      (_accessor, declaration: CodeDeclaration) => peekUsages(declaration)))
    usageRegistrations.push(monaco.languages.registerCodeLensProvider(language, {
      provideCodeLenses(model) {
        const lenses = model.uri.toString() !== modified.uri.toString() ? [] : result.declarations.map(declaration => ({
          range: new monaco.Range(declaration.line, 1, declaration.line, 1),
          // Nothing to open for zero usages: an empty id renders the lens as plain text.
          command: {
            id: declaration.usages.length > 0 ? commandId : '',
            title: usageLabel(declaration.usages, result.mode),
            arguments: [declaration],
          },
        }))
        return { lenses, dispose() {} }
      },
    }))
    usageRegistrations.push(monaco.languages.registerReferenceProvider(language, {
      async provideReferences(model, position) {
        if (model.uri.toString() !== modified.uri.toString()) return []
        const declaration = declarationAt(result.declarations, position)
        return declaration ? await usageLocations(source, declaration, signal) : []
      },
    }))
  } catch (error) {
    if (!(error instanceof DOMException && error.name === 'AbortError')) {
      console.error('Nie udało się policzyć użyć.', error)
    }
  }
}

// The declaration under the cursor, or the one whose usage in this same file it is on.
function declarationAt(declarations: CodeDeclaration[], position: monaco.Position): CodeDeclaration | undefined {
  const on = (line: number, start: number, end: number) =>
    line === position.lineNumber && start <= position.column && position.column <= end
  return declarations.find(item => on(item.line, item.startColumn, item.endColumn)) ??
    declarations.find(item => item.usages.some(usage =>
      usage.path === props.path && on(usage.line, usage.startColumn, usage.endColumn)))
}

async function usageLocations(source: UsageSource, declaration: CodeDeclaration, signal: AbortSignal) {
  const missing = [...new Set(declaration.usages.map(usage => usage.path))]
    .filter(path => path !== props.path && !referenceModels.has(path))
  await Promise.all(missing.map(async path => {
    const text = await source.source(path, signal)
    if (signal.aborted || referenceModels.has(path)) return
    const uri = monaco.Uri.from({ scheme: 'pr-ref', path })
    referenceModels.set(path, monaco.editor.getModel(uri) ?? monaco.editor.createModel(text, languageForPath(path), uri))
  }))
  return declaration.usages.flatMap(usage => {
    const uri = usage.path === props.path ? modifiedModel?.uri : referenceModels.get(usage.path)?.uri
    return uri ? [{ uri, range: new monaco.Range(usage.line, usage.startColumn, usage.line, usage.endColumn) }] : []
  })
}

// The lens only moves the cursor onto the name and asks for Monaco's own peek, so the
// click and "Peek References" from the context menu are one code path.
function peekUsages(declaration: CodeDeclaration) {
  const pane = editor?.getModifiedEditor()
  if (!pane) return
  pane.setPosition({ lineNumber: declaration.line, column: declaration.startColumn })
  pane.focus()
  pane.trigger('usages', 'editor.action.referenceSearch.trigger', null)
}

onMounted(() => {
  if (!container.value) return
  const language = languageForPath(props.path)
  const originalLanguage = languageForPath(props.originalPath ?? props.path)
  originalModel = monaco.editor.createModel(props.originalText, originalLanguage)
  modifiedModel = monaco.editor.createModel(props.modifiedText, language)
  configureCodeThemes()
  editor = monaco.editor.createDiffEditor(container.value, {
    theme: themeName(),
    readOnly: true,
    originalEditable: false,
    renderSideBySide: props.sideBySide ?? false,
    diffWordWrap: 'on',
    scrollBeyondLastLine: false,
    minimap: { enabled: false },
    fontSize: 14,
    lineHeight: 22,
    lineNumbersMinChars: 3,
    // Collapses the untouched stretches of a large file down to its actual hunks.
    hideUnchangedRegions: { enabled: true, revealLineCount: 20, minimumLineCount: 6, contextLineCount: 3 },
    renderWhitespace: 'selection',
    // The gutter menu only offers revert/stage, which a read-only viewer cannot do.
    renderGutterMenu: false,
    // The diff editor switches CodeLens off in its panes unless asked; the usage counts
    // ride on it. No other provider registers lenses, so nothing else appears.
    diffCodeLens: true,
    // Comment markers ride the line-decorations strip, not the glyph margin: the glyph
    // margin is ~26px of empty gutter that pushed the code sideways, and an inline diff
    // already spends two number columns on the left.
    glyphMargin: false,
    lineDecorationsWidth: 18,
    // ponytail: experimental by name — first thing to drop if moved blocks render oddly.
    experimental: { showMoves: true },
    // Lets the page keep scrolling once the editor reaches its end (stacked layout).
    scrollbar: { alwaysConsumeMouseWheel: false },
  })
  editor.setModel({ original: originalModel, modified: modifiedModel })
  editor.getOriginalEditor().updateOptions({ 'semanticHighlighting.enabled': true })
  editor.getModifiedEditor().updateOptions({ 'semanticHighlighting.enabled': true })
  resizeObserver = new ResizeObserver(() => editor?.layout())
  resizeObserver.observe(container.value)
  darkQuery?.addEventListener?.('change', applyTheme)
  // Open on the first change rather than an unchanged prologue. Disposed after one
  // shot so later recomputes never yank the view away from where the reader is.
  firstDiffListener = editor.onDidUpdateDiff(() => {
    firstDiffListener?.dispose()
    firstDiffListener = null
    editor?.revealFirstDiff?.()
  })
  // The diff editor rebuilds its own zones whenever the diff recomputes, so ours are
  // re-asserted afterwards rather than assumed to have survived.
  diffZoneListener = editor.onDidUpdateDiff(() => syncZones())
  const modified = editor.getModifiedEditor()
  // Both the margin icon and the line number open the conversation, because "click the
  // line" is what a reviewer reaches for and a 12-pixel icon is a poor target.
  glyphListener = modified.onMouseDown(event => {
    if (event.target.type !== monaco.editor.MouseTargetType.GUTTER_LINE_DECORATIONS &&
        event.target.type !== monaco.editor.MouseTargetType.GUTTER_LINE_NUMBERS) return
    const line = event.target.position?.lineNumber
    if (line) emit('openLine', line)
  })
  hoverMoveListener = modified.onMouseMove(event => setHoveredLine(event.target.position?.lineNumber ?? null))
  hoverLeaveListener = modified.onMouseLeave(() => setHoveredLine(null))
  // Right-click on a selection is the way in: a bare letter never reaches Monaco, because
  // the page takes shortcuts in the capture phase. Both panes, since in side-by-side the
  // old version on the left is just as readable.
  askActions = [modified, editor.getOriginalEditor()].map(pane => pane.addAction({
    id: 'prcockpit.ask',
    label: 'Zapytaj AI o zaznaczenie',
    contextMenuGroupId: 'navigation',
    run: () => {
      const text = selectedText()
      if (text) emit('ask', text)
    },
  }))
  refreshGlyphs()
  syncZones()
  if (language === 'csharp' || originalLanguage === 'csharp') void loadCSharpHovers()
  if (usagePaths.test(props.path) && !/\.min\.js$/i.test(props.path) && props.modifiedText) void loadUsages(language)
})

watch(() => props.zoneLines, syncZones, { deep: true })

watch(() => [props.commentLines, props.resolvedLines], () => {
  refreshGlyphs()
  // A line that just got a thread must lose its "+" without waiting for another mouse move.
  const line = hoveredLine
  hoveredLine = null
  setHoveredLine(line)
}, { deep: true })

watch(() => props.sideBySide, value => editor?.updateOptions({ renderSideBySide: value ?? false }))

// The selection can be on either side; the modified pane first, because that is where a
// reviewer spends the time. An empty selection yields an empty range, so nothing else to check.
function selectedText(): string {
  for (const pane of [editor?.getModifiedEditor(), editor?.getOriginalEditor()]) {
    const selection = pane?.getSelection()
    const text = selection ? pane?.getModel()?.getValueInRange(selection) ?? '' : ''
    if (text.trim()) return text
  }
  return ''
}

defineExpose({
  selectedText,
  goToDiff: (target: 'next' | 'previous') => editor?.goToDiff(target),
  focusEditor: () => editor?.getModifiedEditor().focus(),
  // Anchoring is always right-hand side, so the cursor line of the modified editor is the
  // line a new comment gets. The cursor works even though the editor is read-only.
  cursorLine: () => editor?.getModifiedEditor().getPosition()?.lineNumber ?? null,
  // Opening a comment must bring its line into view — otherwise the panel scrolls the
  // conversation into the space where the code was, and the line is nowhere to be seen.
  revealLine: (line: number) => {
    const modified = editor?.getModifiedEditor()
    if (!modified) return
    modified.setPosition({ lineNumber: line, column: 1 })
    modified.revealLineInCenter(line)
  },
})

// Always the modified editor. With renderSideBySide off, deleted lines are view zones with
// no addressable position, so the right-hand side is the only side a marker can live on.
// The affordance for starting a comment: hovering a line puts a "+" in its margin, the
// way a code review tool is expected to behave. Without it the margin looks inert and
// nobody discovers that a line can be commented at all.
function setHoveredLine(line: number | null) {
  if (line === hoveredLine || !editor) return
  hoveredLine = line
  hoverGlyphs ??= editor.getModifiedEditor().createDecorationsCollection()
  const commented = new Set([...(props.commentLines ?? []), ...(props.resolvedLines ?? [])])
  hoverGlyphs.set(line !== null && !commented.has(line)
    ? [{
        range: new monaco.Range(line, 1, line, 1),
        options: {
          linesDecorationsClassName: 'comment-add-glyph',
          linesDecorationsTooltip: 'Dodaj komentarz do tej linii',
        },
      }]
    : [])
}

/**
 * Comment blocks live between the code as Monaco view zones. The diff editor keeps zones
 * of its own — alignment on the left, deleted lines when the diff renders inline — so ours
 * are only ever added and removed one by one, never through a wholesale reset, and they
 * are re-asserted after the diff recomputes.
 *
 * Every block also gets a blank twin of the same height in the original editor. The two
 * editors scroll in lockstep and each clamps to its own content height, so without the
 * twin the original runs out of scroll first and drags the modified back — the tail of a
 * commented file becomes unreachable. The twin keeps the side-by-side panes aligned too.
 *
 * ponytail: the height comes from a ResizeObserver on the container rather than from
 * measuring the content ourselves. Ceiling: a zone briefly lags a very large paste.
 */
function syncZones() {
  if (!editor) return
  const modified = editor.getModifiedEditor()
  const wanted = new Set((props.zoneLines ?? []).filter(line => line > 0))
  const gone = [...zoneIds.keys()].filter(line => !wanted.has(line))
  const fresh = [...wanted].filter(line => !zoneIds.has(line))

  modified.changeViewZones(accessor => {
    for (const line of gone) {
      accessor.removeZone(zoneIds.get(line)!)
      zoneIds.delete(line)
      zoneObservers.get(line)?.disconnect()
      zoneObservers.delete(line)
      zoneNodes.delete(line)
    }

    for (const line of fresh) {
      const container = document.createElement('div')
      container.className = 'comment-zone'
      // Monaco pins the container to the zone's height, so its scrollHeight never shrinks
      // and a folded comment would keep its old space. The content goes in an inner node
      // that sizes itself, and that is what gets measured.
      const content = document.createElement('div')
      content.className = 'comment-zone-content'
      container.appendChild(content)
      const zone: monaco.editor.IViewZone = {
        afterLineNumber: line,
        domNode: container,
        heightInPx: 0,
        // Deliberately false. suppressMouseDown makes the editor call preventDefault on
        // mousedown over the zone, which kills focus in the textarea and the click on every
        // button inside it. The card stops propagation itself instead.
        suppressMouseDown: false,
      }
      const id = accessor.addZone(zone)
      zoneIds.set(line, id)
      zoneNodes.set(line, content)

      const observer = new ResizeObserver(() => {
        const height = content.offsetHeight
        if (height === zone.heightInPx) return
        zone.heightInPx = height
        modified.changeViewZones(inner => inner.layoutZone(id))
        resizeTwin(line, height)
      })
      observer.observe(content)
      zoneObservers.set(line, observer)
    }
  })

  editor.getOriginalEditor().changeViewZones(accessor => {
    for (const line of gone) {
      const id = twinIds.get(line)
      if (id) accessor.removeZone(id)
      twinIds.delete(line)
      twinZones.delete(line)
    }
    for (const line of fresh) {
      const twin: monaco.editor.IViewZone = {
        afterLineNumber: originalLineFor(line),
        domNode: document.createElement('div'),
        heightInPx: zoneNodes.get(line)?.offsetHeight ?? 0,
        suppressMouseDown: true,
      }
      twinIds.set(line, accessor.addZone(twin))
      twinZones.set(line, twin)
    }
  })

  emit('zones', [...zoneNodes].map(([line, el]) => ({ line, el })))
}

function resizeTwin(line: number, height: number) {
  const twin = twinZones.get(line)
  const id = twinIds.get(line)
  if (!twin || !id || twin.heightInPx === height) return
  twin.heightInPx = height
  editor?.getOriginalEditor().changeViewZones(accessor => accessor.layoutZone(id))
}

/**
 * Where a modified line sits in the original text, so the twin lands beside its comment.
 *
 * ponytail: a line inside a hunk takes that whole hunk's delta, so the twin can sit a few
 * lines off within it. Enough to keep the panes aligned; exactness needs the full mapping.
 */
function originalLineFor(modifiedLine: number): number {
  let line = modifiedLine
  for (const change of editor?.getLineChanges() ?? []) {
    if (change.modifiedStartLineNumber > modifiedLine) break
    const added = change.modifiedEndLineNumber === 0 ? 0 : change.modifiedEndLineNumber - change.modifiedStartLineNumber + 1
    const removed = change.originalEndLineNumber === 0 ? 0 : change.originalEndLineNumber - change.originalStartLineNumber + 1
    line += removed - added
  }
  return Math.min(Math.max(line, 0), originalModel?.getLineCount() ?? 0)
}

function clearZones() {
  for (const observer of zoneObservers.values()) observer.disconnect()
  zoneObservers.clear()
  editor?.getModifiedEditor().changeViewZones(accessor => {
    for (const id of zoneIds.values()) accessor.removeZone(id)
  })
  editor?.getOriginalEditor().changeViewZones(accessor => {
    for (const id of twinIds.values()) accessor.removeZone(id)
  })
  zoneIds.clear()
  zoneNodes.clear()
  twinIds.clear()
  twinZones.clear()
}

function refreshGlyphs() {
  if (!editor) return
  glyphs ??= editor.getModifiedEditor().createDecorationsCollection()
  const marker = (line: number, resolved: boolean) => ({
    range: new monaco.Range(line, 1, line, 1),
    options: {
      linesDecorationsClassName: resolved ? 'comment-glyph comment-glyph--resolved' : 'comment-glyph',
      linesDecorationsTooltip: resolved ? 'Rozwiązany komentarz' : 'Komentarz w tej linii',
      stickiness: monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges,
    },
  })
  glyphs.set([
    ...(props.commentLines ?? []).map(line => marker(line, false)),
    ...(props.resolvedLines ?? []).map(line => marker(line, true)),
  ])
}

onBeforeUnmount(() => {
  hoverAbort?.abort()
  usageAbort?.abort()
  usageRegistrations.forEach(registration => registration.dispose())
  usageRegistrations = []
  referenceModels.forEach(model => model.dispose())
  referenceModels.clear()
  askActions.forEach(action => action.dispose())
  clearZones()
  diffZoneListener?.dispose()
  glyphListener?.dispose()
  hoverMoveListener?.dispose()
  hoverLeaveListener?.dispose()
  glyphs?.clear()
  hoverGlyphs?.clear()
  firstDiffListener?.dispose()
  darkQuery?.removeEventListener?.('change', applyTheme)
  hoverRegistration?.dispose()
  semanticRegistration?.dispose()
  resizeObserver?.disconnect()
  editor?.dispose()
  originalModel?.dispose()
  modifiedModel?.dispose()
})
</script>

<template>
  <div ref="container" class="monaco-diff" role="region" :aria-label="`Zmiany w pliku ${path}`" />
</template>

<style scoped>
.monaco-diff { flex: 1; min-width: 0; min-height: 0; }
</style>

<style>
/* Not scoped: Monaco creates these nodes itself, outside this template. */
/* The gutter marker for a line that already has a thread, and the hover affordance for
   starting one on a line that does not. */
.comment-glyph, .comment-add-glyph { cursor: pointer; }
.comment-glyph::before { content: '💬'; display: block; font-size: 10px; line-height: 22px; text-align: center; }
.comment-add-glyph::before { content: '+'; display: block; font-size: 14px; line-height: 22px; text-align: center; color: var(--accent); font-weight: 700; }
/* A comment block rendered between the lines of code by a Monaco view zone. The editor
   gives it the full width of the content area, so the card provides its own inset. */
/* Monaco appends .view-lines AFTER .view-zones (view.js), and the line layer is an
   absolutely positioned box over the whole content — so it paints over the comment
   block and eats every click in it, cursor included. The zone node is already
   position:absolute, so one z-index lifts it back above the code. */
/* Monaco wylacza zaznaczanie na calym edytorze, zeby samemu zarzadzac selekcja kodu,
   a nasze karty komentarzy siedza w jego strefach i to dziedzicza. Tutaj jest zwykly
   tekst do przeczytania i skopiowania, wiec zaznaczanie wraca. Kod obok zostaje pod
   kontrola Monaka. */
.comment-zone { width: 100%; z-index: 2; cursor: default; -webkit-user-select: text; user-select: text; }
/* flow-root keeps the card's margins inside, so offsetHeight is the height the zone needs. */
.comment-zone-content { display: flow-root; }
.comment-glyph--resolved { opacity: .45; }
</style>

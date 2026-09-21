<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as monaco from 'monaco-editor'
import EditorWorker from 'monaco-editor/editor/editor.worker?worker'
import JsonWorker from 'monaco-editor/languages/features/json/json.worker?worker'
import CssWorker from 'monaco-editor/languages/features/css/css.worker?worker'
import HtmlWorker from 'monaco-editor/languages/features/html/html.worker?worker'
import TsWorker from 'monaco-editor/languages/features/typescript/ts.worker?worker'
import { api, type CSharpHoverEntry, type CSharpSemanticToken } from './api'

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
let hoverGlyphs: monaco.editor.IEditorDecorationsCollection | null = null
let hoveredLine: number | null = null

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
 * ponytail: the height comes from a ResizeObserver on the container rather than from
 * measuring the content ourselves. Ceiling: a zone briefly lags a very large paste.
 */
function syncZones() {
  if (!editor) return
  const modified = editor.getModifiedEditor()
  const wanted = new Set((props.zoneLines ?? []).filter(line => line > 0))

  modified.changeViewZones(accessor => {
    for (const [line, id] of [...zoneIds]) {
      if (wanted.has(line)) continue
      accessor.removeZone(id)
      zoneIds.delete(line)
      zoneObservers.get(line)?.disconnect()
      zoneObservers.delete(line)
      zoneNodes.delete(line)
    }

    for (const line of wanted) {
      if (zoneIds.has(line)) continue
      const container = document.createElement('div')
      container.className = 'comment-zone'
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
      zoneNodes.set(line, container)

      const observer = new ResizeObserver(() => {
        const height = container.scrollHeight
        if (height === zone.heightInPx) return
        zone.heightInPx = height
        modified.changeViewZones(inner => inner.layoutZone(id))
      })
      observer.observe(container)
      zoneObservers.set(line, observer)
    }
  })

  emit('zones', [...zoneNodes].map(([line, el]) => ({ line, el })))
}

function clearZones() {
  for (const observer of zoneObservers.values()) observer.disconnect()
  zoneObservers.clear()
  const modified = editor?.getModifiedEditor()
  modified?.changeViewZones(accessor => {
    for (const id of zoneIds.values()) accessor.removeZone(id)
  })
  zoneIds.clear()
  zoneNodes.clear()
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

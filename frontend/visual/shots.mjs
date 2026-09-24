// Visual regression check: the real App on fixtures (visual/main.ts), screenshotted in
// headless Chrome and compared pixel by pixel with a baseline taken on this machine.
//
//   npm run visual:baseline [-- scenario ...]   capture visual/out/baseline (before a change)
//   npm run visual [-- scenario ...]            capture visual/out/current and compare
//
// Baselines are not committed: fonts and the browser build change the pixels, so a baseline
// only means something on the machine that took it. Take it before the change, check after.
// ponytail: plain pixel counting with a noise threshold. Two runs of the same code differ by
// a few dozen antialiased pixels on button corners; a real CSS regression is thousands.
// Perceptual diffing (pixelmatch) is the upgrade if the threshold ever hides a real change.
import { spawn } from 'node:child_process'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.join(HERE, 'out')
const THRESHOLD = Number(process.env.VISUAL_THRESHOLD ?? 150)
const VARIANTS = [
  { name: 'light', width: 1600, height: 1000, dark: false },
  { name: 'dark', width: 1600, height: 1000, dark: true },
  { name: 'narrow', width: 700, height: 1400, dark: false },
]
const CHROMES = [
  process.env.CHROME,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser',
].filter(Boolean)
const sleep = ms => new Promise(r => setTimeout(r, ms))

async function openBrowser() {
  const executable = CHROMES.find(candidate => fs.existsSync(candidate))
  if (!executable) throw new Error('No Chrome or Edge found. Set CHROME to the browser executable.')
  const port = 9300 + Math.floor(Math.random() * 500)
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'prcockpit-visual-'))
  const proc = spawn(executable, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
    '--hide-scrollbars', '--force-device-scale-factor=1', '--disable-gpu', '--font-render-hinting=none', 'about:blank'], { stdio: 'ignore' })
  let targets
  for (let i = 0; i < 100 && !targets; i++) {
    try { targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json() } catch { await sleep(100) }
  }
  if (!targets) { proc.kill(); throw new Error('The browser did not open its debugging port.') }
  const ws = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl)
  await new Promise(r => ws.addEventListener('open', r))
  let id = 0
  const pending = new Map()
  ws.addEventListener('message', event => {
    const msg = JSON.parse(event.data)
    if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id) }
  })
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const n = ++id
    pending.set(n, msg => msg.error ? reject(new Error(`${method}: ${msg.error.message}`)) : resolve(msg.result))
    ws.send(JSON.stringify({ id: n, method, params }))
  })
  const evaluate = async expression =>
    (await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true })).result.value
  return {
    send, evaluate,
    async close() {
      ws.close()
      const exited = new Promise(r => proc.once('exit', r))
      proc.kill()
      await exited
      // Chrome's helper processes can hold the profile a moment longer; a leftover temp dir is harmless.
      try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }) } catch { /* left for the OS */ }
    },
  }
}

async function load(browser, url) {
  await browser.send('Page.navigate', { url })
  for (let i = 0; i < 300; i++) {
    await sleep(100)
    const ready = await browser.evaluate('window.__ready').catch(() => undefined)
    if (ready) return ready
  }
  return 'timeout'
}

async function capture(browser, base, dir, only) {
  fs.rmSync(dir, { recursive: true, force: true })
  fs.mkdirSync(dir, { recursive: true })
  // Warm-up: the first visit makes Vite optimize Monaco and reload the page mid-scenario.
  await load(browser, `${base}?s=opened`)
  const scenarios = only.length ? only : await browser.evaluate('window.__scenarios')
  const problems = []
  for (const variant of VARIANTS) {
    await browser.send('Emulation.setDeviceMetricsOverride', { width: variant.width, height: variant.height, deviceScaleFactor: 1, mobile: false })
    await browser.send('Emulation.setEmulatedMedia', { features: [
      { name: 'prefers-color-scheme', value: variant.dark ? 'dark' : 'light' },
      { name: 'prefers-reduced-motion', value: 'reduce' },
    ] })
    for (const scenario of scenarios) {
      const ready = await load(browser, `${base}?s=${scenario}${scenario === 'readonly' ? '&ro' : ''}`)
      if (ready !== 'ok') problems.push(`${variant.name}-${scenario}: ${ready}`)
      const unmocked = await browser.evaluate('window.__unmocked')
      if (unmocked?.length) problems.push(`${variant.name}-${scenario}: unmocked ${[...new Set(unmocked)].join(', ')}`)
      // Focus rings and the caret must not differ between runs.
      await browser.evaluate(`document.activeElement?.blur?.(); document.querySelectorAll('.cursor').forEach(c => c.style.visibility = 'hidden')`)
      const { data } = await browser.send('Page.captureScreenshot', { format: 'png' })
      fs.writeFileSync(path.join(dir, `${variant.name}-${scenario}.png`), Buffer.from(data, 'base64'))
    }
  }
  return problems
}

async function compare(browser, baseline, current, diffDir) {
  fs.rmSync(diffDir, { recursive: true, force: true })
  fs.mkdirSync(diffDir, { recursive: true })
  const failures = []
  for (const file of fs.readdirSync(current).filter(f => f.endsWith('.png')).sort()) {
    const before = path.join(baseline, file)
    if (!fs.existsSync(before)) { failures.push(`${file}: no baseline`); continue }
    const a = fs.readFileSync(before), b = fs.readFileSync(path.join(current, file))
    if (a.equals(b)) { console.log(`  ${file}: identical`); continue }
    const result = await browser.evaluate(`(async () => {
      const load = src => new Promise(r => { const i = new Image(); i.onload = () => r(i); i.src = src })
      const [a, b] = await Promise.all([load('data:image/png;base64,${a.toString('base64')}'), load('data:image/png;base64,${b.toString('base64')}')])
      if (a.width !== b.width || a.height !== b.height) return { size: true }
      const c = document.createElement('canvas'); c.width = a.width; c.height = a.height
      const x = c.getContext('2d'); x.drawImage(a, 0, 0); const da = x.getImageData(0, 0, c.width, c.height)
      x.drawImage(b, 0, 0); const db = x.getImageData(0, 0, c.width, c.height)
      const out = x.createImageData(c.width, c.height); let n = 0
      for (let i = 0; i < da.data.length; i += 4) {
        const same = da.data[i] === db.data[i] && da.data[i+1] === db.data[i+1] && da.data[i+2] === db.data[i+2]
        if (!same) { n++; out.data[i] = 255 } else { out.data[i] = out.data[i+1] = out.data[i+2] = da.data[i] / 3 + 170 }
        out.data[i+3] = 255
      }
      x.putImageData(out, 0, 0)
      return { n, png: c.toDataURL('image/png').split(',')[1] }
    })()`)
    if (result.size) { failures.push(`${file}: size differs`); continue }
    fs.writeFileSync(path.join(diffDir, file), Buffer.from(result.png, 'base64'))
    const line = `${file}: ${result.n} px differ`
    if (result.n > THRESHOLD) failures.push(line)
    else console.log(`  ${line} (noise)`)
  }
  return failures
}

const [mode = 'check', ...only] = process.argv.slice(2)
if (!['baseline', 'check'].includes(mode)) { console.error('Usage: shots.mjs baseline|check [scenario ...]'); process.exit(2) }
const server = await createServer({ root: path.dirname(HERE), server: { port: 7191, strictPort: false, open: false }, logLevel: 'warn' })
await server.listen()
const base = `${server.resolvedUrls.local[0].replace(/\/$/, '')}/visual/index.html`
const browser = await openBrowser()
let exitCode = 0
try {
  const target = path.join(OUT, mode === 'baseline' ? 'baseline' : 'current')
  const problems = await capture(browser, base, target, only)
  if (problems.length) { exitCode = 1; console.error('Scenario problems:\n  ' + problems.join('\n  ')) }
  if (mode === 'baseline') console.log(`Baseline in ${target}`)
  else {
    const failures = await compare(browser, path.join(OUT, 'baseline'), target, path.join(OUT, 'diff'))
    if (failures.length) {
      exitCode = 1
      console.error(`Changed beyond ${THRESHOLD} px (red pixels in ${path.join(OUT, 'diff')}):\n  ` + failures.join('\n  '))
    } else if (!problems.length) console.log('No visual change.')
  }
} finally {
  await browser.close()
  await server.close()
}
process.exit(exitCode)

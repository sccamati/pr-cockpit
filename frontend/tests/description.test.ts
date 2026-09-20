// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { commentPreview, renderComment, renderDescription } from '../src/description'

function render(text: string, workItems: { id: string; url: string }[] = []) {
  const host = document.createElement('div')
  host.innerHTML = renderDescription(text, workItems)
  return host
}

describe('render opisu PR', () => {
  it('turns Azure DevOps markdown into real elements', () => {
    const host = render('## Tytuł\n\n- pierwszy\n- drugi\n\nTekst z [linkiem](https://example.com).')

    expect(host.querySelector('h2')?.textContent).toBe('Tytuł')
    expect(host.querySelectorAll('li')).toHaveLength(2)
    expect(host.querySelector('a')?.getAttribute('href')).toBe('https://example.com')
  })

  it('keeps a single newline as a line break, the way Azure DevOps does', () => {
    expect(render('pierwsza\ndruga').querySelectorAll('br')).toHaveLength(1)
  })

  it('renders fenced code without treating it as markup', () => {
    const host = render('```\nvar x = "<b>nie tag</b>";\n```')

    expect(host.querySelector('pre code')?.textContent).toContain('<b>nie tag</b>')
    expect(host.querySelector('pre b')).toBeNull()
  })

  // The description is written by other people. This is the one place where their text
  // becomes HTML, so these assertions are the security boundary, not a nicety.
  it('does not let raw HTML through', () => {
    const host = render('<script>window.pwned = 1</script>\n\n<img src=x onerror="window.pwned = 1">')

    expect(host.querySelector('script')).toBeNull()
    expect(host.querySelector('img')).toBeNull()
    // The markup survives as visible text, never as an element or an event attribute.
    const attributes = [...host.querySelectorAll('*')].flatMap(element => [...element.attributes])
    expect(attributes.filter(attribute => attribute.name.startsWith('on'))).toHaveLength(0)
    expect(host.innerHTML).toContain('&lt;img')
    expect((window as unknown as { pwned?: number }).pwned).toBeUndefined()
  })

  it('refuses a javascript: link', () => {
    const host = render('[klik](javascript:window.pwned = 1)')

    expect(host.querySelector('a')?.getAttribute('href') ?? '').not.toContain('javascript:')
  })

  it('opens every link in a new tab without handing over the opener', () => {
    const link = render('[a](https://example.com)').querySelector('a')!

    expect(link.getAttribute('target')).toBe('_blank')
    expect(link.getAttribute('rel')).toBe('noopener noreferrer')
  })

  it('replaces an attachment image with a link, because it would render broken', () => {
    const host = render('![diagram](https://dev.azure.com/org/_apis/wit/attachments/abc)')

    expect(host.querySelector('img')).toBeNull()
    const link = host.querySelector('a.description-attachment')!
    expect(link.getAttribute('href')).toBe('https://dev.azure.com/org/_apis/wit/attachments/abc')
    expect(link.textContent).toContain('diagram')
  })

  it('links a work item mention only when the pull request really has it', () => {
    const host = render('Naprawia #1234 oraz #9999, ale nie `#1234` w kodzie.',
      [{ id: '1234', url: 'https://dev.azure.com/org/_workitems/edit/1234' }])

    const links = [...host.querySelectorAll('a')]
    expect(links).toHaveLength(1)
    expect(links[0]!.textContent).toBe('#1234')
    expect(links[0]!.getAttribute('href')).toBe('https://dev.azure.com/org/_workitems/edit/1234')
    expect(host.querySelector('code')?.textContent).toBe('#1234')
    expect(host.textContent).toContain('#9999')
  })
})

describe('komentarze', () => {
  it('renders a comment as Markdown and drops tooling markers', () => {
    const host = document.createElement('div')
    host.innerHTML = renderComment('<!--review-swarm-->\n**[security]** MEDIUM\n\n`Program.cs:62`')

    expect(host.textContent).not.toContain('review-swarm')
    expect(host.querySelector('strong')?.textContent).toBe('[security]')
    expect(host.querySelector('code')?.textContent).toBe('Program.cs:62')
  })

  it('sanitises a comment the same way a description is sanitised', () => {
    const host = document.createElement('div')
    host.innerHTML = renderComment('<img src=x onerror=alert(1)>[klik](javascript:alert(1))')

    // html:false escapes the tag, so "onerror" survives as visible text and never as an
    // attribute — that is the escaping working, not a leak.
    expect(host.querySelector('img')).toBeNull()
    expect(host.querySelector('[onerror]')).toBeNull()
    expect(host.textContent).toContain('<img src=x onerror=alert(1)>')
    // markdown-it refuses a javascript: target outright, so no link is produced at all.
    expect(host.querySelector('a[href^="javascript:"]')).toBeNull()
  })

  it('strips Markdown punctuation for a one-line preview', () => {
    expect(commentPreview('<!--x-->**Problem:** `ekobill`\n\n---\n\nnie dostaje parametrow'))
      .toBe('Problem: ekobill nie dostaje parametrow')
  })
})

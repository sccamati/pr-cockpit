import MarkdownIt from 'markdown-it'
import DOMPurify from 'dompurify'
import type { WorkItem } from '@/api'

// A pull request description is untrusted text written by other people, so it gets two
// independent layers: `html: false` makes markdown-it escape raw HTML instead of passing
// it through, and DOMPurify sanitises the result in case the first layer ever slips.
// `breaks: true` matches how Azure DevOps treats a single newline.
const markdown = new MarkdownIt({ html: false, linkify: true, breaks: true })

const workItemPattern = /#(\d+)/g
// Tooling leaves markers like <!--review-swarm--> in comments. Azure DevOps hides them as
// HTML comments; with html:false markdown-it would print them, so they go first.
const htmlComment = /<!--[\s\S]*?-->/g

/**
 * A pull request comment, through the same two sanitising layers as the description.
 * Comments are Markdown in Azure DevOps, and showing the source instead of the result
 * turned every bold heading and code span into noise.
 */
export function renderComment(text: string): string {
  return renderDescription(text.replace(htmlComment, ''))
}

/** One plain line for a list, with the Markdown punctuation taken off rather than rendered. */
export function commentPreview(text: string): string {
  return text
    .replace(htmlComment, '')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/[*_`>#]/g, '')
    .replace(/^\s*[-=]{3,}\s*$/gm, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

export function renderDescription(text: string, workItems: readonly WorkItem[] = []): string {
  const fragment = DOMPurify.sanitize(markdown.render(text), { RETURN_DOM_FRAGMENT: true })
  const host = document.createElement('div')
  host.append(fragment)
  hardenLinks(host)
  replaceImages(host)
  linkWorkItems(host, workItems)
  return host.innerHTML
}

function harden(link: HTMLAnchorElement) {
  link.setAttribute('target', '_blank')
  link.setAttribute('rel', 'noopener noreferrer')
}

function hardenLinks(host: HTMLElement) {
  host.querySelectorAll('a[href]').forEach(link => harden(link as HTMLAnchorElement))
}

// Azure DevOps attachments need authentication, so an <img> here always renders broken.
// A link works instead, because the browser that opens it has the user's ADO session.
// ponytail: images are not visible in place. Upgrade path: proxy attachments through the
// backend, which holds the PAT.
function replaceImages(host: HTMLElement) {
  host.querySelectorAll('img[src]').forEach(image => {
    const link = document.createElement('a')
    link.href = image.getAttribute('src') ?? ''
    link.className = 'description-attachment'
    const alt = image.getAttribute('alt')?.trim()
    link.textContent = alt ? `📎 ${alt}` : '📎 obrazek w Azure DevOps'
    harden(link)
    image.replaceWith(link)
  })
}

function linkWorkItems(host: HTMLElement, workItems: readonly WorkItem[]) {
  if (workItems.length === 0) return
  const urls = new Map(workItems.map(item => [item.id, item.url]))
  const walker = document.createTreeWalker(host, NodeFilter.SHOW_TEXT)
  const targets: Text[] = []
  while (walker.nextNode()) {
    const node = walker.currentNode as Text
    // Never rewrite inside code or an existing link.
    if (node.parentElement?.closest('a, code, pre')) continue
    if (workItemPattern.test(node.data)) targets.push(node)
    workItemPattern.lastIndex = 0
  }

  for (const node of targets) {
    const replacement = document.createDocumentFragment()
    let last = 0
    for (const match of node.data.matchAll(workItemPattern)) {
      const url = urls.get(match[1]!)
      if (!url) continue
      const start = match.index!
      if (start > last) replacement.append(node.data.slice(last, start))
      const link = document.createElement('a')
      link.href = url
      link.textContent = match[0]
      harden(link)
      replacement.append(link)
      last = start + match[0].length
    }
    if (last === 0) continue
    if (last < node.data.length) replacement.append(node.data.slice(last))
    node.replaceWith(replacement)
  }
}

export interface TreeFile {
  path: string
  name: string
  changeType: string
  originalPath: string | null
  reviewed: boolean
  critical: boolean
  criticalDisabled: boolean
  selected: boolean
}

export interface TreeFolder {
  key: string
  label: string
  folders: TreeFolder[]
  files: TreeFile[]
  total: number
  reviewedCount: number
  containsSelected: boolean
  open: boolean
}

interface RawNode {
  name: string
  children: Map<string, RawNode>
  files: TreeFile[]
}

/**
 * Groups changed files into a folder tree. Chains of folders that hold nothing but one
 * subfolder are merged into a single label, so a path like
 * `/apps/ekobill/server/Ekobill.Server.Modules/Ppe/PpeService.cs` costs one header row
 * instead of five — the whole point when every file in a PR shares a deep prefix.
 *
 * The caller passes files already filtered by search and by the unreviewed toggle, so the
 * tree always describes exactly what is on screen.
 */
export function buildFileTree(files: readonly TreeFile[], expandAll: boolean): TreeFolder {
  const root: RawNode = { name: '', children: new Map(), files: [] }
  for (const file of files) {
    const segments = file.path.split('/').filter(Boolean)
    segments.pop()
    let node = root
    for (const segment of segments) {
      let child = node.children.get(segment)
      if (!child) {
        child = { name: segment, children: new Map(), files: [] }
        node.children.set(segment, child)
      }
      node = child
    }
    node.files.push(file)
  }
  return toFolder(root, '', expandAll)
}

function toFolder(node: RawNode, prefix: string, expandAll: boolean): TreeFolder {
  let label = node.name
  let current = node
  while (current.files.length === 0 && current.children.size === 1) {
    const only = current.children.values().next().value as RawNode
    label = label ? `${label}/${only.name}` : only.name
    current = only
  }

  const key = prefix ? `${prefix}/${label}` : label
  const folders = [...current.children.values()].map(child => toFolder(child, key, expandAll))
  const files = current.files
  const total = files.length + folders.reduce((sum, folder) => sum + folder.total, 0)
  const reviewedCount = files.filter(file => file.reviewed).length +
    folders.reduce((sum, folder) => sum + folder.reviewedCount, 0)
  const containsSelected = files.some(file => file.selected) ||
    folders.some(folder => folder.containsSelected)

  return {
    key,
    label,
    folders,
    files,
    total,
    reviewedCount,
    containsSelected,
    // A folder you have finished folds itself away, which is the point of the tree on a
    // 44-file pull request. Searching or holding the open file always wins.
    open: expandAll || containsSelected || reviewedCount < total,
  }
}

/**
 * The paths in the order the tree renders them: subfolders first, then the files sitting
 * directly in a folder. Keyboard navigation walks this, so "next file" always means the
 * next one you can actually see rather than the next one Azure DevOps happened to list.
 */
export function flattenTree(folder: TreeFolder): string[] {
  return [...folder.folders.flatMap(flattenTree), ...folder.files.map(file => file.path)]
}

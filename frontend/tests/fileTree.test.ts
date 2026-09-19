import { describe, expect, it } from 'vitest'
import { buildFileTree, flattenTree, type TreeFile } from '../src/fileTree'

function file(path: string, overrides: Partial<TreeFile> = {}): TreeFile {
  return {
    path,
    name: path.split('/').pop()!,
    changeType: 'Zmieniono',
    originalPath: null,
    reviewed: false,
    critical: false,
    criticalDisabled: false,
    selected: false,
    ...overrides,
  }
}

describe('drzewo plików', () => {
  it('merges a chain of single-child folders into one header', () => {
    const tree = buildFileTree([
      file('/apps/ekobill/server/Ekobill.Server.Modules/Ppe/PpeService.cs'),
      file('/apps/ekobill/server/Ekobill.Server.Modules/Ppe/PpeRowValidator.cs'),
    ], false)

    // The whole shared prefix costs one row, not five.
    expect(tree.label).toBe('apps/ekobill/server/Ekobill.Server.Modules/Ppe')
    expect(tree.folders).toHaveLength(0)
    expect(tree.files.map(entry => entry.name)).toEqual(['PpeService.cs', 'PpeRowValidator.cs'])
  })

  it('stops merging where the tree actually branches', () => {
    const tree = buildFileTree([
      file('/src/server/Ppe/PpeService.cs'),
      file('/src/client/App.vue'),
    ], false)

    expect(tree.label).toBe('src')
    expect(tree.folders.map(folder => folder.label).sort()).toEqual(['client', 'server/Ppe'])
  })

  it('counts reviewed files through every level', () => {
    const tree = buildFileTree([
      file('/src/a/one.cs', { reviewed: true }),
      file('/src/b/two.cs'),
      file('/src/b/three.cs', { reviewed: true }),
    ], false)

    expect(tree.total).toBe(3)
    expect(tree.reviewedCount).toBe(2)
    const b = tree.folders.find(folder => folder.label === 'b')!
    expect(`${b.reviewedCount}/${b.total}`).toBe('1/2')
  })

  it('folds away a folder that is fully read, but not one holding the open file', () => {
    const tree = buildFileTree([
      file('/src/done/one.cs', { reviewed: true }),
      file('/src/open/two.cs', { reviewed: true, selected: true }),
      file('/src/left/three.cs'),
    ], false)

    const byLabel = Object.fromEntries(tree.folders.map(folder => [folder.label, folder]))
    expect(byLabel['done']!.open).toBe(false)
    expect(byLabel['open']!.open).toBe(true)
    expect(byLabel['left']!.open).toBe(true)
  })

  it('opens everything while a search is running', () => {
    const tree = buildFileTree([file('/src/done/one.cs', { reviewed: true })], true)

    expect(tree.open).toBe(true)
    expect(tree.folders.every(folder => folder.open)).toBe(true)
  })

  it('handles a file sitting at the repository root', () => {
    const tree = buildFileTree([file('/README.md'), file('/src/one.cs')], false)

    expect(tree.label).toBe('')
    expect(tree.files.map(entry => entry.name)).toEqual(['README.md'])
    expect(tree.folders.map(folder => folder.label)).toEqual(['src'])
  })

  it('walks files in the order the tree shows them, not the order the API listed them', () => {
    // Azure DevOps interleaves the folders; the tree groups them, so navigation must too.
    const tree = buildFileTree([
      file('/src/a/one.cs'),
      file('/src/b/two.cs'),
      file('/src/a/three.cs'),
    ], false)

    expect(flattenTree(tree)).toEqual(['/src/a/one.cs', '/src/a/three.cs', '/src/b/two.cs'])
  })

  it('puts subfolders before the files sitting directly in a folder', () => {
    const tree = buildFileTree([
      file('/src/root.cs'),
      file('/src/deep/nested.cs'),
    ], false)

    expect(flattenTree(tree)).toEqual(['/src/deep/nested.cs', '/src/root.cs'])
  })
})

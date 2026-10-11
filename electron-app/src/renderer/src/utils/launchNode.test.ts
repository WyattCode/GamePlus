import { describe, it, expect } from 'vitest'
import { useSmartNodeForLaunch } from './launchNode'

describe('useSmartNodeForLaunch', () => {
  it('smart mode with a manual node → smart node', () => {
    expect(useSmartNodeForLaunch('smart', false, true)).toBe(true)
  })

  it('keep mode with a manual node → keep the manual node', () => {
    expect(useSmartNodeForLaunch('keep', false, true)).toBe(false)
  })

  it('keep mode with smartNode already true → smart node', () => {
    expect(useSmartNodeForLaunch('keep', true, true)).toBe(true)
  })

  it('keep mode with manual flag but no selected node → smart node', () => {
    expect(useSmartNodeForLaunch('keep', false, false)).toBe(true)
  })

  it('undefined mode behaves like keep', () => {
    expect(useSmartNodeForLaunch(undefined, false, true)).toBe(false)
    expect(useSmartNodeForLaunch(undefined, true, true)).toBe(true)
    expect(useSmartNodeForLaunch(undefined, false, false)).toBe(true)
  })
})

import type { LaunchNodeMode } from '@shared/types'

/**
 * Decides whether the launch should use the smart node instead of a manually picked node.
 * Returns true when the mode is 'smart', or when there is no usable manual choice
 * (smart mode is already active, or no node is currently selected). Unknown/undefined
 * modes behave like 'keep'.
 */
export function useSmartNodeForLaunch(
  mode: LaunchNodeMode | undefined | null,
  smartNode: boolean,
  hasSelectedNode: boolean
): boolean {
  if (mode === 'smart') return true
  if (smartNode) return true
  if (!hasSelectedNode) return true
  return false
}

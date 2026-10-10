import { backend } from '@/api/backend'

/**
 * Collects a one-click diagnostic bundle and saves it to a user-chosen path.
 *
 * Flow: gather client-side info (Electron main) → ask the elevated backend to build the zip in
 * memory (base64) → hand it to the main process, which shows the save dialog and writes the file.
 * In a plain browser preview (no preload bridge) the bundle is downloaded via a Blob instead.
 *
 * Returns the saved path, or null when the user cancelled the save dialog.
 */
export async function exportDiagnosticBundle(): Promise<string | null> {
  const client = await collectClient()
  const bundle = await backend.exportDiagnosticBundle(client)

  const save = window.gameplus?.diag?.saveBundle
  if (save) {
    return await save(bundle.fileName, bundle.base64)
  }

  // Browser preview fallback: trigger a download of the zip.
  downloadBase64(bundle.fileName, bundle.base64)
  return bundle.fileName
}

async function collectClient(): Promise<unknown> {
  const collect = window.gameplus?.diag?.collectClient
  if (!collect) return {}
  try {
    return await collect()
  } catch {
    return {}
  }
}

function downloadBase64(fileName: string, base64: string): void {
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  const blob = new Blob([bytes], { type: 'application/zip' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

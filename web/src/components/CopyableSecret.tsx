import { useState } from 'react'
import { Check, Copy } from 'lucide-react'

export function CopyableSecret({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false)

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard API can be unavailable (e.g. insecure context) - the value is
      // still selectable/readable in the box, so this is a soft failure.
    }
  }

  return (
    <div>
      <p className="mb-1.5 text-sm text-line-200">{label}</p>
      <div className="flex items-center gap-2 rounded-lg border border-ink-700 bg-ink-950 px-3 py-2">
        <code className="flex-1 overflow-x-auto whitespace-nowrap text-sm text-mint-300">{value}</code>
        <button
          type="button"
          onClick={handleCopy}
          aria-label={`Copy ${label}`}
          className="shrink-0 rounded-md p-1.5 text-muted-400 hover:bg-ink-800 hover:text-line-200"
        >
          {copied ? <Check className="h-4 w-4 text-green-500" /> : <Copy className="h-4 w-4" />}
        </button>
      </div>
    </div>
  )
}

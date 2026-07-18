import { useState, type ReactNode } from 'react'
import { Check, ChevronDown, Pause } from 'lucide-react'
import type { ChannelConnection } from '@/lib/types'

export function StatusPill({ status }: { status: ChannelConnection['status'] | 'NotConnected' }) {
  if (status === 'Active') {
    return (
      <span className="flex items-center gap-1 rounded-full bg-green-500/10 px-2 py-0.5 text-xs font-medium text-green-500">
        <Check className="h-3 w-3" /> Active
      </span>
    )
  }
  if (status === 'Paused') {
    return (
      <span className="flex items-center gap-1 rounded-full bg-ink-700 px-2 py-0.5 text-xs font-medium text-muted-400">
        <Pause className="h-3 w-3" /> Paused
      </span>
    )
  }
  if (status === 'Error') {
    return <span className="rounded-full bg-coral-500/10 px-2 py-0.5 text-xs font-medium text-coral-500">Error</span>
  }
  return <span className="rounded-full bg-ink-800 px-2 py-0.5 text-xs font-medium text-muted-400">Not connected</span>
}

/** Collapsible "How to connect" guide — collapsed by default so a returning
 *  agent who already knows the drill isn't stuck scrolling past instructions. */
export function GuideDisclosure({ title, children }: { title: string; children: ReactNode }) {
  const [isOpen, setIsOpen] = useState(false)
  return (
    <div className="mt-3 rounded-lg border border-ink-700">
      <button
        type="button"
        onClick={() => setIsOpen((v) => !v)}
        className="flex w-full items-center justify-between px-3 py-2 text-left text-xs font-medium text-muted-400 hover:text-line-200"
      >
        {title}
        <ChevronDown className={`h-3.5 w-3.5 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
      </button>
      {isOpen && <div className="space-y-2 border-t border-ink-700 px-3 py-3 text-xs text-muted-400">{children}</div>}
    </div>
  )
}

export function GuideStep({ n, children }: { n: number; children: ReactNode }) {
  return (
    <div className="flex gap-2">
      <span className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full bg-ink-700 text-[10px] font-semibold text-line-200">
        {n}
      </span>
      <p className="leading-relaxed">{children}</p>
    </div>
  )
}

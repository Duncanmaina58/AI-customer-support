import clsx from 'clsx'
import type { ReactNode } from 'react'

const TONES = {
  teal: 'bg-teal-500/15 text-mint-300 ring-1 ring-teal-500/20',
  purple: 'bg-purple-500/15 text-purple-500 ring-1 ring-purple-500/20',
  muted: 'bg-ink-800 text-muted-400 ring-1 ring-ink-700',
  green: 'bg-green-500/15 text-green-500 ring-1 ring-green-500/20',
  amber: 'bg-amber-500/15 text-amber-500 ring-1 ring-amber-500/20',
  coral: 'bg-coral-500/15 text-coral-500 ring-1 ring-coral-500/20',
} as const

export function Badge({ tone, children }: { tone: keyof typeof TONES; children: ReactNode }) {
  return (
    <span className={clsx('inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[11px] font-semibold tracking-wide', TONES[tone])}>
      {children}
    </span>
  )
}

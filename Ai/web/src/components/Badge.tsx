import clsx from 'clsx'
import type { ReactNode } from 'react'

const TONES = {
  teal: 'bg-teal-500/15 text-mint-300',
  purple: 'bg-purple-500/15 text-purple-500',
  muted: 'bg-ink-800 text-muted-400',
  green: 'bg-green-500/15 text-green-500',
  amber: 'bg-amber-500/15 text-amber-500',
  coral: 'bg-coral-500/15 text-coral-500',
} as const
//bade
export function Badge({ tone, children }: { tone: keyof typeof TONES; children: ReactNode }) {
  return (
    <span className={clsx('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium', TONES[tone])}>
      {children}
    </span>
  )
}

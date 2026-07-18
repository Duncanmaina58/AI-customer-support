import { ArrowUp, ArrowDown, Minus } from 'lucide-react'

/**
 * `higherIsBetter` flips the color semantics: for most metrics (conversations,
 * containment rate, CSAT) up is good and green; for response time, down is
 * good and green. Null percent (no previous-period data to compare) renders
 * nothing rather than a misleading "+∞%" or "0%".
 */
export function TrendBadge({
  percent,
  higherIsBetter = true,
}: {
  percent: number | null
  higherIsBetter?: boolean
}) {
  if (percent === null) return null

  const isFlat = Math.abs(percent) < 0.1
  const isPositive = percent > 0
  const isGood = isFlat ? null : higherIsBetter ? isPositive : !isPositive

  const colorClass = isFlat ? 'text-muted-400' : isGood ? 'text-green-500' : 'text-coral-500'
  const Icon = isFlat ? Minus : isPositive ? ArrowUp : ArrowDown

  return (
    <span className={`inline-flex items-center gap-0.5 text-xs font-medium ${colorClass}`}>
      <Icon className="h-3 w-3" />
      {Math.abs(percent).toFixed(1)}%
    </span>
  )
}

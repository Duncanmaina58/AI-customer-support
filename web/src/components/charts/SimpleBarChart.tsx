/**
 * Minimal vertical bar chart — no charting library dependency. Renders raw
 * SVG rects scaled to the data's max value. Deliberately simple: this is a
 * "how's my AI doing this week" glance, not an interactive data-exploration
 * tool, so tooltips/zoom/pan would be more complexity than the feature needs.
 */
export function SimpleBarChart({
  data,
  height = 160,
  formatLabel,
}: {
  data: { label: string; value: number }[]
  height?: number
  formatLabel?: (label: string) => string
}) {
  const max = Math.max(...data.map((d) => d.value), 1)
  const barWidth = 100 / data.length
  const gradId = 'simple-bar-grad'

  return (
    <div>
      <svg viewBox={`0 0 100 ${height}`} preserveAspectRatio="none" className="w-full" style={{ height }}>
        <defs>
          <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#818cf8" />
            <stop offset="100%" stopColor="#6366f1" stopOpacity="0.5" />
          </linearGradient>
        </defs>
        {data.map((d, i) => {
          const barHeight = (d.value / max) * (height - 20)
          return (
            <rect
              key={i}
              x={i * barWidth + barWidth * 0.15}
              y={height - 20 - barHeight}
              width={barWidth * 0.7}
              height={Math.max(barHeight, d.value > 0 ? 1.5 : 0)}
              rx={1.5}
              fill={`url(#${gradId})`}
            />
          )
        })}
      </svg>
      <div className="mt-1 flex font-mono text-[10px] text-muted-400">
        {data.map((d, i) => (
          <div key={i} className="flex-1 truncate text-center" style={{ width: `${barWidth}%` }}>
            {i % Math.ceil(data.length / 8 || 1) === 0 ? (formatLabel ? formatLabel(d.label) : d.label) : ''}
          </div>
        ))}
      </div>
    </div>
  )
}

/** Horizontal bar list — for channel breakdown, top questions, anything ranked. */
export function HorizontalBarList({
  items,
  colorClassName = 'bg-teal-500',
}: {
  items: { label: string; count: number }[]
  colorClassName?: string
}) {
  const max = Math.max(...items.map((i) => i.count), 1)

  if (items.length === 0) {
    return <p className="text-sm text-muted-400">No data yet.</p>
  }

  return (
    <div className="space-y-3">
      {items.map((item, i) => (
        <div key={i}>
          <div className="mb-1.5 flex items-center justify-between text-xs">
            <span className="truncate text-line-200">{item.label}</span>
            <span className="shrink-0 font-mono text-muted-400">{item.count}</span>
          </div>
          <div className="h-[6px] overflow-hidden rounded-full bg-ink-800">
            <div
              className={`h-full rounded-full bg-gradient-to-r from-teal-500 to-purple-500 transition-all duration-700 ${colorClassName === 'bg-teal-500' ? '' : colorClassName}`}
              style={{ width: `${(item.count / max) * 100}%` }}
            />
          </div>
        </div>
      ))}
    </div>
  )
}

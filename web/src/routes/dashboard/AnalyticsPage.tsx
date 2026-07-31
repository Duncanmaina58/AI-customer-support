import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Star, Clock, ShieldCheck, MessagesSquare, Sparkles } from 'lucide-react'
import { api } from '@/lib/api'
import { SimpleBarChart, HorizontalBarList } from '@/components/charts/SimpleBarChart'
import { TrendBadge } from '@/components/charts/TrendBadge'
import type {
  AnalyticsSummary,
  DailyConversationCount,
  ChannelBreakdown,
  TopQuestion,
  EscalationReasonBreakdown,
  DailyTokenUsage,
  CsatSummary,
} from '@/lib/types'

const CHANNEL_LABELS: Record<string, string> = {
  WebChat: 'Web Chat',
  WhatsApp: 'WhatsApp',
  Email: 'Email',
  Messenger: 'Messenger',
  Telegram: 'Telegram',
  Instagram: 'Instagram',
  MobileSdk: 'Mobile SDK',
}

const RANGE_OPTIONS = [
  { label: '7d', days: 7 },
  { label: '14d', days: 14 },
  { label: '30d', days: 30 },
  { label: '90d', days: 90 },
]

function formatSeconds(seconds: number | null): string {
  if (seconds === null) return '—'
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`
  return `${(seconds / 3600).toFixed(1)}h`
}

/** Tiny inline sparkline for the "live trend" band — pure SVG, no charting dependency. */
function Sparkline({ data, color = '#818cf8', height = 44 }: { data: number[]; color?: string; height?: number }) {
  if (!data || data.length < 2) return <div style={{ height }} />
  const max = Math.max(...data, 1)
  const min = Math.min(...data, 0)
  const range = max - min || 1
  const width = 600
  const points = data
    .map((v, i) => {
      const x = (i / (data.length - 1)) * width
      const y = height - ((v - min) / range) * (height - 6) - 3
      return `${x},${y}`
    })
    .join(' ')

  return (
    <svg width="100%" height={height} viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" className="overflow-visible">
      <defs>
        <linearGradient id="analytics-spark-grad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.3" />
          <stop offset="100%" stopColor={color} stopOpacity="0" />
        </linearGradient>
      </defs>
      <polygon points={`0,${height} ${points} ${width},${height}`} fill="url(#analytics-spark-grad)" />
      <polyline points={points} fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

export function AnalyticsPage() {
  const [days, setDays] = useState(14)

  const { data: summary, isLoading } = useQuery({
    queryKey: ['analytics-summary', days],
    queryFn: async () => {
      const { data } = await api.get<AnalyticsSummary>(`/api/analytics/summary?days=${days}`)
      return data
    },
    refetchInterval: 20_000,
  })

  const { data: volume, isLoading: volumeLoading } = useQuery({
    queryKey: ['analytics-conversations-over-time', days],
    queryFn: async () => {
      const { data } = await api.get<DailyConversationCount[]>(`/api/analytics/conversations-over-time?days=${days}`)
      return data
    },
  })

  const { data: channels, isLoading: channelsLoading } = useQuery({
    queryKey: ['analytics-channel-breakdown', days],
    queryFn: async () => {
      const { data } = await api.get<ChannelBreakdown[]>(`/api/analytics/channel-breakdown?days=${days}`)
      return data
    },
  })

  const { data: topQuestions, isLoading: questionsLoading } = useQuery({
    queryKey: ['analytics-top-questions', days],
    queryFn: async () => {
      const { data } = await api.get<TopQuestion[]>(`/api/analytics/top-questions?days=${days}&limit=8`)
      return data
    },
  })

  const { data: escalationReasons, isLoading: escalationsLoading } = useQuery({
    queryKey: ['analytics-escalation-reasons', days],
    queryFn: async () => {
      const { data } = await api.get<EscalationReasonBreakdown[]>(`/api/analytics/escalation-reasons?days=${days}`)
      return data
    },
  })

  const { data: tokenUsage, isLoading: tokensLoading } = useQuery({
    queryKey: ['analytics-token-usage', days],
    queryFn: async () => {
      const { data } = await api.get<DailyTokenUsage[]>(`/api/analytics/token-usage-over-time?days=${days}`)
      return data
    },
  })

  const { data: csat, isLoading: csatLoading } = useQuery({
    queryKey: ['analytics-csat', days],
    queryFn: async () => {
      const { data } = await api.get<CsatSummary>(`/api/analytics/csat?days=${days}`)
      return data
    },
  })

  const volumeSparkline = useMemo(() => (volume ? volume.map((d) => d.count) : []), [volume])

  const kpis = [
    {
      label: 'Conversations',
      value: summary?.totalConversations,
      trend: summary?.conversationsTrendPercent ?? null,
      higherIsBetter: true,
      icon: MessagesSquare,
      color: '#6366f1',
      bg: 'rgba(99, 102, 241, 0.1)',
      sub: 'Across all channels',
    },
    {
      label: 'AI containment rate',
      value: summary ? `${summary.containmentRate}%` : undefined,
      trend: summary?.containmentRateTrendPercent ?? null,
      higherIsBetter: true,
      icon: ShieldCheck,
      color: '#10b981',
      bg: 'rgba(16, 185, 129, 0.1)',
      sub: 'Resolved without a human',
    },
    {
      label: 'Avg first response',
      value: summary ? formatSeconds(summary.avgFirstResponseSeconds) : undefined,
      trend: summary?.avgFirstResponseTrendPercent ?? null,
      higherIsBetter: false,
      icon: Clock,
      color: '#ef4444',
      bg: 'rgba(239, 68, 68, 0.1)',
      sub: 'Median time to first reply',
    },
    {
      label: 'CSAT',
      value: summary?.csatAverageScore ? `${summary.csatAverageScore.toFixed(2)} / 5` : '—',
      trend: null,
      higherIsBetter: true,
      icon: Star,
      color: '#f59e0b',
      bg: 'rgba(245, 158, 11, 0.1)',
      sub: summary ? `${summary.csatRatingCount} rating${summary.csatRatingCount === 1 ? '' : 's'}` : undefined,
    },
  ]

  return (
    <div>
      <header className="animate-reveal mb-6 flex flex-wrap items-end justify-between gap-4 border-b border-ink-700 pb-5">
        <div>
          <div className="mb-2 flex items-center gap-2">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 trend-dot" />
            <span className="text-[11px] font-semibold uppercase tracking-[0.12em] text-teal-400">Analytics Dashboard</span>
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-white sm:text-3xl">
            Performance <em className="font-light not-italic text-muted-400">Overview</em>
          </h1>
          <p className="mt-2 max-w-md text-sm leading-relaxed text-muted-400">
            Real-time insight into how your AI is performing across every connected channel.
          </p>
        </div>
        <div className="flex items-center gap-1 rounded-xl border border-ink-700 bg-ink-900 p-1">
          {RANGE_OPTIONS.map((opt) => (
            <button
              key={opt.days}
              onClick={() => setDays(opt.days)}
              className={`rounded-lg px-4 py-2 text-[13px] font-medium transition-all duration-200 ${
                days === opt.days ? 'bg-teal-500 text-white shadow-[0_1px_3px_rgba(0,0,0,0.3)]' : 'text-muted-400 hover:text-line-200'
              }`}
            >
              {opt.label}
            </button>
          ))}
        </div>
      </header>

      {/* KPI row */}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {kpis.map((kpi, i) => (
          <div key={kpi.label} className="glass-card animate-reveal rounded-2xl p-5" style={{ animationDelay: `${0.06 * (i + 1)}s` }}>
            <div className="mb-3 flex items-start justify-between">
              <div className="flex h-9 w-9 items-center justify-center rounded-xl" style={{ background: kpi.bg, border: `1px solid ${kpi.color}20` }}>
                <kpi.icon className="h-4 w-4" style={{ color: kpi.color }} />
              </div>
              <TrendBadge percent={kpi.trend} higherIsBetter={kpi.higherIsBetter} />
            </div>
            <p className="font-mono text-2xl font-bold tracking-tight text-white">
              {isLoading ? <span className="inline-block h-7 w-16 rounded-md skeleton-shimmer align-middle" /> : (kpi.value ?? '—')}
            </p>
            <p className="mt-1 text-xs uppercase tracking-wide text-muted-400">{kpi.label}</p>
            {kpi.sub && <p className="mt-0.5 text-[11px] text-muted-400">{kpi.sub}</p>}
          </div>
        ))}
      </div>

      {/* Live trend band */}
      {volume && volume.length > 0 && (
        <div className="glass-card animate-reveal mt-4 flex items-center gap-5 rounded-2xl px-6 py-5" style={{ animationDelay: '0.32s' }}>
          <div className="flex min-w-fit items-center gap-2.5">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 trend-dot" />
            <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-muted-400">Live trend</span>
          </div>
          <div className="flex flex-1 items-center gap-4">
            <Sparkline data={volumeSparkline} />
            <div className="min-w-fit text-right">
              <div className="font-mono text-lg font-bold leading-none text-white">{volume[volume.length - 1]?.count ?? 0}</div>
              <div className="mt-0.5 text-[10px] uppercase tracking-wider text-muted-400">latest day</div>
            </div>
          </div>
        </div>
      )}

      {summary && summary.totalConversations === 0 ? (
        <div className="glass-card animate-reveal mt-6 rounded-2xl border-dashed p-10 text-center" style={{ animationDelay: '0.4s' }}>
          <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-500/10 ring-1 ring-teal-500/20">
            <Sparkles className="h-5 w-5 text-teal-400" />
          </div>
          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-400">
            No conversations in this window yet. Try a longer range, or send a message through your widget,
            WhatsApp, or another connected channel to start building real insight here.
          </p>
        </div>
      ) : (
        <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
          <div className="glass-card animate-reveal rounded-2xl p-5 lg:col-span-2" style={{ animationDelay: '0.38s' }}>
            <p className="mb-4 text-sm font-semibold text-white">Conversation volume</p>
            {volumeLoading || !volume ? (
              <ChartSkeleton />
            ) : (
              <SimpleBarChart
                data={volume.map((d) => ({ label: d.date, value: d.count }))}
                formatLabel={(label) => new Date(label).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
              />
            )}
          </div>

          <div className="glass-card animate-reveal rounded-2xl p-5" style={{ animationDelay: '0.44s' }}>
            <p className="mb-4 text-sm font-semibold text-white">By channel</p>
            {channelsLoading || !channels ? (
              <ChartSkeleton />
            ) : (
              <HorizontalBarList items={channels.map((c) => ({ label: CHANNEL_LABELS[c.channel] ?? c.channel, count: c.count }))} />
            )}
          </div>

          <div className="glass-card animate-reveal rounded-2xl p-5" style={{ animationDelay: '0.5s' }}>
            <p className="mb-4 text-sm font-semibold text-white">Token usage</p>
            {tokensLoading || !tokenUsage ? (
              <ChartSkeleton />
            ) : (
              <SimpleBarChart
                data={tokenUsage.map((d) => ({ label: d.date, value: d.tokens }))}
                formatLabel={(label) => new Date(label).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                height={130}
              />
            )}
          </div>

          <div className="glass-card animate-reveal rounded-2xl p-5" style={{ animationDelay: '0.56s' }}>
            <p className="mb-4 text-sm font-semibold text-white">Why conversations escalate</p>
            {escalationsLoading || !escalationReasons ? (
              <ChartSkeleton />
            ) : escalationReasons.length > 0 ? (
              <HorizontalBarList items={escalationReasons.map((r) => ({ label: r.reason, count: r.count }))} colorClassName="bg-amber-500" />
            ) : (
              <p className="text-sm text-muted-400">No escalations in this window — great sign.</p>
            )}
          </div>

          <div className="glass-card animate-reveal rounded-2xl p-5" style={{ animationDelay: '0.62s' }}>
            <p className="mb-4 text-sm font-semibold text-white">Customer satisfaction</p>
            {csatLoading || !csat ? (
              <ChartSkeleton />
            ) : csat.ratingCount > 0 ? (
              <div>
                <div className="mb-4 flex items-baseline gap-2">
                  <span className="font-mono text-3xl font-bold tracking-tight text-white">{csat.averageScore?.toFixed(2)}</span>
                  <span className="text-xs text-muted-400">/ 5 · {csat.ratingCount} ratings</span>
                </div>
                <HorizontalBarList
                  items={csat.distribution
                    .slice()
                    .reverse()
                    .map((b) => ({ label: '★'.repeat(b.score), count: b.count }))}
                  colorClassName="bg-mint-300"
                />
              </div>
            ) : (
              <p className="text-sm text-muted-400">
                No ratings yet — customers can rate a conversation from the chat widget once they've exchanged a
                few messages.
              </p>
            )}
          </div>

          <div className="glass-card animate-reveal rounded-2xl p-5 lg:col-span-2" style={{ animationDelay: '0.68s' }}>
            <p className="mb-1 text-sm font-semibold text-white">Top questions</p>
            <p className="mb-4 text-xs text-muted-400">
              Grouped by exact wording, so close paraphrases count separately — a useful signal for what to add to
              your knowledge base, not a precise ranking.
            </p>
            {questionsLoading || !topQuestions ? (
              <ChartSkeleton />
            ) : topQuestions.length > 0 ? (
              <HorizontalBarList items={topQuestions.map((q) => ({ label: q.question, count: q.count }))} />
            ) : (
              <p className="text-sm text-muted-400">Not enough repeated questions yet to show a ranking.</p>
            )}
          </div>
        </div>
      )}

      <footer className="mt-8 flex items-center justify-between border-t border-ink-700 pt-4">
        <div className="flex items-center gap-2">
          <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 trend-dot" />
          <span className="text-[10px] font-medium uppercase tracking-wider text-muted-400">Live data · refreshes every 20s</span>
        </div>
      </footer>
    </div>
  )
}

function ChartSkeleton() {
  return (
    <div className="flex h-32 flex-col items-center justify-center gap-2 text-xs text-muted-400">
      <div className="h-6 w-6 animate-spin rounded-full border-2 border-teal-400/30 border-t-teal-400" />
      Loading…
    </div>
  )
}

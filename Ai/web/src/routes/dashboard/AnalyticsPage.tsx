import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Star, Clock, ShieldCheck, MessagesSquare } from 'lucide-react'
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
  { label: '7 days', days: 7 },
  { label: '14 days', days: 14 },
  { label: '30 days', days: 30 },
  { label: '90 days', days: 90 },
]

function formatSeconds(seconds: number | null): string {
  if (seconds === null) return '—'
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`
  return `${(seconds / 3600).toFixed(1)}h`
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

  const { data: volume } = useQuery({
    queryKey: ['analytics-conversations-over-time', days],
    queryFn: async () => {
      const { data } = await api.get<DailyConversationCount[]>(`/api/analytics/conversations-over-time?days=${days}`)
      return data
    },
  })

  const { data: channels } = useQuery({
    queryKey: ['analytics-channel-breakdown', days],
    queryFn: async () => {
      const { data } = await api.get<ChannelBreakdown[]>(`/api/analytics/channel-breakdown?days=${days}`)
      return data
    },
  })

  const { data: topQuestions } = useQuery({
    queryKey: ['analytics-top-questions', days],
    queryFn: async () => {
      const { data } = await api.get<TopQuestion[]>(`/api/analytics/top-questions?days=${days}&limit=8`)
      return data
    },
  })

  const { data: escalationReasons } = useQuery({
    queryKey: ['analytics-escalation-reasons', days],
    queryFn: async () => {
      const { data } = await api.get<EscalationReasonBreakdown[]>(`/api/analytics/escalation-reasons?days=${days}`)
      return data
    },
  })

  const { data: tokenUsage } = useQuery({
    queryKey: ['analytics-token-usage', days],
    queryFn: async () => {
      const { data } = await api.get<DailyTokenUsage[]>(`/api/analytics/token-usage-over-time?days=${days}`)
      return data
    },
  })

  const { data: csat } = useQuery({
    queryKey: ['analytics-csat', days],
    queryFn: async () => {
      const { data } = await api.get<CsatSummary>(`/api/analytics/csat?days=${days}`)
      return data
    },
  })

  const kpis = [
    {
      label: 'Conversations',
      value: summary?.totalConversations,
      trend: summary?.conversationsTrendPercent ?? null,
      higherIsBetter: true,
      icon: MessagesSquare,
    },
    {
      label: 'AI containment rate',
      value: summary ? `${summary.containmentRate}%` : undefined,
      trend: summary?.containmentRateTrendPercent ?? null,
      higherIsBetter: true,
      icon: ShieldCheck,
      sub: 'Resolved without a human',
    },
    {
      label: 'Avg first response',
      value: summary ? formatSeconds(summary.avgFirstResponseSeconds) : undefined,
      trend: summary?.avgFirstResponseTrendPercent ?? null,
      higherIsBetter: false,
      icon: Clock,
    },
    {
      label: 'CSAT',
      value: summary?.csatAverageScore ? `${summary.csatAverageScore.toFixed(2)} / 5` : '—',
      trend: null,
      higherIsBetter: true,
      icon: Star,
      sub: summary ? `${summary.csatRatingCount} rating${summary.csatRatingCount === 1 ? '' : 's'}` : undefined,
    },
  ]

  return (
    <div>
      <header className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-white">Analytics</h1>
          <p className="mt-1 text-sm text-muted-400">Deep insight into how your AI is performing.</p>
        </div>
        <div className="flex gap-1 rounded-lg border border-ink-700 bg-ink-900 p-1">
          {RANGE_OPTIONS.map((opt) => (
            <button
              key={opt.days}
              onClick={() => setDays(opt.days)}
              className={`rounded-md px-3 py-1.5 text-xs font-medium transition-colors ${
                days === opt.days ? 'bg-teal-500 text-white' : 'text-muted-400 hover:text-line-200'
              }`}
            >
              {opt.label}
            </button>
          ))}
        </div>
      </header>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {kpis.map((kpi) => (
          <div key={kpi.label} className="rounded-xl border border-ink-700 bg-ink-900 p-4">
            <div className="flex items-center gap-1.5 text-xs text-muted-400">
              <kpi.icon className="h-3.5 w-3.5" />
              {kpi.label}
            </div>
            <div className="mt-2 flex items-baseline gap-2">
              <p className="text-2xl font-semibold text-white">{isLoading ? '—' : (kpi.value ?? '—')}</p>
              <TrendBadge percent={kpi.trend} higherIsBetter={kpi.higherIsBetter} />
            </div>
            {kpi.sub && <p className="mt-0.5 text-[11px] text-muted-400">{kpi.sub}</p>}
          </div>
        ))}
      </div>

      {summary && summary.totalConversations === 0 ? (
        <div className="mt-8 rounded-xl border border-dashed border-ink-700 p-10 text-center">
          <p className="text-sm text-muted-400">
            No conversations in this window yet. Try a longer range, or send a message through your widget,
            WhatsApp, or another connected channel to start building real insight here.
          </p>
        </div>
      ) : (
        <div className="mt-6 grid grid-cols-1 gap-4 lg:grid-cols-3">
          <div className="rounded-xl border border-ink-700 bg-ink-900 p-4 lg:col-span-2">
            <p className="mb-3 text-sm font-medium text-line-200">Conversation volume</p>
            {volume ? (
              <SimpleBarChart
                data={volume.map((d) => ({ label: d.date, value: d.count }))}
                formatLabel={(label) => new Date(label).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
              />
            ) : (
              <ChartSkeleton />
            )}
          </div>

          <div className="rounded-xl border border-ink-700 bg-ink-900 p-4">
            <p className="mb-3 text-sm font-medium text-line-200">By channel</p>
            {channels ? (
              <HorizontalBarList
                items={channels.map((c) => ({ label: CHANNEL_LABELS[c.channel] ?? c.channel, count: c.count }))}
              />
            ) : (
              <ChartSkeleton />
            )}
          </div>

          <div className="rounded-xl border border-ink-700 bg-ink-900 p-4">
            <p className="mb-3 text-sm font-medium text-line-200">Token usage</p>
            {tokenUsage ? (
              <SimpleBarChart
                data={tokenUsage.map((d) => ({ label: d.date, value: d.tokens }))}
                formatLabel={(label) => new Date(label).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                height={130}
              />
            ) : (
              <ChartSkeleton />
            )}
          </div>

          <div className="rounded-xl border border-ink-700 bg-ink-900 p-4">
            <p className="mb-3 text-sm font-medium text-line-200">Why conversations escalate</p>
            {escalationReasons ? (
              escalationReasons.length > 0 ? (
                <HorizontalBarList
                  items={escalationReasons.map((r) => ({ label: r.reason, count: r.count }))}
                  colorClassName="bg-amber-500"
                />
              ) : (
                <p className="text-sm text-muted-400">No escalations in this window — great sign.</p>
              )
            ) : (
              <ChartSkeleton />
            )}
          </div>

          <div className="rounded-xl border border-ink-700 bg-ink-900 p-4">
            <p className="mb-3 text-sm font-medium text-line-200">Customer satisfaction</p>
            {csat ? (
              csat.ratingCount > 0 ? (
                <div>
                  <div className="mb-3 flex items-baseline gap-2">
                    <span className="text-xl font-semibold text-white">{csat.averageScore?.toFixed(2)}</span>
                    <span className="text-xs text-muted-400">/ 5 across {csat.ratingCount} ratings</span>
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
              )
            ) : (
              <ChartSkeleton />
            )}
          </div>

          <div className="rounded-xl border border-ink-700 bg-ink-900 p-4 lg:col-span-2">
            <p className="mb-1 text-sm font-medium text-line-200">Top questions</p>
            <p className="mb-3 text-xs text-muted-400">
              Grouped by exact wording, so close paraphrases count separately — a useful signal for what to add to
              your knowledge base, not a precise ranking.
            </p>
            {topQuestions ? (
              topQuestions.length > 0 ? (
                <HorizontalBarList
                  items={topQuestions.map((q) => ({ label: q.question, count: q.count }))}
                />
              ) : (
                <p className="text-sm text-muted-400">Not enough repeated questions yet to show a ranking.</p>
              )
            ) : (
              <ChartSkeleton />
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function ChartSkeleton() {
  return <div className="flex h-32 items-center justify-center text-xs text-muted-400">Loading…</div>
}

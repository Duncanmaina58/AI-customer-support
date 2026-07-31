import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ArrowRight } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import { TrendBadge } from '@/components/charts/TrendBadge'
import type { AnalyticsSummary } from '@/lib/types'

/**
 * Quick-glance landing page. The deep-dive (date ranges, escalation
 * breakdown, token trend, CSAT distribution, top questions) lives at
 * /analytics — kept separate so the page an agent lands on every login stays
 * fast and focused, rather than loading seven charts' worth of queries.
 */
export function OverviewPage() {
  const { agent } = useAuth()

  const { data, isLoading, isError } = useQuery({
    queryKey: ['analytics-summary', 14],
    queryFn: async () => {
      const { data } = await api.get<AnalyticsSummary>('/api/analytics/summary?days=14')
      return data
    },
    // Conversations change in near-real-time as the AI pipeline runs — poll
    // gently so the dashboard feels "live" without hammering the API.
    refetchInterval: 15_000,
  })

  const tokenPercentUsed =
    data && data.monthlyTokenBudget > 0
      ? Math.min(100, Math.round((data.tokensUsedThisMonth / data.monthlyTokenBudget) * 100))
      : 0

  const stats = [
    {
      label: 'Open conversations',
      value: data?.openConversations,
      trend: null,
      accent: 'text-mint-300',
    },
    {
      label: 'AI containment rate',
      value: data ? `${data.containmentRate}%` : undefined,
      trend: data?.containmentRateTrendPercent ?? null,
      accent: 'text-mint-300',
    },
    {
      label: 'Resolved',
      value: data?.resolvedConversations,
      trend: null,
      accent: 'text-green-500',
    },
    {
      label: 'Escalated',
      value: data?.escalatedConversations,
      trend: null,
      accent: 'text-amber-500',
    },
  ]

  return (
    <div>
      <header className="mb-8 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-white">Welcome back{agent ? `, ${agent.name.split(' ')[0]}` : ''}</h1>
          <p className="mt-1 text-sm text-muted-400">Here's how your support is doing right now, at a glance.</p>
        </div>
        <Link
          to="/analytics"
          className="flex shrink-0 items-center gap-1 text-sm font-medium text-mint-300 hover:underline"
        >
          Full analytics <ArrowRight className="h-3.5 w-3.5" />
        </Link>
      </header>

      {isError && (
        <div className="mb-6 rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          Couldn't load analytics. Is the backend running?
        </div>
      )}

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {stats.map((stat) => (
          <div key={stat.label} className="rounded-xl border border-ink-700 bg-ink-900 p-4">
            <div className="flex items-baseline gap-2">
              <p className={`text-2xl font-semibold ${stat.accent}`}>
                {isLoading ? '—' : (stat.value ?? '—')}
              </p>
              <TrendBadge percent={stat.trend} />
            </div>
            <p className="mt-1 text-xs text-muted-400">{stat.label}</p>
          </div>
        ))}
      </div>

      <div className="mt-4 rounded-xl border border-ink-700 bg-ink-900 p-4">
        <div className="flex items-center justify-between">
          <p className="text-sm font-medium text-line-200">AI token usage this month</p>
          <p className="text-xs text-muted-400">
            {isLoading
              ? '—'
              : `${data?.tokensUsedThisMonth.toLocaleString()} / ${data?.monthlyTokenBudget.toLocaleString()} tokens`}
          </p>
        </div>
        <div className="mt-2 h-2 overflow-hidden rounded-full bg-ink-800">
          <div
            className={`h-full rounded-full transition-all ${
              tokenPercentUsed >= 90 ? 'bg-coral-500' : tokenPercentUsed >= 70 ? 'bg-amber-500' : 'bg-teal-500'
            }`}
            style={{ width: `${tokenPercentUsed}%` }}
          />
        </div>
        {tokenPercentUsed >= 90 && (
          <p className="mt-2 text-xs text-coral-500">
            You're close to this month's limit — visit Settings → Billing to upgrade before responses pause.
          </p>
        )}
      </div>

      {data && data.totalConversations === 0 && (
        <div className="mt-8 rounded-xl border border-dashed border-ink-700 p-10 text-center">
          <p className="text-sm text-muted-400">
            No conversations yet — this fills in once your widget or WhatsApp number receives a real message.
            Open <code className="rounded bg-ink-800 px-1.5 py-0.5 text-xs">/widget-test.html</code> in a new tab to try it,
            or head to <Link to="/sandbox" className="text-mint-300 hover:underline">Sandbox</Link> to test safely first.
          </p>
        </div>
      )}

      {data && data.totalConversations > 0 && (
        <div className="mt-6 flex items-center justify-between rounded-xl border border-dashed border-ink-700 p-6 text-center">
          <p className="mx-auto text-sm text-muted-400">
            Conversation trends, channel breakdown, escalation reasons, token usage over time, and CSAT all live on
            the{' '}
            <Link to="/analytics" className="font-medium text-mint-300 hover:underline">
              Analytics
            </Link>{' '}
            page.
          </p>
        </div>
      )}
    </div>
  )
}

import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ArrowRight, MessagesSquare, ShieldCheck, CheckCircle2, TriangleAlert, Sparkles, FlaskConical } from 'lucide-react'
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
      icon: MessagesSquare,
      color: '#6366f1',
      bg: 'rgba(99, 102, 241, 0.1)',
    },
    {
      label: 'AI containment rate',
      value: data ? `${data.containmentRate}%` : undefined,
      trend: data?.containmentRateTrendPercent ?? null,
      icon: ShieldCheck,
      color: '#8b5cf6',
      bg: 'rgba(139, 92, 246, 0.1)',
    },
    {
      label: 'Resolved',
      value: data?.resolvedConversations,
      trend: null,
      icon: CheckCircle2,
      color: '#10b981',
      bg: 'rgba(16, 185, 129, 0.1)',
    },
    {
      label: 'Escalated',
      value: data?.escalatedConversations,
      trend: null,
      icon: TriangleAlert,
      color: '#f59e0b',
      bg: 'rgba(245, 158, 11, 0.1)',
    },
  ]

  return (
    <div>
      <header className="animate-reveal mb-8 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <div className="mb-2 flex items-center gap-2">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 trend-dot" />
            <span className="text-[11px] font-semibold uppercase tracking-[0.12em] text-teal-400">Live overview</span>
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-white">
            Welcome back{agent ? `, ${agent.name.split(' ')[0]}` : ''}
          </h1>
          <p className="mt-1 text-sm text-muted-400">Here's how your support is doing right now, at a glance.</p>
        </div>
        <Link
          to="/dashboard/analytics"
          className="flex shrink-0 items-center gap-1.5 self-start rounded-xl border border-ink-700 bg-ink-900 px-4 py-2 text-sm font-medium text-mint-300 transition-all hover:border-teal-500/30 hover:bg-ink-800"
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
        {stats.map((stat, i) => (
          <div
            key={stat.label}
            className="glass-card animate-reveal rounded-2xl p-5"
            style={{ animationDelay: `${0.06 * (i + 1)}s` }}
          >
            <div className="mb-3 flex items-center justify-between">
              <div
                className="flex h-9 w-9 items-center justify-center rounded-xl"
                style={{ background: stat.bg, border: `1px solid ${stat.color}20` }}
              >
                <stat.icon className="h-4 w-4" style={{ color: stat.color }} />
              </div>
              <TrendBadge percent={stat.trend} />
            </div>
            <p className="font-mono text-2xl font-bold tracking-tight text-white">
              {isLoading ? (
                <span className="inline-block h-7 w-16 rounded-md skeleton-shimmer align-middle" />
              ) : (
                (stat.value ?? '—')
              )}
            </p>
            <p className="mt-1 text-xs text-muted-400">{stat.label}</p>
          </div>
        ))}
      </div>

      <div className="glass-card animate-reveal mt-4 rounded-2xl p-5" style={{ animationDelay: '0.3s' }}>
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-teal-400" />
            <p className="text-sm font-medium text-line-200">AI token usage this month</p>
          </div>
          <p className="font-mono text-xs text-muted-400">
            {isLoading
              ? '—'
              : `${data?.tokensUsedThisMonth.toLocaleString()} / ${data?.monthlyTokenBudget.toLocaleString()} tokens`}
          </p>
        </div>
        <div className="mt-3 h-2.5 overflow-hidden rounded-full bg-ink-800">
          <div
            className={`h-full rounded-full transition-all duration-700 ${
              tokenPercentUsed >= 90
                ? 'bg-gradient-to-r from-coral-500 to-amber-500'
                : tokenPercentUsed >= 70
                  ? 'bg-gradient-to-r from-amber-500 to-teal-500'
                  : 'bg-gradient-to-r from-teal-500 to-purple-500'
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
        <div className="glass-card animate-reveal mt-8 rounded-2xl border-dashed p-10 text-center" style={{ animationDelay: '0.36s' }}>
          <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-500/10 ring-1 ring-teal-500/20">
            <FlaskConical className="h-5 w-5 text-teal-400" />
          </div>
          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-400">
            No conversations yet — this fills in once your widget or WhatsApp number receives a real message.
            Open <code className="rounded bg-ink-800 px-1.5 py-0.5 text-xs text-line-200">/widget-test.html</code> in a new tab to try it,
            or head to <Link to="/dashboard/sandbox" className="font-medium text-mint-300 hover:underline">Sandbox</Link> to test safely first.
          </p>
        </div>
      )}

      {data && data.totalConversations > 0 && (
        <div className="glass-card animate-reveal mt-6 rounded-2xl border-dashed p-6 text-center" style={{ animationDelay: '0.36s' }}>
          <p className="mx-auto text-sm text-muted-400">
            Conversation trends, channel breakdown, escalation reasons, token usage over time, and CSAT all live on
            the{' '}
            <Link to="/dashboard/analytics" className="font-medium text-mint-300 hover:underline">
              Analytics
            </Link>{' '}
            page.
          </p>
        </div>
      )}
    </div>
  )
}

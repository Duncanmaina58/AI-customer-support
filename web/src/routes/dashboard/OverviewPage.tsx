import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import type { AnalyticsSummary } from '@/lib/types'

export function OverviewPage() {
  const { agent } = useAuth()

  const { data, isLoading, isError } = useQuery({
    queryKey: ['analytics-summary'],
    queryFn: async () => {
      const { data } = await api.get<AnalyticsSummary>('/api/analytics/summary')
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
    { label: 'Open conversations', value: data?.openConversations, accent: 'text-mint-300' },
    { label: 'AI resolution rate', value: data ? `${data.resolutionRate}%` : undefined, accent: 'text-mint-300' },
    { label: 'Resolved conversations', value: data?.resolvedConversations, accent: 'text-green-500' },
    { label: 'Escalated', value: data?.escalatedConversations, accent: 'text-amber-500' },
  ]

  return (
    <div>
      <header className="mb-8">
        <h1 className="text-xl font-semibold text-white">Welcome back{agent ? `, ${agent.name.split(' ')[0]}` : ''}</h1>
        <p className="mt-1 text-sm text-muted-400">Here's how your support is doing right now.</p>
      </header>

      {isError && (
        <div className="mb-6 rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          Couldn't load analytics. Is the backend running?
        </div>
      )}

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {stats.map((stat) => (
          <div key={stat.label} className="rounded-xl border border-ink-700 bg-ink-900 p-4">
            <p className={`text-2xl font-semibold ${stat.accent}`}>
              {isLoading ? '—' : (stat.value ?? '—')}
            </p>
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
      </div>

      {data && data.totalConversations === 0 && (
        <div className="mt-8 rounded-xl border border-dashed border-ink-700 p-10 text-center">
          <p className="text-sm text-muted-400">
            No conversations yet — this fills in once your widget or WhatsApp number receives a real message.
            Open <code className="rounded bg-ink-800 px-1.5 py-0.5 text-xs">/widget-test.html</code> in a new tab to try it.
          </p>
        </div>
      )}
    </div>
  )
}

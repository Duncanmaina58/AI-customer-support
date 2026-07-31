import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Check, Sparkles, ArrowLeft } from 'lucide-react'
import { api } from '@/lib/api'
import type { BillingPlan } from '@/lib/types'

/**
 * Sprint 7: public pricing page for new signups (platform.com/pricing).
 * Pulls live from BillingController.GetPlans — the same static catalog the
 * in-dashboard Billing tab reads — so this page can never drift out of sync
 * with what a company would actually be charged after signing up.
 */
export function PricingPage() {
  const { data: plans, isLoading } = useQuery({
    queryKey: ['billing-plans'],
    queryFn: async () => {
      const { data } = await api.get<BillingPlan[]>('/api/billing/plans')
      return data
    },
  })

  return (
    <div className="relative min-h-screen bg-ink-950">
      <div
        className="pointer-events-none fixed inset-0"
        style={{
          background: `
            radial-gradient(ellipse at 50% 0%, rgba(99,102,241,0.09) 0%, transparent 50%),
            radial-gradient(ellipse at 85% 30%, rgba(236,72,153,0.05) 0%, transparent 50%)
          `,
        }}
      />

      <header className="relative z-10 mx-auto flex max-w-5xl items-center justify-between px-6 py-6">
        <Link to="/" className="flex items-center gap-2.5">
          <div
            className="flex h-8 w-8 items-center justify-center rounded-[9px] text-white"
            style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
          >
            <Sparkles className="h-4 w-4" />
          </div>
          <span className="text-base font-bold tracking-tight text-white">Asupport</span>
        </Link>
        <Link to="/" className="flex items-center gap-1.5 text-sm font-medium text-muted-400 hover:text-line-200">
          <ArrowLeft className="h-3.5 w-3.5" /> Back home
        </Link>
      </header>

      <div className="relative z-10 mx-auto max-w-5xl px-6 pb-20 pt-6">
        <div className="animate-reveal text-center">
          <div className="mb-4 inline-flex items-center gap-2 rounded-full border border-indigo-500/20 bg-indigo-500/[0.08] px-3 py-1.5">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 trend-dot" />
            <span className="text-[11px] font-semibold uppercase tracking-wider text-indigo-300">KES pricing · M-Pesa ready</span>
          </div>
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">Simple, transparent pricing</h1>
          <p className="mx-auto mt-3 max-w-lg text-muted-400">
            Pick a plan, pay with M-Pesa, and your AI is answering customers in minutes. Upgrade or downgrade any time.
          </p>
        </div>

        {isLoading ? (
          <div className="mt-16 flex justify-center">
            <div className="h-6 w-6 animate-spin rounded-full border-2 border-teal-400 border-t-transparent" />
          </div>
        ) : (
          <div className="mt-12 grid grid-cols-1 gap-6 md:grid-cols-3">
            {plans?.map((plan, i) => {
              const featured = plan.plan === 'Growth'
              return (
                <div
                  key={plan.plan}
                  className={`animate-reveal relative rounded-2xl p-6 ${
                    featured
                      ? 'border border-teal-400/40 bg-gradient-to-b from-teal-500/[0.08] to-purple-500/[0.04]'
                      : 'glass-card'
                  }`}
                  style={{
                    animationDelay: `${0.08 * i}s`,
                    boxShadow: featured ? '0 20px 60px -20px rgba(99,102,241,0.35)' : undefined,
                  }}
                >
                  {featured && (
                    <span className="mb-3 inline-flex items-center gap-1 rounded-full bg-teal-500/20 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wide text-mint-300 ring-1 ring-teal-500/30">
                      <Sparkles className="h-3 w-3" /> Most popular
                    </span>
                  )}
                  <h2 className="text-lg font-semibold text-white">{plan.name}</h2>
                  <p className="mt-2 font-mono text-3xl font-bold text-mint-300">
                    KES {plan.priceKes.toLocaleString()}
                    <span className="text-sm font-normal text-muted-400"> / month</span>
                  </p>
                  <ul className="mt-5 space-y-2.5">
                    {plan.features.map((feature) => (
                      <li key={feature} className="flex items-start gap-2 text-sm text-line-200">
                        <Check className="mt-0.5 h-4 w-4 shrink-0 text-teal-400" /> {feature}
                      </li>
                    ))}
                  </ul>
                  <Link
                    to="/register"
                    className="mt-6 block w-full rounded-lg bg-teal-500 px-4 py-2.5 text-center text-sm font-medium text-white hover:bg-teal-400"
                  >
                    Get started
                  </Link>
                </div>
              )
            })}
          </div>
        )}

        <p className="mt-12 text-center text-xs text-muted-400">
          Overage beyond your plan's conversation limit is billed at KES 0.80 per conversation. Pay with M-Pesa —
          no card needed.
        </p>
      </div>
    </div>
  )
}

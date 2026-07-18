import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Check } from 'lucide-react'
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
    <div className="min-h-screen bg-ink-950 px-6 py-16">
      <div className="mx-auto max-w-5xl">
        <div className="text-center">
          <h1 className="text-3xl font-semibold text-white">Simple, KES pricing</h1>
          <p className="mt-3 text-muted-400">
            Pick a plan, pay with M-Pesa, and your AI is answering customers in minutes. Upgrade or change any time.
          </p>
        </div>

        {isLoading ? (
          <div className="mt-16 flex justify-center">
            <div className="h-6 w-6 animate-spin rounded-full border-2 border-teal-400 border-t-transparent" />
          </div>
        ) : (
          <div className="mt-12 grid grid-cols-1 gap-6 md:grid-cols-3">
            {plans?.map((plan) => (
              <div
                key={plan.plan}
                className={`rounded-2xl border p-6 ${
                  plan.plan === 'Growth' ? 'border-teal-400 bg-teal-500/5' : 'border-ink-700 bg-ink-900'
                }`}
              >
                {plan.plan === 'Growth' && (
                  <span className="mb-3 inline-block rounded-full bg-teal-500/20 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wide text-mint-300">
                    Most popular
                  </span>
                )}
                <h2 className="text-lg font-semibold text-white">{plan.name}</h2>
                <p className="mt-2 text-3xl font-semibold text-mint-300">
                  KES {plan.priceKes.toLocaleString()}
                  <span className="text-sm font-normal text-muted-400"> / month</span>
                </p>
                <ul className="mt-5 space-y-2.5">
                  {plan.features.map((feature) => (
                    <li key={feature} className="flex items-start gap-2 text-sm text-line-200">
                      <Check className="mt-0.5 h-4 w-4 shrink-0 text-teal-500" /> {feature}
                    </li>
                  ))}
                </ul>
                <Link
                  to="/register"
                  className="mt-6 block w-full rounded-lg bg-teal-500 px-4 py-2.5 text-center text-sm font-medium text-white transition-colors hover:bg-teal-400"
                >
                  Get started
                </Link>
              </div>
            ))}
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

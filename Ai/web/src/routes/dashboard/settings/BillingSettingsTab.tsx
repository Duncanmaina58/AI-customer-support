import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Smartphone, Loader2 } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import type {
  BillingInfo,
  BillingPlan,
  InitiateMpesaPaymentResponse,
  MpesaTransactionStatus,
} from '@/lib/types'

/**
 * Sprint 7: current plan + usage + M-Pesa upgrade flow.
 *
 * STK Push is inherently async — InitiateMpesaPayment returns the instant the
 * push is sent to the customer's phone, not once payment succeeds. This
 * component polls GetTransactionStatus every couple of seconds afterward
 * until Safaricom's callback (BillingController.MpesaCallback) has updated
 * the transaction to a final state.
 */
export function BillingSettingsTab() {
  const { agent } = useAuth()
  const canManageBilling = agent?.role === 'Owner' || agent?.role === 'Admin'
  const queryClient = useQueryClient()

  const [selectedPlan, setSelectedPlan] = useState<string | null>(null)
  const [phoneNumber, setPhoneNumber] = useState('')
  const [pendingTransactionId, setPendingTransactionId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const { data: billingInfo, isLoading } = useQuery({
    queryKey: ['billing-info'],
    queryFn: async () => {
      const { data } = await api.get<BillingInfo>('/api/billing/info')
      return data
    },
  })

  const { data: plans } = useQuery({
    queryKey: ['billing-plans'],
    queryFn: async () => {
      const { data } = await api.get<BillingPlan[]>('/api/billing/plans')
      return data
    },
  })

  useEffect(() => {
    if (billingInfo?.billingPhoneNumber && !phoneNumber) setPhoneNumber(billingInfo.billingPhoneNumber)
  }, [billingInfo, phoneNumber])

  const { data: transactionStatus } = useQuery({
    queryKey: ['mpesa-transaction-status', pendingTransactionId],
    queryFn: async () => {
      const { data } = await api.get<MpesaTransactionStatus>(`/api/billing/mpesa/status/${pendingTransactionId}`)
      return data
    },
    enabled: !!pendingTransactionId,
    // Poll every 3s until the transaction reaches a final state — Safaricom's
    // callback typically arrives within 10-30s of the customer entering (or
    // ignoring) their PIN.
    refetchInterval: (query) => (query.state.data?.status === 'Pending' ? 3000 : false),
  })

  useEffect(() => {
    if (transactionStatus && transactionStatus.status === 'Success') {
      queryClient.invalidateQueries({ queryKey: ['billing-info'] })
    }
  }, [transactionStatus, queryClient])

  const initiateMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<InitiateMpesaPaymentResponse>('/api/billing/mpesa/initiate', {
        plan: selectedPlan,
        phoneNumber,
      })
      return data
    },
    onSuccess: (data) => {
      setPendingTransactionId(data.transactionId)
      setError(null)
    },
    onError: (err: unknown) => {
      const response = (err as { response?: { data?: { message?: string } } })?.response
      setError(response?.data?.message ?? "Couldn't start the M-Pesa payment.")
    },
  })

  function startPayment(plan: string) {
    setError(null)
    setSelectedPlan(plan)
    if (!phoneNumber.trim()) return
    initiateMutation.mutate()
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16 text-muted-400">
        <Loader2 className="h-5 w-5 animate-spin" />
      </div>
    )
  }

  const isAwaitingPayment = !!pendingTransactionId && transactionStatus?.status === 'Pending'
  const percentUsed = billingInfo ? Math.min(100, Math.round(billingInfo.percentUsed)) : 0

  return (
    <div className="space-y-6">
      {billingInfo && (
        <div className="rounded-xl border border-ink-700 bg-ink-900 p-5">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-xs text-muted-400">Current plan</p>
              <p className="text-lg font-semibold text-white">
                {billingInfo.currentPlan} — KES {billingInfo.currentPlanPriceKes.toLocaleString()}/month
              </p>
            </div>
            <p className="text-xs text-muted-400">
              Next reset: {new Date(billingInfo.nextResetAt).toLocaleDateString()}
            </p>
          </div>

          <div className="mt-4">
            <div className="flex items-center justify-between text-xs text-muted-400">
              <span>Usage this period</span>
              <span>
                {billingInfo.tokensUsedThisMonth.toLocaleString()} / {billingInfo.monthlyTokenBudget.toLocaleString()} tokens
              </span>
            </div>
            <div className="mt-1.5 h-2 overflow-hidden rounded-full bg-ink-800">
              <div
                className={`h-full rounded-full transition-all ${
                  percentUsed >= 90 ? 'bg-coral-500' : percentUsed >= 70 ? 'bg-amber-500' : 'bg-teal-500'
                }`}
                style={{ width: `${percentUsed}%` }}
              />
            </div>
          </div>
        </div>
      )}

      {canManageBilling && (
        <div className="rounded-xl border border-ink-700 bg-ink-900 p-5">
          <h3 className="text-sm font-semibold text-white">Upgrade or change plan</h3>

          <div className="mt-3">
            <label className="mb-1.5 block text-xs text-muted-400">M-Pesa phone number</label>
            <div className="flex items-center gap-2">
              <Smartphone className="h-4 w-4 shrink-0 text-muted-400" />
              <input
                value={phoneNumber}
                onChange={(e) => setPhoneNumber(e.target.value)}
                placeholder="07XX XXX XXX"
                className="w-full max-w-xs rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
              />
            </div>
          </div>

          {error && <p className="mt-2 text-xs text-coral-500">{error}</p>}

          {isAwaitingPayment && (
            <div className="mt-4 flex items-center gap-2 rounded-lg border border-teal-500/30 bg-teal-500/10 px-3 py-2 text-sm text-mint-300">
              <Loader2 className="h-4 w-4 animate-spin" />
              Check your phone — enter your M-Pesa PIN to complete the {selectedPlan} plan payment.
            </div>
          )}

          {transactionStatus?.status === 'Success' && (
            <div className="mt-4 flex items-center gap-2 rounded-lg border border-green-500/30 bg-green-500/10 px-3 py-2 text-sm text-green-500">
              <Check className="h-4 w-4" /> Payment received — you're now on the {billingInfo?.currentPlan} plan.
              {transactionStatus.mpesaReceiptNumber && ` Receipt: ${transactionStatus.mpesaReceiptNumber}`}
            </div>
          )}

          {transactionStatus && (transactionStatus.status === 'Failed' || transactionStatus.status === 'Cancelled') && (
            <p className="mt-4 text-sm text-coral-500">
              Payment {transactionStatus.status === 'Cancelled' ? 'was cancelled' : "didn't go through"}
              {transactionStatus.resultDescription ? ` — ${transactionStatus.resultDescription}` : ''}. Try again below.
            </p>
          )}

          <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-3">
            {plans?.map((plan) => (
              <div
                key={plan.plan}
                className={`rounded-lg border p-4 ${
                  billingInfo?.currentPlan === plan.plan ? 'border-teal-400 bg-teal-500/5' : 'border-ink-700'
                }`}
              >
                <p className="text-sm font-semibold text-white">{plan.name}</p>
                <p className="mt-1 text-xl font-semibold text-mint-300">
                  KES {plan.priceKes.toLocaleString()}
                  <span className="text-xs font-normal text-muted-400">/mo</span>
                </p>
                <ul className="mt-3 space-y-1.5">
                  {plan.features.map((feature) => (
                    <li key={feature} className="flex items-start gap-1.5 text-xs text-muted-400">
                      <Check className="mt-0.5 h-3 w-3 shrink-0 text-teal-500" /> {feature}
                    </li>
                  ))}
                </ul>
                <button
                  type="button"
                  onClick={() => startPayment(plan.plan)}
                  disabled={
                    billingInfo?.currentPlan === plan.plan ||
                    !phoneNumber.trim() ||
                    (initiateMutation.isPending && selectedPlan === plan.plan) ||
                    isAwaitingPayment
                  }
                  className="mt-4 w-full rounded-md bg-teal-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-teal-400 disabled:opacity-50"
                >
                  {billingInfo?.currentPlan === plan.plan
                    ? 'Current plan'
                    : initiateMutation.isPending && selectedPlan === plan.plan
                      ? 'Sending prompt…'
                      : 'Switch to this plan'}
                </button>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

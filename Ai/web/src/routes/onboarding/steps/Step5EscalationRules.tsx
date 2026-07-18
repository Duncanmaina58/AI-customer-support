import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'
import type { CompanyDetails } from '@/lib/types'
import { EscalationRulesForm, parseEscalationRules } from '@/components/EscalationRulesForm'

/**
 * Onboarding wizard step 5: escalation rules (the Phase 1 deep-dive doc's
 * "Step 6" — numbered 5 here since this wizard has no separate knowledge-base
 * step; KB is a standalone dashboard page, not a wizard step, in this codebase).
 *
 * Sprint 6: no longer the final step — advances to Step6TestAndGoLive, which
 * now owns the finish-onboarding call, so a company can actually try the AI
 * before committing to going live rather than finishing blind.
 */
export function Step5EscalationRules({
  company,
  onNext,
  onBack,
}: {
  company: CompanyDetails
  onNext: () => void
  onBack: () => void
}) {
  const queryClient = useQueryClient()
  const [rules, setRules] = useState(() => parseEscalationRules(company.escalationRulesJson))
  const [error, setError] = useState<string | null>(null)

  const saveMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.patch<CompanyDetails>('/api/companies/me', {
        escalationRulesJson: JSON.stringify(rules),
      })
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['company'], data)
      onNext()
    },
    onError: () => setError("Couldn't save escalation rules — try again."),
  })

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-400">
        Decide when the AI should hand off to a human. Sensible defaults are already on — adjust anything you'd
        like, or just continue.
      </p>

      {error && (
        <div role="alert" className="rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
          {error}
        </div>
      )}

      <EscalationRulesForm rules={rules} onChange={setRules} />

      <div className="flex justify-between pt-2">
        <button
          type="button"
          onClick={onBack}
          className="rounded-lg border border-ink-700 px-4 py-2 text-sm text-muted-400 hover:text-line-200"
        >
          Back
        </button>
        <button
          type="button"
          onClick={() => saveMutation.mutate()}
          disabled={saveMutation.isPending}
          className="rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
        >
          {saveMutation.isPending ? 'Saving…' : 'Continue'}
        </button>
      </div>
    </div>
  )
}

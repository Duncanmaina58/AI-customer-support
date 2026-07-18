import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { FlaskConical } from 'lucide-react'
import { api } from '@/lib/api'
import { CopyableSecret } from '@/components/CopyableSecret'
import { ChatPanel } from '@/components/ChatPanel'
import type { CompanyDetails, SandboxInfo } from '@/lib/types'

const SANDBOX_SESSION_STORAGE_KEY = 'asp_onboarding_sandbox_session_id'

/**
 * Onboarding wizard's final step (the Phase 1 deep-dive doc's "Step 7: Test &
 * Go Live" — numbered 6 here for the same reason Step 5 isn't "Step 6": this
 * wizard skips a separate knowledge-base step).
 *
 * Lets the company actually try their AI — via their own private sandbox test
 * chat, right here in the wizard — before finishing setup, rather than
 * connecting channels blind and finding out how it behaves only once real
 * customers are already messaging it. Sandbox conversations never count
 * against the token budget or create real tickets (see ChatHub).
 */
export function Step6TestAndGoLive({ onBack }: { onBack: () => void }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)

  const { data: sandbox } = useQuery({
    queryKey: ['sandbox-info'],
    queryFn: async () => {
      const { data } = await api.get<SandboxInfo>('/api/sandbox/info')
      return data
    },
  })

  const goLiveMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<CompanyDetails>('/api/companies/me/onboarding/complete')
      return data
    },
    onSuccess: (data) => {
      // Set the cache directly (synchronous) rather than invalidateQueries
      // (async refetch) — OnboardingGate must see onboardingCompletedAt
      // already populated the instant it mounts after navigate() below.
      queryClient.setQueryData(['company'], data)
      navigate('/', { replace: true })
    },
    onError: () => setError("Couldn't finish setup — try again."),
  })

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-400">
        Try your AI below — this is a private sandbox, so nothing here counts against your usage or creates real
        tickets. Ask it something from your knowledge base, or try asking for a human to see escalation in action.
      </p>

      {error && (
        <div role="alert" className="rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
          {error}
        </div>
      )}

      <div className="overflow-hidden rounded-lg border border-ink-700">
        <ChatPanel
          connectionKey={sandbox?.sandboxToken ?? null}
          sessionStorageKey={SANDBOX_SESSION_STORAGE_KEY}
          headerTitle="Sandbox test chat"
          missingKeyMessage="Loading your sandbox…"
          className="h-96"
        />
      </div>

      {sandbox && (
        <div>
          <p className="mb-1.5 flex items-center gap-1.5 text-xs text-muted-400">
            <FlaskConical className="h-3.5 w-3.5" /> Share this link with your team to test from anywhere:
          </p>
          <CopyableSecret label="Sandbox test link" value={`${window.location.origin}${sandbox.testLinkPath}`} />
        </div>
      )}

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
          onClick={() => goLiveMutation.mutate()}
          disabled={goLiveMutation.isPending}
          className="rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
        >
          {goLiveMutation.isPending ? 'Going live…' : 'Go live 🚀'}
        </button>
      </div>
    </div>
  )
}

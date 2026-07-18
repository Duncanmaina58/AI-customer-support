import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FlaskConical, RefreshCw } from 'lucide-react'
import { api } from '@/lib/api'
import { CopyableSecret } from '@/components/CopyableSecret'
import { ChatPanel } from '@/components/ChatPanel'
import { useAuth } from '@/context/useAuth'
import type { SandboxInfo } from '@/lib/types'

const SANDBOX_SESSION_STORAGE_KEY = 'asp_dashboard_sandbox_session_id'

/**
 * Sprint 6: dashboard Sandbox page. Same private test chat shown during
 * onboarding's Step 6, kept reachable afterward — testing shouldn't be a
 * one-time onboarding thing, e.g. after editing the knowledge base or
 * escalation rules, or to get a shareable link for a teammate to try it
 * without touching production data.
 */
export function SandboxPage() {
  const { agent } = useAuth()
  const canRegenerate = agent?.role === 'Owner' || agent?.role === 'Admin'
  const queryClient = useQueryClient()
  const [justRegenerated, setJustRegenerated] = useState(false)

  const { data: sandbox, isLoading } = useQuery({
    queryKey: ['sandbox-info'],
    queryFn: async () => {
      const { data } = await api.get<SandboxInfo>('/api/sandbox/info')
      return data
    },
  })

  const regenerateMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<SandboxInfo>('/api/sandbox/regenerate')
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['sandbox-info'], data)
      setJustRegenerated(true)
      setTimeout(() => setJustRegenerated(false), 2500)
    },
  })

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <div className="h-6 w-6 animate-spin rounded-full border-2 border-teal-400 border-t-transparent" />
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-6">
      <div>
        <h1 className="flex items-center gap-2 text-xl font-semibold text-white">
          <FlaskConical className="h-5 w-5 text-amber-400" /> Sandbox
        </h1>
        <p className="mt-1 text-sm text-muted-400">
          A private, freely-shareable test chat. Nothing here counts against your token usage or creates real
          tickets — see it as a permanent, safe place to try changes to your knowledge base or escalation rules
          before they reach real customers.
        </p>
      </div>

      {sandbox && (
        <div className="space-y-2 rounded-xl border border-ink-700 bg-ink-900 p-5">
          <p className="text-sm font-medium text-line-200">Shareable test link</p>
          <CopyableSecret label="Sandbox test link" value={`${window.location.origin}${sandbox.testLinkPath}`} />
          {canRegenerate && (
            <div className="flex items-center gap-3 pt-1">
              <button
                type="button"
                onClick={() => regenerateMutation.mutate()}
                disabled={regenerateMutation.isPending}
                className="flex items-center gap-1.5 text-xs text-muted-400 hover:text-line-200 disabled:opacity-60"
              >
                <RefreshCw className={`h-3.5 w-3.5 ${regenerateMutation.isPending ? 'animate-spin' : ''}`} />
                Regenerate link
              </button>
              {justRegenerated && <span className="text-xs text-green-500">Old link deactivated ✓</span>}
            </div>
          )}
        </div>
      )}

      <div className="overflow-hidden rounded-xl border border-ink-700">
        <ChatPanel
          connectionKey={sandbox?.sandboxToken ?? null}
          sessionStorageKey={SANDBOX_SESSION_STORAGE_KEY}
          headerTitle="Sandbox test chat"
          missingKeyMessage="Couldn't load your sandbox — try refreshing."
          className="h-[32rem]"
        />
      </div>
    </div>
  )
}

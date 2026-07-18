import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ChatPanel } from '@/components/ChatPanel'
import { api } from '@/lib/api'

const SANDBOX_SESSION_STORAGE_KEY = 'asp_sandbox_session_id'

/**
 * Sprint 6: private sandbox test chat (platform.com/test/{sandboxToken}).
 * Behaves exactly like the real widget — same ChatPanel, same ChatHub — except
 * ChatHub resolves the company by SandboxToken instead of PublicApiKey, which
 * flags every conversation started here as IsSandbox: no token-budget charge,
 * no real tickets created (see ChatHub.SendMessage). Anyone with this link can
 * test the AI without it touching production data, which is the whole point —
 * it can be shared freely with a team.
 *
 * Uses its own localStorage key (not the widget's) so a business owner testing
 * their own sandbox in the same browser they use for their real site never
 * accidentally shares a conversation between the two.
 */
export function SandboxChatPage() {
  const { token } = useParams<{ token: string }>()

  const { data: company, isLoading, isError } = useQuery({
    queryKey: ['sandbox-company', token],
    queryFn: async () => {
      const { data } = await api.get<{ companyName: string }>(`/api/sandbox/${token}/company`)
      return data
    },
    enabled: !!token,
    retry: false,
  })

  if (isError) {
    return (
      <div className="flex h-screen items-center justify-center bg-ink-950 p-6 text-center">
        <p className="text-sm text-coral-500">
          This test link isn't valid — it may have been regenerated. Ask whoever shared it for a new one.
        </p>
      </div>
    )
  }

  if (isLoading) {
    return (
      <div className="flex h-screen items-center justify-center bg-ink-950">
        <div className="h-6 w-6 animate-spin rounded-full border-2 border-teal-400 border-t-transparent" />
      </div>
    )
  }

  return (
    <ChatPanel
      connectionKey={token ?? null}
      sessionStorageKey={SANDBOX_SESSION_STORAGE_KEY}
      headerTitle={company ? `Testing ${company.companyName}'s AI` : 'Sandbox test chat'}
      missingKeyMessage="This test link is missing a token."
    />
  )
}

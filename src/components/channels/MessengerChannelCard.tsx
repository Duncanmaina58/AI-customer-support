import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { MessageSquare } from 'lucide-react'
import { api } from '@/lib/api'
import type { ChannelConnection, ConnectMessengerRequest } from '@/lib/types'
import { StatusPill, GuideDisclosure, GuideStep } from '@/components/channels/shared'

export function MessengerChannelCard({
  channels,
  canEdit,
  showDisconnect = false,
}: {
  channels: ChannelConnection[] | undefined
  canEdit: boolean
  showDisconnect?: boolean
}) {
  const queryClient = useQueryClient()
  const existing = channels?.find((c) => c.channel === 'Messenger')
  const isConnected = existing?.status === 'Active'

  const [isExpanded, setIsExpanded] = useState(false)
  const [pageAccessToken, setPageAccessToken] = useState('')
  const [pageId, setPageId] = useState('')
  const [error, setError] = useState<string | null>(null)

  const connectMutation = useMutation({
    mutationFn: () =>
      api.post('/api/channels/messenger', { pageAccessToken, pageId } satisfies ConnectMessengerRequest),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['channels'] })
      setIsExpanded(false)
      setPageAccessToken('')
    },
    onError: (err: unknown) => {
      const response = (err as { response?: { data?: { message?: string; detail?: string } } })?.response
      setError(response?.data?.detail ?? response?.data?.message ?? "Couldn't verify those Messenger credentials.")
    },
  })

  const disconnectMutation = useMutation({
    mutationFn: () => api.post(`/api/channels/${existing?.id}/disconnect`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['channels'] }),
  })

  function handleConnect(e: FormEvent) {
    e.preventDefault()
    setError(null)
    connectMutation.mutate()
  }

  return (
    <div className="rounded-xl border border-ink-700 bg-ink-900 p-5">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <MessageSquare className="h-4 w-4 text-[#0084FF]" />
          <div>
            <p className="text-sm font-medium text-line-200">Messenger</p>
            {existing?.displayInfo && <p className="text-xs text-muted-400">{existing.displayInfo}</p>}
          </div>
        </div>
        <div className="flex items-center gap-3">
          <StatusPill status={existing?.status ?? 'NotConnected'} />
          {canEdit && (
            <button
              type="button"
              onClick={() => setIsExpanded((v) => !v)}
              className="text-xs font-medium text-mint-300 hover:underline"
            >
              {isExpanded ? 'Cancel' : isConnected ? 'Reconnect' : 'Connect'}
            </button>
          )}
          {showDisconnect && isConnected && canEdit && (
            <button
              type="button"
              onClick={() => disconnectMutation.mutate()}
              disabled={disconnectMutation.isPending}
              className="text-xs text-muted-400 hover:text-coral-500 disabled:opacity-60"
            >
              {disconnectMutation.isPending ? 'Disconnecting…' : 'Disconnect'}
            </button>
          )}
        </div>
      </div>

      {error && <p className="mt-2 text-xs text-coral-500">{error}</p>}

      {isExpanded && canEdit && (
        <form onSubmit={handleConnect} className="mt-3 space-y-2">
          <input
            required
            value={pageId}
            onChange={(e) => setPageId(e.target.value)}
            placeholder="Facebook Page ID"
            className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />
          <input
            required
            value={pageAccessToken}
            onChange={(e) => setPageAccessToken(e.target.value)}
            placeholder="Page Access Token"
            className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />
          <button
            type="submit"
            disabled={connectMutation.isPending}
            className="w-full rounded-md bg-teal-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-teal-400 disabled:opacity-60"
          >
            {connectMutation.isPending ? 'Verifying…' : 'Verify & connect'}
          </button>
        </form>
      )}

      <GuideDisclosure title="How to connect Messenger">
        <p>
          <strong className="text-line-200">What you need:</strong> a Facebook Page and a Meta for Developers app
          with the Messenger product added.
        </p>
        <GuideStep n={1}>
          Go to <span className="text-line-200">developers.facebook.com</span> → My Apps → your app (or create one)
          → add the <span className="text-line-200">Messenger</span> product.
        </GuideStep>
        <GuideStep n={2}>
          Under Messenger → Access Tokens, generate a Page Access Token for the Page you want connected.
        </GuideStep>
        <GuideStep n={3}>Copy the Page ID (Page Settings → About) and the token into the form above.</GuideStep>
        <GuideStep n={4}>We verify both against Meta's Graph API before saving.</GuideStep>
        <p className="mt-2 rounded-md bg-amber-500/10 px-2 py-1.5 text-amber-400">
          ⚠️ This uses a pasted Page Access Token rather than a "Connect with Facebook" button — the same approach
          WhatsApp uses here — since that button needs a fully registered OAuth app with a public redirect URL.
          Functionally equivalent, just one extra copy-paste step.
        </p>
      </GuideDisclosure>
    </div>
  )
}

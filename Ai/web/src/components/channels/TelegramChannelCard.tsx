import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Send } from 'lucide-react'
import { api } from '@/lib/api'
import type { ChannelConnection, ConnectTelegramRequest } from '@/lib/types'
import { StatusPill, GuideDisclosure, GuideStep } from '@/components/channels/shared'

export function TelegramChannelCard({
  channels,
  canEdit,
  showDisconnect = false,
}: {
  channels: ChannelConnection[] | undefined
  canEdit: boolean
  showDisconnect?: boolean
}) {
  const queryClient = useQueryClient()
  const existing = channels?.find((c) => c.channel === 'Telegram')
  const isConnected = existing?.status === 'Active'

  const [isExpanded, setIsExpanded] = useState(false)
  const [botToken, setBotToken] = useState('')
  const [error, setError] = useState<string | null>(null)

  const connectMutation = useMutation({
    mutationFn: () => api.post('/api/channels/telegram', { botToken } satisfies ConnectTelegramRequest),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['channels'] })
      setIsExpanded(false)
      setBotToken('')
    },
    onError: (err: unknown) => {
      const response = (err as { response?: { data?: { message?: string; detail?: string } } })?.response
      setError(response?.data?.detail ?? response?.data?.message ?? "Couldn't verify that bot token.")
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
          <Send className="h-4 w-4 text-[#26A5E4]" />
          <div>
            <p className="text-sm font-medium text-line-200">Telegram</p>
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
            value={botToken}
            onChange={(e) => setBotToken(e.target.value)}
            placeholder="Bot token from @BotFather"
            className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />
          <button
            type="submit"
            disabled={connectMutation.isPending}
            className="w-full rounded-md bg-teal-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-teal-400 disabled:opacity-60"
          >
            {connectMutation.isPending ? 'Verifying & registering…' : 'Connect'}
          </button>
        </form>
      )}

      <GuideDisclosure title="How to connect Telegram">
        <p>
          <strong className="text-line-200">What you need:</strong> nothing but a Telegram account — this is the
          simplest channel to set up.
        </p>
        <GuideStep n={1}>
          Open Telegram, message <span className="text-line-200">@BotFather</span>, send{' '}
          <span className="text-line-200">/newbot</span>, and follow the prompts (name + username).
        </GuideStep>
        <GuideStep n={2}>BotFather replies with a token — copy it into the form above.</GuideStep>
        <GuideStep n={3}>
          Click Connect. Unlike every other channel, there's nothing else to configure anywhere else — we verify
          the token and register the webhook with Telegram automatically in this one step.
        </GuideStep>
        <GuideStep n={4}>Message your bot on Telegram to test it.</GuideStep>
      </GuideDisclosure>
    </div>
  )
}

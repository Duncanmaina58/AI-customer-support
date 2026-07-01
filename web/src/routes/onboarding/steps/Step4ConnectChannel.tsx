import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { Check, MessageCircle, Globe } from 'lucide-react'
import { api } from '@/lib/api'
import { CopyableSecret } from '@/components/CopyableSecret'
import type { ChannelConnection, CompanyDetails, ConnectWebChatResponse } from '@/lib/types'

export function Step4ConnectChannel({ onBack }: { onBack: () => void }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [finishError, setFinishError] = useState<string | null>(null)

  const { data: channels } = useQuery({
    queryKey: ['channels'],
    queryFn: async () => {
      const { data } = await api.get<ChannelConnection[]>('/api/channels')
      return data
    },
  })

  const hasActiveChannel = channels?.some((c) => c.status === 'Active') ?? false

  const finishMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<CompanyDetails>('/api/companies/me/onboarding/complete')
      return data
    },
    onSuccess: (data) => {
      // Setting the cache directly with the response we already have is
      // synchronous - unlike invalidateQueries (which kicks off an async
      // background refetch), this guarantees OnboardingGate sees
      // onboardingCompletedAt already populated the instant it mounts after
      // navigate() below, with no race window where it could still read the
      // old cached value and bounce straight back to /onboarding.
      queryClient.setQueryData(['company'], data)
      navigate('/', { replace: true })
    },
    onError: () => setFinishError("Couldn't finish setup — connect a channel below first."),
  })

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-400">Connect at least one channel so the AI has somewhere to answer from.</p>

      {finishError && (
        <div role="alert" className="rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
          {finishError}
        </div>
      )}

      <WhatsAppCard channels={channels} />
      <WebChatCard channels={channels} />

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
          onClick={() => finishMutation.mutate()}
          disabled={!hasActiveChannel || finishMutation.isPending}
          className="rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
          title={hasActiveChannel ? undefined : 'Connect a channel first'}
        >
          {finishMutation.isPending ? 'Finishing…' : 'Finish setup'}
        </button>
      </div>
    </div>
  )
}

function WhatsAppCard({ channels }: { channels: ChannelConnection[] | undefined }) {
  const queryClient = useQueryClient()
  const existing = channels?.find((c) => c.channel === 'WhatsApp')
  const isConnected = existing?.status === 'Active'

  const [isExpanded, setIsExpanded] = useState(false)
  const [accessToken, setAccessToken] = useState('')
  const [phoneNumberId, setPhoneNumberId] = useState('')
  const [testNumber, setTestNumber] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [testSent, setTestSent] = useState(false)

  const connectMutation = useMutation({
    mutationFn: async () => {
      await api.post('/api/channels/whatsapp', { accessToken, phoneNumberId })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['channels'] })
      setIsExpanded(false)
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        "Couldn't verify those credentials."
      setError(message)
    },
  })

  const testMutation = useMutation({
    mutationFn: async () => {
      await api.post('/api/channels/whatsapp/test-message', { toPhoneNumber: testNumber })
    },
    onSuccess: () => setTestSent(true),
    onError: () => setError("Couldn't send the test message."),
  })

  function handleConnect(e: FormEvent) {
    e.preventDefault()
    setError(null)
    connectMutation.mutate()
  }

  return (
    <div className="rounded-lg border border-ink-700 p-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <MessageCircle className="h-4 w-4 text-green-500" />
          <span className="text-sm font-medium text-line-200">WhatsApp</span>
        </div>
        {isConnected ? (
          <span className="flex items-center gap-1 text-xs text-green-500">
            <Check className="h-3.5 w-3.5" /> Connected{existing?.displayInfo ? ` · ${existing.displayInfo}` : ''}
          </span>
        ) : (
          <button
            type="button"
            onClick={() => setIsExpanded((v) => !v)}
            className="text-xs font-medium text-mint-300 hover:underline"
          >
            {isExpanded ? 'Cancel' : 'Connect'}
          </button>
        )}
      </div>

      {error && <p className="mt-2 text-xs text-coral-500">{error}</p>}

      {isExpanded && !isConnected && (
        <form onSubmit={handleConnect} className="mt-3 space-y-2">
          <input
            required
            value={accessToken}
            onChange={(e) => setAccessToken(e.target.value)}
            placeholder="Meta access token"
            className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />
          <input
            required
            value={phoneNumberId}
            onChange={(e) => setPhoneNumberId(e.target.value)}
            placeholder="Phone number ID"
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

      {isConnected && (
        <div className="mt-3 flex items-center gap-2">
          <input
            value={testNumber}
            onChange={(e) => setTestNumber(e.target.value)}
            placeholder="Your WhatsApp number, e.g. 2547..."
            className="flex-1 rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />
          <button
            type="button"
            onClick={() => testMutation.mutate()}
            disabled={!testNumber || testMutation.isPending}
            className="shrink-0 rounded-md border border-ink-700 px-3 py-1.5 text-xs text-line-200 hover:bg-ink-800 disabled:opacity-60"
          >
            {testMutation.isPending ? 'Sending…' : testSent ? 'Sent ✓' : 'Send test message'}
          </button>
        </div>
      )}
    </div>
  )
}

function WebChatCard({ channels }: { channels: ChannelConnection[] | undefined }) {
  const queryClient = useQueryClient()
  const existing = channels?.find((c) => c.channel === 'WebChat')
  const isConnected = existing?.status === 'Active'
  const [scriptTag, setScriptTag] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const connectMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<ConnectWebChatResponse>('/api/channels/webchat')
      return data
    },
    onSuccess: (data) => {
      setScriptTag(data.embedScriptTag)
      queryClient.invalidateQueries({ queryKey: ['channels'] })
    },
    onError: () => setError("Couldn't activate web chat."),
  })

  return (
    <div className="rounded-lg border border-ink-700 p-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Globe className="h-4 w-4 text-teal-400" />
          <span className="text-sm font-medium text-line-200">Web Chat</span>
        </div>
        {isConnected ? (
          <span className="flex items-center gap-1 text-xs text-green-500">
            <Check className="h-3.5 w-3.5" /> Connected
          </span>
        ) : (
          <button
            type="button"
            onClick={() => connectMutation.mutate()}
            disabled={connectMutation.isPending}
            className="text-xs font-medium text-mint-300 hover:underline disabled:opacity-60"
          >
            {connectMutation.isPending ? 'Activating…' : 'Activate'}
          </button>
        )}
      </div>

      {error && <p className="mt-2 text-xs text-coral-500">{error}</p>}

      {isConnected && (
        <div className="mt-3">
          <p className="mb-1.5 text-xs text-muted-400">Paste this before {'</body>'} on your website:</p>
          <CopyableSecret label="Embed script" value={scriptTag ?? `<script data-key="${existing?.id ?? ''}" ...>`} />
        </div>
      )}
    </div>
  )
}

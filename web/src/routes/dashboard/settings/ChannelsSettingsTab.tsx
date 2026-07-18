import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { MessageCircle, Globe, Loader2 } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import { CopyableSecret } from '@/components/CopyableSecret'
import { EmailChannelCard } from '@/components/channels/EmailChannelCard'
import { MessengerChannelCard } from '@/components/channels/MessengerChannelCard'
import { TelegramChannelCard } from '@/components/channels/TelegramChannelCard'
import { StatusPill, GuideDisclosure, GuideStep } from '@/components/channels/shared'
import type {
  ChannelConnection,
  CompanyDetails,
  ConnectWebChatResponse,
} from '@/lib/types'

/**
 * Settings → Channels. Lets an already-onboarded company reconnect a channel
 * (e.g. rotate a WhatsApp token, change the support inbox), disconnect one,
 * or connect a channel they skipped during onboarding — the wizard's Step 4
 * only requires *one* active channel to finish, so this is often the first
 * place a company connects their second or third channel.
 */
export function ChannelsSettingsTab() {
  const { agent } = useAuth()
  const canEdit = agent?.role === 'Owner' || agent?.role === 'Admin'

  const { data: channels, isLoading } = useQuery({
    queryKey: ['channels'],
    queryFn: async () => {
      const { data } = await api.get<ChannelConnection[]>('/api/channels')
      return data
    },
  })

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16 text-muted-400">
        <Loader2 className="h-5 w-5 animate-spin" />
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-sm font-semibold text-white">Connected channels</h3>
        <p className="mt-1 text-sm text-muted-400">
          Wherever a channel is Active, the AI (or an agent, once escalated) answers customers there automatically.
          Reconnect a channel any time to rotate credentials — it won't create a duplicate.
        </p>
      </div>

      <WhatsAppCard channels={channels} canEdit={canEdit} />
      <WebChatCard channels={channels} canEdit={canEdit} />
      <EmailChannelCard channels={channels} canEdit={canEdit} showDisconnect />
      <MessengerChannelCard channels={channels} canEdit={canEdit} showDisconnect />
      <TelegramChannelCard channels={channels} canEdit={canEdit} showDisconnect />
    </div>
  )
}

// -----------------------------------------------------------------------------
// Shared bits
// -----------------------------------------------------------------------------

function DisconnectButton({ connectionId, canEdit }: { connectionId: string; canEdit: boolean }) {
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: () => api.post(`/api/channels/${connectionId}/disconnect`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['channels'] }),
  })

  if (!canEdit) return null

  return (
    <button
      type="button"
      onClick={() => mutation.mutate()}
      disabled={mutation.isPending}
      className="text-xs text-muted-400 hover:text-coral-500 disabled:opacity-60"
    >
      {mutation.isPending ? 'Disconnecting…' : 'Disconnect'}
    </button>
  )
}

// -----------------------------------------------------------------------------
// WhatsApp
// -----------------------------------------------------------------------------

function WhatsAppCard({ channels, canEdit }: { channels: ChannelConnection[] | undefined; canEdit: boolean }) {
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
    mutationFn: () => api.post('/api/channels/whatsapp', { accessToken, phoneNumberId }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['channels'] })
      setIsExpanded(false)
      setAccessToken('')
      setPhoneNumberId('')
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        "Couldn't verify those credentials."
      setError(message)
    },
  })

  const testMutation = useMutation({
    mutationFn: () => api.post('/api/channels/whatsapp/test-message', { toPhoneNumber: testNumber }),
    onSuccess: () => setTestSent(true),
    onError: () => setError("Couldn't send the test message."),
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
          <MessageCircle className="h-4 w-4 text-green-500" />
          <div>
            <p className="text-sm font-medium text-line-200">WhatsApp</p>
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
          {isConnected && existing && <DisconnectButton connectionId={existing.id} canEdit={canEdit} />}
        </div>
      </div>

      {error && <p className="mt-2 text-xs text-coral-500">{error}</p>}

      {isExpanded && canEdit && (
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
            {connectMutation.isPending ? 'Verifying…' : isConnected ? 'Verify & reconnect' : 'Verify & connect'}
          </button>
        </form>
      )}

      {isConnected && !isExpanded && (
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

      <GuideDisclosure title="How to connect WhatsApp">
        <p>
          <strong className="text-line-200">What you need:</strong> a Meta Business Account with WhatsApp Business
          API access, an access token, and a Phone Number ID.
        </p>
        <GuideStep n={1}>
          Go to <span className="text-line-200">developers.facebook.com</span> → My Apps → create (or open) an app
          of type "Business".
        </GuideStep>
        <GuideStep n={2}>In the app dashboard, add the <span className="text-line-200">WhatsApp</span> product.</GuideStep>
        <GuideStep n={3}>
          Under WhatsApp → API Setup, copy the <span className="text-line-200">temporary access token</span> and the{' '}
          <span className="text-line-200">Phone number ID</span> shown there.
        </GuideStep>
        <GuideStep n={4}>Paste both into the form above — we verify them against Meta's API before saving.</GuideStep>
        <GuideStep n={5}>Once connected, send yourself a test message to confirm delivery works.</GuideStep>
        <p className="mt-2 rounded-md bg-amber-500/10 px-2 py-1.5 text-amber-400">
          ⚠️ Meta's temporary tokens expire after 24 hours. For a production setup that doesn't need reconnecting
          daily, generate a permanent token via a System User in Meta Business Settings, then reconnect here with
          that token instead.
        </p>
      </GuideDisclosure>
    </div>
  )
}

// -----------------------------------------------------------------------------
// Web Chat
// -----------------------------------------------------------------------------

function WebChatCard({ channels, canEdit }: { channels: ChannelConnection[] | undefined; canEdit: boolean }) {
  const queryClient = useQueryClient()
  const existing = channels?.find((c) => c.channel === 'WebChat')
  const isConnected = existing?.status === 'Active'
  const [scriptTag, setScriptTag] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const { data: company } = useQuery({
    queryKey: ['company'],
    queryFn: async () => {
      const { data } = await api.get<CompanyDetails>('/api/companies/me')
      return data
    },
    enabled: isConnected && scriptTag === null,
  })

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

  const fallbackScriptTag = company
    ? `<script src="${window.location.origin}/widget-loader.js" data-key="${company.publicApiKey}" async></script>`
    : null

  return (
    <div className="rounded-xl border border-ink-700 bg-ink-900 p-5">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <Globe className="h-4 w-4 text-teal-400" />
          <p className="text-sm font-medium text-line-200">Web Chat</p>
        </div>
        <div className="flex items-center gap-3">
          <StatusPill status={existing?.status ?? 'NotConnected'} />
          {canEdit && !isConnected && (
            <button
              type="button"
              onClick={() => connectMutation.mutate()}
              disabled={connectMutation.isPending}
              className="text-xs font-medium text-mint-300 hover:underline disabled:opacity-60"
            >
              {connectMutation.isPending ? 'Activating…' : 'Activate'}
            </button>
          )}
          {isConnected && existing && <DisconnectButton connectionId={existing.id} canEdit={canEdit} />}
        </div>
      </div>

      {error && <p className="mt-2 text-xs text-coral-500">{error}</p>}

      {isConnected && (
        <div className="mt-3">
          <p className="mb-1.5 text-xs text-muted-400">Paste this before {'</body>'} on your website:</p>
          <CopyableSecret label="Embed script" value={scriptTag ?? fallbackScriptTag ?? 'Loading…'} />
        </div>
      )}

      <GuideDisclosure title="How to connect Web Chat">
        <p>
          <strong className="text-line-200">What you need:</strong> nothing external — just the ability to paste a
          script tag on your website.
        </p>
        <GuideStep n={1}>Click <span className="text-line-200">Activate</span> above.</GuideStep>
        <GuideStep n={2}>Copy the generated embed script.</GuideStep>
        <GuideStep n={3}>
          Paste it just before <span className="text-line-200">{'</body>'}</span> on every page you want the chat
          bubble to appear on.
        </GuideStep>
        <GuideStep n={4}>
          That's it — the widget loads your AI assistant automatically, scoped to your company via the key baked
          into the script tag. No further setup needed.
        </GuideStep>
      </GuideDisclosure>
    </div>
  )
}

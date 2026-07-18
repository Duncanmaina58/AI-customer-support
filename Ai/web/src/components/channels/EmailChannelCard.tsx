import { useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Mail } from 'lucide-react'
import { api, API_BASE_URL } from '@/lib/api'
import { CopyableSecret } from '@/components/CopyableSecret'
import { StatusPill, GuideDisclosure, GuideStep } from '@/components/channels/shared'
import type { ChannelConnection, CompanyDetails, ConnectEmailResponse } from '@/lib/types'

/**
 * Sprint 5 follow-up: shared by onboarding's Step4ConnectChannel and Settings'
 * ChannelsSettingsTab, so the (fairly involved) two-inbound-modes UI can't drift
 * out of sync between the two places it appears.
 *
 * Two inbound modes, client's choice:
 *   Webhook — Brevo inbound parsing. Simple, but Brevo's inbound parsing
 *             requires a paid plan (their free tier covers *sending* mail,
 *             not receiving it).
 *   Imap    — MailKit polls any regular mailbox. Free, works with Gmail,
 *             Outlook/Office365, Zoho, cPanel hosting, etc. A bit more setup.
 */
type Provider = 'gmail' | 'outlook' | 'zoho' | 'custom'

const PROVIDER_PRESETS: Record<Exclude<Provider, 'custom'>, { imapHost: string; imapPort: number; smtpHost: string; smtpPort: number; note: string }> = {
  gmail: {
    imapHost: 'imap.gmail.com', imapPort: 993,
    smtpHost: 'smtp.gmail.com', smtpPort: 587,
    note: "Gmail requires an App Password (not your normal login password) — Google Account → Security → 2-Step Verification → App passwords. Regular passwords are rejected even if correct.",
  },
  outlook: {
    imapHost: 'outlook.office365.com', imapPort: 993,
    smtpHost: 'smtp.office365.com', smtpPort: 587,
    note: 'If your organization enforces modern auth / MFA, you may need an app password from Microsoft 365 admin settings instead of your normal password.',
  },
  zoho: {
    imapHost: 'imap.zoho.com', imapPort: 993,
    smtpHost: 'smtp.zoho.com', smtpPort: 587,
    note: 'Enable IMAP access first: Zoho Mail Settings → Mail Accounts → IMAP Access.',
  },
}

export function EmailChannelCard({
  channels,
  canEdit,
  showDisconnect = false,
}: {
  channels: ChannelConnection[] | undefined
  canEdit: boolean
  showDisconnect?: boolean
}) {
  const queryClient = useQueryClient()
  const existing = channels?.find((c) => c.channel === 'Email')
  const isConnected = existing?.status === 'Active'

  const [isExpanded, setIsExpanded] = useState(false)
  const [mode, setMode] = useState<'Webhook' | 'Imap'>('Imap')
  const [provider, setProvider] = useState<Provider>('gmail')
  const [senderEmail, setSenderEmail] = useState('')
  const [senderName, setSenderName] = useState('')
  const [imapHost, setImapHost] = useState(PROVIDER_PRESETS.gmail.imapHost)
  const [imapPort, setImapPort] = useState(PROVIDER_PRESETS.gmail.imapPort)
  const [smtpHost, setSmtpHost] = useState(PROVIDER_PRESETS.gmail.smtpHost)
  const [smtpPort, setSmtpPort] = useState(PROVIDER_PRESETS.gmail.smtpPort)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [webhookUrl, setWebhookUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const { data: company } = useQuery({
    queryKey: ['company'],
    queryFn: async () => {
      const { data } = await api.get<CompanyDetails>('/api/companies/me')
      return data
    },
    enabled: isConnected && webhookUrl === null,
  })

  function applyProvider(p: Provider) {
    setProvider(p)
    if (p !== 'custom') {
      const preset = PROVIDER_PRESETS[p]
      setImapHost(preset.imapHost)
      setImapPort(preset.imapPort)
      setSmtpHost(preset.smtpHost)
      setSmtpPort(preset.smtpPort)
    }
  }

  const connectMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<ConnectEmailResponse>('/api/channels/email', {
        mode,
        senderEmail,
        senderName,
        ...(mode === 'Imap'
          ? { imapHost, imapPort, smtpHost, smtpPort, username: username || senderEmail, password }
          : {}),
      })
      return data
    },
    onSuccess: (data) => {
      setWebhookUrl(data.brevoWebhookUrl)
      queryClient.invalidateQueries({ queryKey: ['channels'] })
      setIsExpanded(false)
      setPassword('')
    },
    onError: (err: unknown) => {
      const response = (err as { response?: { data?: { message?: string; detail?: string } } })?.response
      const message = response?.data?.detail ?? response?.data?.message ?? "Couldn't connect that email account."
      setError(message)
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
          <Mail className="h-4 w-4 text-sky-400" />
          <div>
            <p className="text-sm font-medium text-line-200">Email</p>
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
              {isExpanded ? 'Cancel' : isConnected ? 'Change' : 'Connect'}
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

      {error && <p className="mt-2 whitespace-pre-wrap text-xs text-coral-500">{error}</p>}

      {isExpanded && canEdit && (
        <form onSubmit={handleConnect} className="mt-3 space-y-3">
          <div className="flex gap-2 rounded-lg bg-ink-950 p-1">
            <ModeTab active={mode === 'Imap'} onClick={() => setMode('Imap')}>
              IMAP/SMTP <span className="ml-1 text-green-500">· Free</span>
            </ModeTab>
            <ModeTab active={mode === 'Webhook'} onClick={() => setMode('Webhook')}>
              Brevo webhook <span className="ml-1 text-amber-400">· Pro plan</span>
            </ModeTab>
          </div>

          <input
            required
            type="email"
            value={senderEmail}
            onChange={(e) => setSenderEmail(e.target.value)}
            placeholder="support@yourcompany.com"
            className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />
          <input
            required
            value={senderName}
            onChange={(e) => setSenderName(e.target.value)}
            placeholder="Display name, e.g. Acme Support"
            className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />

          {mode === 'Imap' && (
            <div className="space-y-2 rounded-lg border border-ink-700 p-3">
              <label className="block text-xs text-muted-400">Mail provider</label>
              <select
                value={provider}
                onChange={(e) => applyProvider(e.target.value as Provider)}
                className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
              >
                <option value="gmail">Gmail / Google Workspace</option>
                <option value="outlook">Outlook / Office 365</option>
                <option value="zoho">Zoho Mail</option>
                <option value="custom">Custom (cPanel, other host…)</option>
              </select>
              {provider !== 'custom' && (
                <p className="rounded-md bg-amber-500/10 px-2 py-1.5 text-xs text-amber-400">
                  ⚠️ {PROVIDER_PRESETS[provider].note}
                </p>
              )}

              <div className="grid grid-cols-2 gap-2">
                <LabeledInput label="IMAP host" value={imapHost} onChange={setImapHost} />
                <LabeledInput label="IMAP port" value={String(imapPort)} onChange={(v) => setImapPort(Number(v) || 993)} />
                <LabeledInput label="SMTP host" value={smtpHost} onChange={setSmtpHost} />
                <LabeledInput label="SMTP port" value={String(smtpPort)} onChange={(v) => setSmtpPort(Number(v) || 587)} />
              </div>
              <input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="Mailbox username (defaults to the address above)"
                className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
              />
              <input
                required
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Password (or app password)"
                className="w-full rounded-md border border-ink-700 bg-ink-950 px-2.5 py-1.5 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
              />
            </div>
          )}

          <button
            type="submit"
            disabled={connectMutation.isPending}
            className="w-full rounded-md bg-teal-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-teal-400 disabled:opacity-60"
          >
            {connectMutation.isPending
              ? mode === 'Imap' ? 'Verifying IMAP + SMTP…' : 'Connecting…'
              : 'Connect email'}
          </button>
        </form>
      )}

      {isConnected && !isExpanded && (
        <EmailPostConnectInfo existing={existing} company={company} freshWebhookUrl={webhookUrl} />
      )}

      <GuideDisclosure title="How to connect Email">
        <p>
          <strong className="text-line-200">Two ways to receive customer email — pick whichever's simpler for you:</strong>
        </p>
        <div className="space-y-1.5 rounded-md border border-ink-700 p-2.5">
          <p className="font-medium text-line-200">Option A — IMAP/SMTP (free, recommended for most)</p>
          <GuideStep n={1}>Pick your mail provider above (or Custom) and enter the address + password.</GuideStep>
          <GuideStep n={2}>
            For Gmail/Workspace you need an <span className="text-line-200">App Password</span>, not your regular
            login — 2-Step Verification must be on first. Outlook and other providers with MFA usually work the
            same way.
          </GuideStep>
          <GuideStep n={3}>We verify both IMAP and SMTP actually connect before saving anything.</GuideStep>
          <GuideStep n={4}>
            Done — no dashboard to configure elsewhere. New mail is checked automatically every ~30 seconds.
          </GuideStep>
        </div>
        <div className="space-y-1.5 rounded-md border border-ink-700 p-2.5">
          <p className="font-medium text-line-200">Option B — Brevo webhook (needs Brevo's paid plan)</p>
          <GuideStep n={1}>Enter the address and display name, choose "Brevo webhook", click Connect.</GuideStep>
          <GuideStep n={2}>Copy the generated webhook URL.</GuideStep>
          <GuideStep n={3}>
            In Brevo: Settings → Transactional → Inbound Parsing → add a route pointing at that URL. Brevo's free
            plan only covers <em>sending</em> mail — inbound parsing needs a paid plan.
          </GuideStep>
          <GuideStep n={4}>Send a test email to confirm it arrives.</GuideStep>
        </div>
        <p className="rounded-md bg-amber-500/10 px-2 py-1.5 text-amber-400">
          ⚠️ Either way, sending still needs a working Brevo <strong>API key</strong> (not an SMTP key — they look
          similar but aren't interchangeable) configured by your platform administrator — it's used for internal
          ticket notifications to your team regardless of which inbound mode you pick here.
        </p>
      </GuideDisclosure>
    </div>
  )
}

function EmailPostConnectInfo({
  existing,
  company,
  freshWebhookUrl,
}: {
  existing: ChannelConnection | undefined
  company: CompanyDetails | undefined
  freshWebhookUrl: string | null
}) {
  const mode = existing?.inboundMode

  if (mode === 'Imap') {
    return (
      <p className="mt-3 flex items-center gap-1.5 text-xs text-green-500">
        <Check className="h-3.5 w-3.5" /> IMAP mode — checking for new mail automatically, nothing else to configure.
      </p>
    )
  }

  // Webhook mode: show the freshly-returned URL if we just connected this
  // session, otherwise rebuild it from the company id (the URL is deterministic
  // and not itself a secret, so this is safe to reconstruct client-side).
  const url = freshWebhookUrl ?? (company ? `${API_BASE_URL}/webhook/email/${company.id}` : null)
  if (!url) return null

  return (
    <div className="mt-3">
      <p className="mb-1.5 text-xs text-muted-400">Point Brevo's inbound parsing at:</p>
      <CopyableSecret label="Inbound webhook URL" value={url} />
    </div>
  )
}

// -----------------------------------------------------------------------------
// Small bits kept local to this file — only Email needs these two
// -----------------------------------------------------------------------------

function ModeTab({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex-1 rounded-md px-3 py-1.5 text-xs font-medium transition-colors ${
        active ? 'bg-ink-800 text-line-200' : 'text-muted-400 hover:text-line-200'
      }`}
    >
      {children}
    </button>
  )
}

function LabeledInput({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label className="block">
      <span className="mb-1 block text-[11px] text-muted-400">{label}</span>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-md border border-ink-700 bg-ink-950 px-2 py-1 text-xs text-line-200 focus:border-teal-400 focus:outline-none"
      />
    </label>
  )
}


import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { X, MessagesSquare, Smartphone, Mail, Send, Camera, MonitorSmartphone } from 'lucide-react'
import { api } from '@/lib/api'
import type { Conversation, Message } from '@/lib/types'

const STATUS_STYLES: Record<Conversation['status'], string> = {
  Open: 'bg-teal-500/15 text-mint-300 ring-1 ring-teal-500/20',
  Pending: 'bg-amber-500/15 text-amber-500 ring-1 ring-amber-500/20',
  Resolved: 'bg-green-500/15 text-green-500 ring-1 ring-green-500/20',
  Escalated: 'bg-coral-500/15 text-coral-500 ring-1 ring-coral-500/20',
}

const CHANNEL_LABELS: Record<Conversation['channel'], string> = {
  WebChat: 'Web Chat',
  WhatsApp: 'WhatsApp',
  Email: 'Email',
  Messenger: 'Messenger',
  Telegram: 'Telegram',
  Instagram: 'Instagram',
  MobileSdk: 'Mobile',
}

const CHANNEL_ICONS: Record<Conversation['channel'], typeof MessagesSquare> = {
  WebChat: MessagesSquare,
  WhatsApp: Smartphone,
  Email: Mail,
  Messenger: Send,
  Telegram: Send,
  Instagram: Camera,
  MobileSdk: MonitorSmartphone,
}

function initials(name: string) {
  return name
    .split(' ')
    .map((p) => p.charAt(0))
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

export function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['conversations'],
    queryFn: async () => {
      const { data } = await api.get<Conversation[]>('/api/conversations')
      return data
    },
    // Sprint 3: no SignalR push to the dashboard yet (that's the widget's job) —
    // poll so new WhatsApp/web-chat conversations show up without a manual refresh.
    refetchInterval: 8_000,
  })

  return (
    <div>
      <header className="animate-reveal mb-6">
        <div className="mb-2 flex items-center gap-2">
          <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 trend-dot" />
          <span className="text-[11px] font-semibold uppercase tracking-[0.12em] text-teal-400">Live inbox</span>
        </div>
        <h1 className="text-2xl font-bold tracking-tight text-white">Conversations</h1>
        <p className="mt-1 text-sm text-muted-400">Across every connected channel, scoped to your company only.</p>
      </header>

      {isLoading && (
        <div className="glass-card animate-reveal flex items-center gap-3 rounded-2xl p-6">
          <div className="h-4 w-4 animate-spin rounded-full border-2 border-teal-400/30 border-t-teal-400" />
          <p className="text-sm text-muted-400">Loading conversations…</p>
        </div>
      )}

      {isError && (
        <div className="rounded-2xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          Couldn't reach the API. Is the backend running at the configured VITE_API_BASE_URL?
        </div>
      )}

      {data && data.length === 0 && (
        <div className="glass-card animate-reveal rounded-2xl border-dashed p-10 text-center">
          <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-500/10 ring-1 ring-teal-500/20">
            <MessagesSquare className="h-5 w-5 text-teal-400" />
          </div>
          <p className="text-sm text-muted-400">No conversations yet. Connect a channel to start receiving them.</p>
        </div>
      )}

      {data && data.length > 0 && (
        <div className="glass-card animate-reveal overflow-hidden rounded-2xl">
          {/* Desktop table */}
          <table className="hidden w-full text-left text-sm sm:table">
            <thead className="text-[11px] uppercase tracking-wider text-muted-400">
              <tr className="border-b border-ink-700">
                <th className="px-5 py-3.5 font-semibold">Customer</th>
                <th className="px-5 py-3.5 font-semibold">Channel</th>
                <th className="px-5 py-3.5 font-semibold">Status</th>
                <th className="px-5 py-3.5 font-semibold">Messages</th>
                <th className="px-5 py-3.5 font-semibold">Started</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-700">
              {data.map((c) => {
                const ChannelIcon = CHANNEL_ICONS[c.channel]
                const name = c.customerDisplayName ?? c.customerId
                return (
                  <tr
                    key={c.id}
                    onClick={() => setSelectedId(c.id)}
                    className="cursor-pointer transition-colors hover:bg-ink-800/60"
                  >
                    <td className="px-5 py-3.5">
                      <div className="flex items-center gap-2.5">
                        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-teal-500/25 to-purple-500/25 text-[11px] font-semibold text-mint-300 ring-1 ring-teal-500/20">
                          {initials(name)}
                        </span>
                        <span className="text-line-200">{name}</span>
                      </div>
                    </td>
                    <td className="px-5 py-3.5 text-muted-400">
                      <span className="inline-flex items-center gap-1.5">
                        <ChannelIcon className="h-3.5 w-3.5" />
                        {CHANNEL_LABELS[c.channel]}
                      </span>
                    </td>
                    <td className="px-5 py-3.5">
                      <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${STATUS_STYLES[c.status]}`}>
                        {c.status}
                      </span>
                    </td>
                    <td className="px-5 py-3.5 font-mono text-muted-400">{c.messageCount}</td>
                    <td className="px-5 py-3.5 text-muted-400">{new Date(c.createdAt).toLocaleString()}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>

          {/* Mobile stacked cards */}
          <div className="divide-y divide-ink-700 sm:hidden">
            {data.map((c) => {
              const ChannelIcon = CHANNEL_ICONS[c.channel]
              const name = c.customerDisplayName ?? c.customerId
              return (
                <button
                  key={c.id}
                  onClick={() => setSelectedId(c.id)}
                  className="flex w-full items-center gap-3 px-4 py-3.5 text-left transition-colors active:bg-ink-800"
                >
                  <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-teal-500/25 to-purple-500/25 text-xs font-semibold text-mint-300 ring-1 ring-teal-500/20">
                    {initials(name)}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center justify-between gap-2">
                      <p className="truncate text-sm text-line-200">{name}</p>
                      <span className={`shrink-0 rounded-full px-2 py-0.5 text-[10px] font-semibold ${STATUS_STYLES[c.status]}`}>
                        {c.status}
                      </span>
                    </div>
                    <p className="mt-0.5 flex items-center gap-1.5 text-xs text-muted-400">
                      <ChannelIcon className="h-3 w-3" />
                      {CHANNEL_LABELS[c.channel]} · {c.messageCount} msgs
                    </p>
                  </div>
                </button>
              )
            })}
          </div>
        </div>
      )}

      {selectedId && (
        <ConversationDetail
          conversationId={selectedId}
          conversation={data?.find((c) => c.id === selectedId)}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  )
}

const ROLE_STYLES: Record<Message['role'], string> = {
  User: 'self-end bg-gradient-to-br from-teal-500 to-teal-500/80 text-white rounded-br-sm',
  Ai: 'self-start bg-ink-800 text-line-200 rounded-bl-sm ring-1 ring-ink-700',
  Agent: 'self-start bg-purple-500/20 text-purple-200 rounded-bl-sm ring-1 ring-purple-500/20',
  System: 'self-center bg-ink-800/50 text-muted-400 text-xs italic',
}

function ConversationDetail({
  conversationId,
  conversation,
  onClose,
}: {
  conversationId: string
  conversation: Conversation | undefined
  onClose: () => void
}) {
  const { data: messages, isLoading } = useQuery({
    queryKey: ['conversation-messages', conversationId],
    queryFn: async () => {
      const { data } = await api.get<Message[]>(`/api/conversations/${conversationId}/messages`)
      return data
    },
    // Live-updating transcript view — useful while watching a real-time test
    // conversation come through the widget or WhatsApp during Sprint 3 testing.
    refetchInterval: 3_000,
  })

  const ChannelIcon = conversation ? CHANNEL_ICONS[conversation.channel] : MessagesSquare

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 px-4 backdrop-blur-sm" role="presentation">
      <button
        type="button"
        aria-label="Close"
        onClick={onClose}
        className="absolute inset-0 cursor-default"
        tabIndex={-1}
      />
      <div
        role="dialog"
        aria-modal="true"
        className="glass-card relative flex h-[80vh] w-full max-w-lg flex-col overflow-hidden rounded-2xl shadow-2xl"
      >
        <div className="flex items-center justify-between border-b border-ink-700 px-5 py-4">
          <div className="flex items-center gap-3">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-teal-500/25 to-purple-500/25 text-mint-300 ring-1 ring-teal-500/20">
              <ChannelIcon className="h-4 w-4" />
            </span>
            <div>
              <p className="text-sm font-semibold text-white">
                {conversation?.customerDisplayName ?? conversation?.customerId ?? 'Conversation'}
              </p>
              <p className="text-xs text-muted-400">
                {conversation ? CHANNEL_LABELS[conversation.channel] : ''} · {conversation?.status}
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-lg p-1.5 text-muted-400 transition-colors hover:bg-ink-800 hover:text-line-200"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="flex flex-1 flex-col gap-2.5 overflow-y-auto px-5 py-4">
          {isLoading && (
            <div className="flex items-center gap-2 text-sm text-muted-400">
              <div className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-teal-400/30 border-t-teal-400" />
              Loading transcript…
            </div>
          )}

          {messages?.length === 0 && <p className="text-sm text-muted-400">No messages yet.</p>}

          {messages?.map((m) => (
            <div key={m.id} className={`flex max-w-[85%] flex-col gap-0.5 rounded-2xl px-3.5 py-2 text-sm ${ROLE_STYLES[m.role]}`}>
              <span>{m.content}</span>
              <span className="text-[10px] opacity-60">
                {m.role === 'Ai' ? 'AI · ' : ''}
                {new Date(m.sentAt).toLocaleTimeString()}
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

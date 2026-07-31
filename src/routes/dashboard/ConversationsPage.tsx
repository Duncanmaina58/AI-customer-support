import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { X } from 'lucide-react'
import { api } from '@/lib/api'
import type { Conversation, Message } from '@/lib/types'

const STATUS_STYLES: Record<Conversation['status'], string> = {
  Open: 'bg-teal-500/15 text-mint-300',
  Pending: 'bg-amber-500/15 text-amber-500',
  Resolved: 'bg-green-500/15 text-green-500',
  Escalated: 'bg-coral-500/15 text-coral-500',
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
      <header className="mb-6">
        <h1 className="text-xl font-semibold text-white">Conversations</h1>
        <p className="mt-1 text-sm text-muted-400">Across every connected channel, scoped to your company only.</p>
      </header>

      {isLoading && <p className="text-sm text-muted-400">Loading conversations…</p>}

      {isError && (
        <div className="rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          Couldn't reach the API. Is the backend running at the configured VITE_API_BASE_URL?
        </div>
      )}

      {data && data.length === 0 && (
        <div className="rounded-xl border border-dashed border-ink-700 p-10 text-center">
          <p className="text-sm text-muted-400">No conversations yet. Connect a channel to start receiving them.</p>
        </div>
      )}

      {data && data.length > 0 && (
        <div className="overflow-hidden rounded-xl border border-ink-700">
          <table className="w-full text-left text-sm">
            <thead className="bg-ink-900 text-xs uppercase tracking-wide text-muted-400">
              <tr>
                <th className="px-4 py-3">Customer</th>
                <th className="px-4 py-3">Channel</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Messages</th>
                <th className="px-4 py-3">Started</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-700 bg-ink-900/40">
              {data.map((c) => (
                <tr
                  key={c.id}
                  onClick={() => setSelectedId(c.id)}
                  className="cursor-pointer hover:bg-ink-800"
                >
                  <td className="px-4 py-3 text-line-200">{c.customerDisplayName ?? c.customerId}</td>
                  <td className="px-4 py-3 text-muted-400">{CHANNEL_LABELS[c.channel]}</td>
                  <td className="px-4 py-3">
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_STYLES[c.status]}`}>
                      {c.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-muted-400">{c.messageCount}</td>
                  <td className="px-4 py-3 text-muted-400">{new Date(c.createdAt).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
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
  User: 'self-end bg-teal-500 text-white rounded-br-sm',
  Ai: 'self-start bg-ink-800 text-line-200 rounded-bl-sm',
  Agent: 'self-start bg-purple-500/20 text-purple-200 rounded-bl-sm',
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

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 px-4" role="presentation">
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
        className="relative flex h-[80vh] w-full max-w-lg flex-col rounded-xl border border-ink-700 bg-ink-900 shadow-xl"
      >
        <div className="flex items-center justify-between border-b border-ink-700 px-5 py-4">
          <div>
            <p className="text-sm font-semibold text-white">
              {conversation?.customerDisplayName ?? conversation?.customerId ?? 'Conversation'}
            </p>
            <p className="text-xs text-muted-400">
              {conversation ? CHANNEL_LABELS[conversation.channel] : ''} · {conversation?.status}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-md p-1.5 text-muted-400 hover:bg-ink-800 hover:text-line-200"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="flex flex-1 flex-col gap-2 overflow-y-auto px-5 py-4">
          {isLoading && <p className="text-sm text-muted-400">Loading transcript…</p>}

          {messages?.length === 0 && (
            <p className="text-sm text-muted-400">No messages yet.</p>
          )}

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

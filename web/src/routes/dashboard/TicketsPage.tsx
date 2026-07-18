import { useState, type FormEvent } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  AlertTriangle, X, MessageSquare, Send, CheckCircle2,
  Clock, UserCheck, Loader2, Filter, Users
} from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import type { TicketListItem, TicketDetail, AgentListItem } from '@/lib/types'

const TEAMS = ['Support', 'Finance', 'IT', 'Logistics', 'Billing']

// ---- Status / priority display maps ----------------------------------------

const STATUS_STYLES: Record<string, string> = {
  Open:       'bg-teal-500/15 text-mint-300',
  InProgress: 'bg-amber-500/15 text-amber-400',
  Resolved:   'bg-green-500/15 text-green-400',
  Closed:     'bg-ink-700 text-muted-400',
}

const PRIORITY_STYLES: Record<string, string> = {
  Low:    'text-muted-400',
  Medium: 'text-sky-400',
  High:   'text-amber-400',
  Urgent: 'text-coral-500',
}

const STATUS_LABELS: Record<string, string> = {
  Open: 'Open', InProgress: 'In Progress', Resolved: 'Resolved', Closed: 'Closed',
}

const ROLE_STYLES: Record<string, string> = {
  User:   'self-end bg-teal-500 text-white rounded-br-sm',
  Ai:     'self-start bg-ink-800 text-line-200 rounded-bl-sm',
  Agent:  'self-start bg-purple-500/20 text-purple-200 rounded-bl-sm border border-purple-500/30',
  System: 'self-center bg-ink-800/40 text-muted-400 text-xs italic',
}

const ROLE_LABEL: Record<string, string> = {
  User: 'Customer', Ai: 'AI', Agent: 'Agent', System: 'System',
}

const CHANNEL_LABELS: Record<string, string> = {
  WebChat: 'Web Chat', WhatsApp: 'WhatsApp', Email: 'Email',
  Messenger: 'Messenger', Telegram: 'Telegram',
}

// ---- Page ------------------------------------------------------------------

const ALL_STATUSES = ['Open', 'InProgress', 'Resolved', 'Closed'] as const

export function TicketsPage() {
  const [filterStatus, setFilterStatus] = useState<string>('')
  const [filterPriority, setFilterPriority] = useState<string>('')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['tickets', filterStatus, filterPriority],
    queryFn: async () => {
      const params = new URLSearchParams()
      if (filterStatus)   params.set('status',   filterStatus)
      if (filterPriority) params.set('priority', filterPriority)
      const { data } = await api.get<TicketListItem[]>(`/api/tickets?${params}`)
      return data
    },
    refetchInterval: 15_000,
  })

  const openCount     = data?.filter(t => t.status === 'Open').length     ?? 0
  const progressCount = data?.filter(t => t.status === 'InProgress').length ?? 0

  return (
    <div>
      <header className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-white">Tickets</h1>
          <p className="mt-1 text-sm text-muted-400">
            Escalations that need a human touch.
            {data && (
              <span className="ml-2 text-line-200">
                {openCount} open · {progressCount} in progress
              </span>
            )}
          </p>
        </div>

        {/* Filters */}
        <div className="flex items-center gap-2">
          <Filter className="h-4 w-4 text-muted-400" />
          <select
            value={filterStatus}
            onChange={e => setFilterStatus(e.target.value)}
            className="rounded-lg border border-ink-700 bg-ink-900 px-3 py-1.5 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          >
            <option value="">All statuses</option>
            {ALL_STATUSES.map(s => (
              <option key={s} value={s}>{STATUS_LABELS[s]}</option>
            ))}
          </select>
          <select
            value={filterPriority}
            onChange={e => setFilterPriority(e.target.value)}
            className="rounded-lg border border-ink-700 bg-ink-900 px-3 py-1.5 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          >
            <option value="">All priorities</option>
            {['Low', 'Medium', 'High', 'Urgent'].map(p => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </div>
      </header>

      {isLoading && (
        <div className="flex items-center gap-2 text-sm text-muted-400">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading tickets…
        </div>
      )}

      {isError && (
        <div className="flex items-center gap-2 rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          <AlertTriangle className="h-4 w-4 shrink-0" />
          Couldn't load tickets. Is the backend running?
        </div>
      )}

      {data?.length === 0 && (
        <div className="rounded-xl border border-dashed border-ink-700 py-16 text-center">
          <CheckCircle2 className="mx-auto mb-3 h-8 w-8 text-green-500/40" />
          <p className="text-sm text-muted-400">No tickets yet — the AI is handling everything!</p>
        </div>
      )}

      {data && data.length > 0 && (
        <div className="overflow-hidden rounded-xl border border-ink-700">
          <table className="w-full text-left text-sm">
            <thead className="bg-ink-900 text-xs uppercase tracking-wide text-muted-400">
              <tr>
                <th className="px-4 py-3">#</th>
                <th className="px-4 py-3">Subject</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Priority</th>
                <th className="px-4 py-3">Channel</th>
                <th className="px-4 py-3">Customer</th>
                <th className="px-4 py-3">Team</th>
                <th className="px-4 py-3">Created</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-700 bg-ink-900/40">
              {data.map(ticket => (
                <tr
                  key={ticket.id}
                  onClick={() => setSelectedId(ticket.id)}
                  className="cursor-pointer hover:bg-ink-800"
                >
                  <td className="px-4 py-3 font-mono text-xs text-muted-400">
                    #{ticket.ticketNumber}
                  </td>
                  <td className="max-w-[220px] px-4 py-3">
                    <p className="truncate text-line-200">{ticket.subject}</p>
                    {ticket.escalationReason && (
                      <p className="truncate text-xs text-muted-400">{ticket.escalationReason}</p>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_STYLES[ticket.status]}`}>
                      {STATUS_LABELS[ticket.status]}
                    </span>
                  </td>
                  <td className={`px-4 py-3 text-xs font-medium ${PRIORITY_STYLES[ticket.priority]}`}>
                    {ticket.priority}
                  </td>
                  <td className="px-4 py-3 text-muted-400">
                    {CHANNEL_LABELS[ticket.conversationChannel] ?? ticket.conversationChannel}
                  </td>
                  <td className="px-4 py-3 text-muted-400">
                    {ticket.customerIdentifier.length > 28
                      ? ticket.customerIdentifier.slice(0, 28) + '…'
                      : ticket.customerIdentifier}
                  </td>
                  <td className="px-4 py-3 text-muted-400">
                    {ticket.assignedTeam ?? <span className="text-ink-600">—</span>}
                  </td>
                  <td className="px-4 py-3 text-xs text-muted-400">
                    {new Date(ticket.createdAt).toLocaleString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {selectedId && (
        <TicketDetailModal
          ticketId={selectedId}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  )
}

// ---- Ticket detail modal ---------------------------------------------------

function TicketDetailModal({ ticketId, onClose }: { ticketId: string; onClose: () => void }) {
  const queryClient = useQueryClient()
  const { agent } = useAuth()
  const canAssign = agent?.role === 'Owner' || agent?.role === 'Admin'
  const [reply, setReply] = useState('')
  const [replyError, setReplyError] = useState<string | null>(null)
  const [isAssigning, setIsAssigning] = useState(false)

  const { data, isLoading } = useQuery({
    queryKey: ['ticket', ticketId],
    queryFn: async () => {
      const { data } = await api.get<TicketDetail>(`/api/tickets/${ticketId}`)
      return data
    },
    refetchInterval: 5_000,
  })

  const statusMutation = useMutation({
    mutationFn: (status: string) =>
      api.patch(`/api/tickets/${ticketId}/status`, { status }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ticket', ticketId] })
      queryClient.invalidateQueries({ queryKey: ['tickets'] })
    },
  })

  const { data: agents } = useQuery({
    queryKey: ['agents'],
    queryFn: async () => {
      const { data } = await api.get<AgentListItem[]>('/api/agents')
      return data
    },
    enabled: canAssign && isAssigning,
  })

  const assignMutation = useMutation({
    mutationFn: (body: { agentId: string | null; team: string | null }) =>
      api.post(`/api/tickets/${ticketId}/assign`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ticket', ticketId] })
      queryClient.invalidateQueries({ queryKey: ['tickets'] })
      setIsAssigning(false)
    },
  })

  const replyMutation = useMutation({
    mutationFn: (message: string) =>
      api.post(`/api/tickets/${ticketId}/reply`, { message }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ticket', ticketId] })
      setReply('')
      setReplyError(null)
    },
    onError: (err: any) => {
      setReplyError(err?.response?.data?.message ?? 'Failed to send reply.')
    },
  })

  function handleReplySubmit(e: FormEvent) {
    e.preventDefault()
    if (!reply.trim()) return
    replyMutation.mutate(reply.trim())
  }

  const nextStatus: Record<string, string> = {
    Open: 'InProgress',
    InProgress: 'Resolved',
    Resolved: 'Closed',
  }

  const nextStatusLabel: Record<string, string> = {
    Open: 'Mark In Progress',
    InProgress: 'Mark Resolved',
    Resolved: 'Close Ticket',
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 px-4" role="presentation">
      <button type="button" aria-label="Close" onClick={onClose} className="absolute inset-0 cursor-default" tabIndex={-1} />

      <div
        role="dialog"
        aria-modal="true"
        className="relative flex h-[85vh] w-full max-w-2xl flex-col rounded-xl border border-ink-700 bg-ink-900 shadow-xl"
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-ink-700 px-5 py-4">
          {isLoading ? (
            <div className="flex items-center gap-2 text-sm text-muted-400">
              <Loader2 className="h-4 w-4 animate-spin" /> Loading…
            </div>
          ) : data ? (
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-xs text-muted-400">#{data.ticketNumber}</span>
                <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_STYLES[data.status]}`}>
                  {STATUS_LABELS[data.status]}
                </span>
                <span className={`text-xs font-medium ${PRIORITY_STYLES[data.priority]}`}>
                  {data.priority}
                </span>
              </div>
              <p className="mt-1 truncate text-sm font-semibold text-white">{data.subject}</p>
              <p className="text-xs text-muted-400">
                {CHANNEL_LABELS[data.conversationChannel] ?? data.conversationChannel} ·{' '}
                {data.customerDisplayName ?? data.customerIdentifier}
                {data.assignedTeam && <> · <UserCheck className="inline h-3 w-3" /> {data.assignedTeam}</>}
                {data.assignedToName && <> · {data.assignedToName}</>}
              </p>
              {data.escalationReason && (
                <p className="mt-1 text-xs text-amber-400">
                  <AlertTriangle className="inline h-3 w-3 mr-1" />{data.escalationReason}
                </p>
              )}
              {canAssign && (
                <AssignPanel
                  isOpen={isAssigning}
                  onToggle={() => setIsAssigning((v) => !v)}
                  agents={agents}
                  currentAgentId={data.assignedToId}
                  currentTeam={data.assignedTeam}
                  onAssign={(agentId, team) => assignMutation.mutate({ agentId, team })}
                  isSaving={assignMutation.isPending}
                />
              )}
            </div>
          ) : null}

          <div className="flex shrink-0 items-center gap-2">
            {data && nextStatus[data.status] && (
              <button
                type="button"
                onClick={() => statusMutation.mutate(nextStatus[data.status])}
                disabled={statusMutation.isPending}
                className="flex items-center gap-1.5 rounded-lg border border-ink-600 px-3 py-1.5 text-xs text-line-200 hover:bg-ink-800 disabled:opacity-50"
              >
                {statusMutation.isPending
                  ? <Loader2 className="h-3 w-3 animate-spin" />
                  : <Clock className="h-3 w-3" />}
                {nextStatusLabel[data.status]}
              </button>
            )}
            <button
              type="button"
              onClick={onClose}
              aria-label="Close"
              className="rounded p-1.5 text-muted-400 hover:bg-ink-800"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        {/* Transcript */}
        <div className="flex flex-1 flex-col gap-2.5 overflow-y-auto px-5 py-4">
          {isLoading && <p className="text-sm text-muted-400">Loading transcript…</p>}

          {data?.messages.map(m => (
            <div
              key={m.id}
              className={`flex max-w-[85%] flex-col gap-0.5 rounded-2xl px-3.5 py-2 text-sm ${ROLE_STYLES[m.role]}`}
            >
              <span className="text-[10px] font-medium opacity-60">{ROLE_LABEL[m.role]}</span>
              <span className="whitespace-pre-wrap leading-relaxed">{m.content}</span>
              <span className="text-[10px] opacity-50">{new Date(m.sentAt).toLocaleTimeString()}</span>
            </div>
          ))}
        </div>

        {/* Reply box — only for non-closed tickets */}
        {data && data.status !== 'Closed' && data.status !== 'Resolved' && (
          <form
            onSubmit={handleReplySubmit}
            className="border-t border-ink-700 px-5 py-3"
          >
            <div className="flex items-end gap-2">
              <div className="flex-1">
                <textarea
                  value={reply}
                  onChange={e => setReply(e.target.value)}
                  placeholder="Type your reply… it will be sent via the original channel"
                  rows={3}
                  className="w-full resize-none rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
                />
                {replyError && (
                  <p className="mt-1 text-xs text-coral-500">{replyError}</p>
                )}
              </div>
              <button
                type="submit"
                disabled={!reply.trim() || replyMutation.isPending}
                aria-label="Send reply"
                className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-teal-500 text-white hover:bg-teal-400 disabled:opacity-50"
              >
                {replyMutation.isPending
                  ? <Loader2 className="h-4 w-4 animate-spin" />
                  : <Send className="h-4 w-4" />}
              </button>
            </div>
            <p className="mt-1.5 flex items-center gap-1 text-xs text-muted-400">
              <MessageSquare className="h-3 w-3" />
              Replied via{' '}
              {CHANNEL_LABELS[data.conversationChannel] ?? data.conversationChannel}
            </p>
          </form>
        )}

        {data?.status === 'Resolved' && (
          <div className="flex items-center justify-between border-t border-ink-700 px-5 py-3">
            <p className="text-xs text-green-400 flex items-center gap-1.5">
              <CheckCircle2 className="h-3.5 w-3.5" /> Resolved {data.resolvedAt ? new Date(data.resolvedAt).toLocaleString() : ''}
            </p>
            <button
              type="button"
              onClick={() => statusMutation.mutate('Closed')}
              disabled={statusMutation.isPending}
              className="text-xs text-muted-400 hover:text-line-200 underline"
            >
              Close ticket
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

/**
 * Inline agent/team assignment control shown in the ticket detail modal, visible
 * only to Owner/Admin roles (matches TicketsController.Assign's [Authorize] policy).
 * Deliberately a toggle-open row rather than always-visible selects, so the header
 * stays scannable for the common case of just reading a ticket.
 */
function AssignPanel({
  isOpen,
  onToggle,
  agents,
  currentAgentId,
  currentTeam,
  onAssign,
  isSaving,
}: {
  isOpen: boolean
  onToggle: () => void
  agents: AgentListItem[] | undefined
  currentAgentId: string | null
  currentTeam: string | null
  onAssign: (agentId: string | null, team: string | null) => void
  isSaving: boolean
}) {
  const [agentId, setAgentId] = useState(currentAgentId ?? '')
  const [team, setTeam] = useState(currentTeam ?? '')

  if (!isOpen) {
    return (
      <button
        type="button"
        onClick={onToggle}
        className="mt-1.5 flex items-center gap-1 text-xs text-mint-300 hover:underline"
      >
        <Users className="h-3 w-3" />
        {currentAgentId || currentTeam ? 'Reassign' : 'Assign'}
      </button>
    )
  }

  return (
    <div className="mt-2 flex flex-wrap items-center gap-2 rounded-lg border border-ink-700 bg-ink-950 p-2.5">
      <select
        value={agentId}
        onChange={(e) => setAgentId(e.target.value)}
        className="rounded-md border border-ink-700 bg-ink-900 px-2 py-1 text-xs text-line-200 focus:border-teal-400 focus:outline-none"
      >
        <option value="">Unassigned agent</option>
        {agents?.map((a) => (
          <option key={a.id} value={a.id}>{a.name}</option>
        ))}
      </select>
      <select
        value={team}
        onChange={(e) => setTeam(e.target.value)}
        className="rounded-md border border-ink-700 bg-ink-900 px-2 py-1 text-xs text-line-200 focus:border-teal-400 focus:outline-none"
      >
        <option value="">Keep current team</option>
        {TEAMS.map((t) => (
          <option key={t} value={t}>{t}</option>
        ))}
      </select>
      <button
        type="button"
        onClick={() => onAssign(agentId || null, team || null)}
        disabled={isSaving}
        className="rounded-md bg-teal-500 px-2.5 py-1 text-xs font-medium text-white hover:bg-teal-400 disabled:opacity-60"
      >
        {isSaving ? 'Saving…' : 'Save'}
      </button>
      <button
        type="button"
        onClick={onToggle}
        className="text-xs text-muted-400 hover:text-line-200"
      >
        Cancel
      </button>
    </div>
  )
}

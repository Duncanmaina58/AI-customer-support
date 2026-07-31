import { useState, type FormEvent } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  AlertTriangle, X, MessageSquare, Send, CheckCircle2,
  Clock, UserCheck, Loader2, Filter, Users, Ticket as TicketIcon,
} from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import type { TicketListItem, TicketDetail, AgentListItem } from '@/lib/types'

const TEAMS = ['Support', 'Finance', 'IT', 'Logistics', 'Billing']

// ---- Status / priority display maps ----------------------------------------

const STATUS_STYLES: Record<string, string> = {
  Open:       'bg-teal-500/15 text-mint-300 ring-1 ring-teal-500/20',
  InProgress: 'bg-amber-500/15 text-amber-400 ring-1 ring-amber-500/20',
  Resolved:   'bg-green-500/15 text-green-400 ring-1 ring-green-500/20',
  Closed:     'bg-ink-700 text-muted-400 ring-1 ring-ink-700',
}

const PRIORITY_STYLES: Record<string, string> = {
  Low:    'text-muted-400',
  Medium: 'text-sky-400',
  High:   'text-amber-400',
  Urgent: 'text-coral-500',
}

const PRIORITY_DOT: Record<string, string> = {
  Low: 'bg-muted-400',
  Medium: 'bg-sky-400',
  High: 'bg-amber-400',
  Urgent: 'bg-coral-500',
}

const STATUS_LABELS: Record<string, string> = {
  Open: 'Open', InProgress: 'In Progress', Resolved: 'Resolved', Closed: 'Closed',
}

const ROLE_STYLES: Record<string, string> = {
  User:   'self-end bg-gradient-to-br from-teal-500 to-teal-500/80 text-white rounded-br-sm',
  Ai:     'self-start bg-ink-800 text-line-200 rounded-bl-sm ring-1 ring-ink-700',
  Agent:  'self-start bg-purple-500/20 text-purple-200 rounded-bl-sm ring-1 ring-purple-500/30',
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

const selectClass =
  'appearance-none rounded-lg border border-ink-700 bg-ink-900 px-3 py-1.5 pr-8 text-sm text-line-200 transition-colors focus:border-teal-400 focus:outline-none hover:border-ink-600'

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
      <header className="animate-reveal mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="mb-2 flex items-center gap-2">
            <span className="h-1.5 w-1.5 rounded-full bg-amber-400 trend-dot" />
            <span className="text-[11px] font-semibold uppercase tracking-[0.12em] text-teal-400">Escalation queue</span>
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-white">Tickets</h1>
          <p className="mt-1 text-sm text-muted-400">
            Escalations that need a human touch.
            {data && (
              <span className="ml-2 font-mono text-line-200">
                {openCount} open · {progressCount} in progress
              </span>
            )}
          </p>
        </div>

        {/* Filters */}
        <div className="flex items-center gap-2">
          <Filter className="h-4 w-4 shrink-0 text-muted-400" />
          <div className="relative">
            <select
              value={filterStatus}
              onChange={e => setFilterStatus(e.target.value)}
              className={selectClass}
            >
              <option value="">All statuses</option>
              {ALL_STATUSES.map(s => (
                <option key={s} value={s}>{STATUS_LABELS[s]}</option>
              ))}
            </select>
          </div>
          <div className="relative">
            <select
              value={filterPriority}
              onChange={e => setFilterPriority(e.target.value)}
              className={selectClass}
            >
              <option value="">All priorities</option>
              {['Low', 'Medium', 'High', 'Urgent'].map(p => (
                <option key={p} value={p}>{p}</option>
              ))}
            </select>
          </div>
        </div>
      </header>

      {isLoading && (
        <div className="glass-card animate-reveal flex items-center gap-2 rounded-2xl p-6 text-sm text-muted-400">
          <Loader2 className="h-4 w-4 animate-spin text-teal-400" /> Loading tickets…
        </div>
      )}

      {isError && (
        <div className="flex items-center gap-2 rounded-2xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          <AlertTriangle className="h-4 w-4 shrink-0" />
          Couldn't load tickets. Is the backend running?
        </div>
      )}

      {data?.length === 0 && (
        <div className="glass-card animate-reveal rounded-2xl border-dashed py-16 text-center">
          <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-2xl bg-green-500/10 ring-1 ring-green-500/20">
            <CheckCircle2 className="h-5 w-5 text-green-400" />
          </div>
          <p className="text-sm text-muted-400">No tickets yet — the AI is handling everything!</p>
        </div>
      )}

      {data && data.length > 0 && (
        <div className="glass-card animate-reveal overflow-hidden rounded-2xl">
          {/* Desktop table */}
          <table className="hidden w-full text-left text-sm lg:table">
            <thead className="text-[11px] uppercase tracking-wider text-muted-400">
              <tr className="border-b border-ink-700">
                <th className="px-5 py-3.5 font-semibold">#</th>
                <th className="px-5 py-3.5 font-semibold">Subject</th>
                <th className="px-5 py-3.5 font-semibold">Status</th>
                <th className="px-5 py-3.5 font-semibold">Priority</th>
                <th className="px-5 py-3.5 font-semibold">Channel</th>
                <th className="px-5 py-3.5 font-semibold">Customer</th>
                <th className="px-5 py-3.5 font-semibold">Team</th>
                <th className="px-5 py-3.5 font-semibold">Created</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-700">
              {data.map(ticket => (
                <tr
                  key={ticket.id}
                  onClick={() => setSelectedId(ticket.id)}
                  className="cursor-pointer transition-colors hover:bg-ink-800/60"
                >
                  <td className="px-5 py-3.5 font-mono text-xs text-muted-400">
                    #{ticket.ticketNumber}
                  </td>
                  <td className="max-w-[220px] px-5 py-3.5">
                    <p className="truncate text-line-200">{ticket.subject}</p>
                    {ticket.escalationReason && (
                      <p className="truncate text-xs text-muted-400">{ticket.escalationReason}</p>
                    )}
                  </td>
                  <td className="px-5 py-3.5">
                    <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${STATUS_STYLES[ticket.status]}`}>
                      {STATUS_LABELS[ticket.status]}
                    </span>
                  </td>
                  <td className="px-5 py-3.5">
                    <span className={`inline-flex items-center gap-1.5 text-xs font-semibold ${PRIORITY_STYLES[ticket.priority]}`}>
                      <span className={`h-1.5 w-1.5 rounded-full ${PRIORITY_DOT[ticket.priority]}`} />
                      {ticket.priority}
                    </span>
                  </td>
                  <td className="px-5 py-3.5 text-muted-400">
                    {CHANNEL_LABELS[ticket.conversationChannel] ?? ticket.conversationChannel}
                  </td>
                  <td className="px-5 py-3.5 text-muted-400">
                    {ticket.customerIdentifier.length > 28
                      ? ticket.customerIdentifier.slice(0, 28) + '…'
                      : ticket.customerIdentifier}
                  </td>
                  <td className="px-5 py-3.5 text-muted-400">
                    {ticket.assignedTeam ?? <span className="text-ink-600">—</span>}
                  </td>
                  <td className="px-5 py-3.5 text-xs text-muted-400">
                    {new Date(ticket.createdAt).toLocaleString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* Mobile / tablet stacked cards */}
          <div className="divide-y divide-ink-700 lg:hidden">
            {data.map(ticket => (
              <button
                key={ticket.id}
                onClick={() => setSelectedId(ticket.id)}
                className="flex w-full flex-col gap-1.5 px-4 py-3.5 text-left transition-colors active:bg-ink-800"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-mono text-xs text-muted-400">#{ticket.ticketNumber}</span>
                  <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold ${STATUS_STYLES[ticket.status]}`}>
                    {STATUS_LABELS[ticket.status]}
                  </span>
                </div>
                <p className="truncate text-sm text-line-200">{ticket.subject}</p>
                <div className="flex items-center gap-3 text-xs text-muted-400">
                  <span className={`inline-flex items-center gap-1 font-semibold ${PRIORITY_STYLES[ticket.priority]}`}>
                    <span className={`h-1.5 w-1.5 rounded-full ${PRIORITY_DOT[ticket.priority]}`} />
                    {ticket.priority}
                  </span>
                  <span>{CHANNEL_LABELS[ticket.conversationChannel] ?? ticket.conversationChannel}</span>
                  <span>{new Date(ticket.createdAt).toLocaleDateString()}</span>
                </div>
              </button>
            ))}
          </div>
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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 px-4 backdrop-blur-sm" role="presentation">
      <button type="button" aria-label="Close" onClick={onClose} className="absolute inset-0 cursor-default" tabIndex={-1} />

      <div
        role="dialog"
        aria-modal="true"
        className="glass-card relative flex h-[85vh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl shadow-2xl"
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-ink-700 px-5 py-4">
          {isLoading ? (
            <div className="flex items-center gap-2 text-sm text-muted-400">
              <Loader2 className="h-4 w-4 animate-spin text-teal-400" /> Loading…
            </div>
          ) : data ? (
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-teal-500/20 to-purple-500/20 text-mint-300 ring-1 ring-teal-500/20">
                  <TicketIcon className="h-3.5 w-3.5" />
                </span>
                <span className="font-mono text-xs text-muted-400">#{data.ticketNumber}</span>
                <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${STATUS_STYLES[data.status]}`}>
                  {STATUS_LABELS[data.status]}
                </span>
                <span className={`inline-flex items-center gap-1 text-xs font-semibold ${PRIORITY_STYLES[data.priority]}`}>
                  <span className={`h-1.5 w-1.5 rounded-full ${PRIORITY_DOT[data.priority]}`} />
                  {data.priority}
                </span>
              </div>
              <p className="mt-1.5 truncate text-sm font-semibold text-white">{data.subject}</p>
              <p className="text-xs text-muted-400">
                {CHANNEL_LABELS[data.conversationChannel] ?? data.conversationChannel} ·{' '}
                {data.customerDisplayName ?? data.customerIdentifier}
                {data.assignedTeam && <> · <UserCheck className="inline h-3 w-3" /> {data.assignedTeam}</>}
                {data.assignedToName && <> · {data.assignedToName}</>}
              </p>
              {data.escalationReason && (
                <p className="mt-1 text-xs text-amber-400">
                  <AlertTriangle className="mr-1 inline h-3 w-3" />{data.escalationReason}
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
                className="flex items-center gap-1.5 rounded-lg border border-ink-600 px-3 py-1.5 text-xs text-line-200 transition-colors hover:bg-ink-800 disabled:opacity-50"
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
              className="rounded-lg p-1.5 text-muted-400 transition-colors hover:bg-ink-800 hover:text-line-200"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        {/* Transcript */}
        <div className="flex flex-1 flex-col gap-2.5 overflow-y-auto px-5 py-4">
          {isLoading && (
            <div className="flex items-center gap-2 text-sm text-muted-400">
              <Loader2 className="h-3.5 w-3.5 animate-spin text-teal-400" /> Loading transcript…
            </div>
          )}

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
                  className="w-full resize-none rounded-xl border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 transition-colors focus:border-teal-400 focus:outline-none"
                />
                {replyError && (
                  <p className="mt-1 text-xs text-coral-500">{replyError}</p>
                )}
              </div>
              <button
                type="submit"
                disabled={!reply.trim() || replyMutation.isPending}
                aria-label="Send reply"
                className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl text-white shadow-[0_3px_15px_rgba(99,102,241,0.25)] transition-all hover:-translate-y-0.5 disabled:opacity-50 disabled:hover:translate-y-0"
                style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
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
            <p className="flex items-center gap-1.5 text-xs text-green-400">
              <CheckCircle2 className="h-3.5 w-3.5" /> Resolved {data.resolvedAt ? new Date(data.resolvedAt).toLocaleString() : ''}
            </p>
            <button
              type="button"
              onClick={() => statusMutation.mutate('Closed')}
              disabled={statusMutation.isPending}
              className="text-xs text-muted-400 underline hover:text-line-200"
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
        className="mt-1.5 flex items-center gap-1 text-xs font-medium text-mint-300 hover:underline"
      >
        <Users className="h-3 w-3" />
        {currentAgentId || currentTeam ? 'Reassign' : 'Assign'}
      </button>
    )
  }

  return (
    <div className="mt-2 flex flex-wrap items-center gap-2 rounded-xl border border-ink-700 bg-ink-950 p-2.5">
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
        className="rounded-md bg-teal-500 px-2.5 py-1 text-xs font-semibold text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
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

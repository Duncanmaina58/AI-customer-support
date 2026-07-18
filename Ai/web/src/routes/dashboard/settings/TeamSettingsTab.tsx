import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { UserPlus } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import { Badge } from '@/components/Badge'
import { Modal } from '@/components/Modal'
import { CopyableSecret } from '@/components/CopyableSecret'
import type { AgentListItem, InviteAgentResponse } from '@/lib/types'

const ROLE_TONE = { Owner: 'purple', Admin: 'teal', Agent: 'muted' } as const

export function TeamSettingsTab() {
  const { agent: me } = useAuth()
  const canManage = me?.role === 'Owner' || me?.role === 'Admin'
  const queryClient = useQueryClient()

  const [isInviteOpen, setIsInviteOpen] = useState(false)
  const [justInvited, setJustInvited] = useState<InviteAgentResponse | null>(null)

  const { data: agents, isLoading, isError } = useQuery({
    queryKey: ['agents'],
    queryFn: async () => {
      const { data } = await api.get<AgentListItem[]>('/api/agents')
      return data
    },
  })

  const updateMutation = useMutation({
    mutationFn: async ({ id, ...body }: { id: string; role?: string; isActive?: boolean }) => {
      const { data } = await api.patch<AgentListItem>(`/api/agents/${id}`, body)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['agents'] })
    },
  })

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-white">Team ({agents?.length ?? 0})</h3>
        {canManage && (
          <button
            type="button"
            onClick={() => setIsInviteOpen(true)}
            className="flex items-center gap-1.5 rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-teal-400"
          >
            <UserPlus className="h-4 w-4" />
            Invite teammate
          </button>
        )}
      </div>

      {isLoading && <p className="text-sm text-muted-400">Loading team…</p>}
      {isError && (
        <div className="rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          Couldn't load the team list.
        </div>
      )}

      {agents && (
        <div className="overflow-hidden rounded-xl border border-ink-700">
          <table className="w-full text-left text-sm">
            <thead className="bg-ink-900 text-xs uppercase tracking-wide text-muted-400">
              <tr>
                <th className="px-4 py-3">Name</th>
                <th className="px-4 py-3">Role</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Last active</th>
                {canManage && <th className="px-4 py-3 text-right">Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-700 bg-ink-900/40">
              {agents.map((a) => {
                const isSelf = a.id === me?.id
                return (
                  <tr key={a.id} className="hover:bg-ink-800">
                    <td className="px-4 py-3">
                      <p className="text-line-200">
                        {a.name} {isSelf && <span className="text-muted-400">(you)</span>}
                      </p>
                      <p className="text-xs text-muted-400">{a.email}</p>
                    </td>
                    <td className="px-4 py-3">
                      <Badge tone={ROLE_TONE[a.role]}>{a.role}</Badge>
                    </td>
                    <td className="px-4 py-3">
                      <Badge tone={a.isActive ? 'green' : 'coral'}>{a.isActive ? 'Active' : 'Deactivated'}</Badge>
                    </td>
                    <td className="px-4 py-3 text-muted-400">
                      {a.lastActiveAt ? new Date(a.lastActiveAt).toLocaleString() : 'Never'}
                    </td>
                    {canManage && (
                      <td className="px-4 py-3 text-right">
                        {!isSelf && a.role !== 'Owner' && (
                          <div className="flex items-center justify-end gap-2">
                            <select
                              value={a.role}
                              onChange={(e) => updateMutation.mutate({ id: a.id, role: e.target.value })}
                              className="rounded-md border border-ink-700 bg-ink-950 px-2 py-1 text-xs text-line-200"
                            >
                              <option value="Admin">Admin</option>
                              <option value="Agent">Agent</option>
                            </select>
                            <button
                              type="button"
                              onClick={() => updateMutation.mutate({ id: a.id, isActive: !a.isActive })}
                              className="rounded-md border border-ink-700 px-2 py-1 text-xs text-muted-400 hover:text-line-200"
                            >
                              {a.isActive ? 'Deactivate' : 'Reactivate'}
                            </button>
                          </div>
                        )}
                      </td>
                    )}
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {isInviteOpen && (
        <InviteModal
          onClose={() => setIsInviteOpen(false)}
          onInvited={(response) => {
            setIsInviteOpen(false)
            setJustInvited(response)
            queryClient.invalidateQueries({ queryKey: ['agents'] })
          }}
        />
      )}

      {justInvited && (
        <Modal title={`${justInvited.agent.name} has been added`} onClose={() => setJustInvited(null)}>
          <div className="space-y-4">
            <p className="text-sm text-muted-400">
              Share this temporary password with them directly — it won't be shown again.
            </p>
            <CopyableSecret label="Temporary password" value={justInvited.temporaryPassword} />
            <button
              type="button"
              onClick={() => setJustInvited(null)}
              className="w-full rounded-lg bg-teal-500 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400"
            >
              Done
            </button>
          </div>
        </Modal>
      )}
    </div>
  )
}

function InviteModal({
  onClose,
  onInvited,
}: {
  onClose: () => void
  onInvited: (response: InviteAgentResponse) => void
}) {
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [role, setRole] = useState('Agent')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)
    try {
      const { data } = await api.post<InviteAgentResponse>('/api/agents/invite', { name, email, role })
      onInvited(data)
    } catch {
      setError('Could not invite that person — the email may already be in use.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Modal title="Invite a teammate" onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-4">
        {error && (
          <div role="alert" className="rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
            {error}
          </div>
        )}

        <div>
          <label htmlFor="inviteName" className="mb-1.5 block text-sm text-line-200">
            Name
          </label>
          <input
            id="inviteName"
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Jane Mwangi"
            className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />
        </div>

        <div>
          <label htmlFor="inviteEmail" className="mb-1.5 block text-sm text-line-200">
            Email
          </label>
          <input
            id="inviteEmail"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="jane@company.com"
            className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
          />
        </div>

        <div>
          <label htmlFor="inviteRole" className="mb-1.5 block text-sm text-line-200">
            Role
          </label>
          <select
            id="inviteRole"
            value={role}
            onChange={(e) => setRole(e.target.value)}
            className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 focus:border-teal-400"
          >
            <option value="Agent">Agent — handles conversations</option>
            <option value="Admin">Admin — also manages team & settings</option>
          </select>
        </div>

        <button
          type="submit"
          disabled={isSubmitting}
          className="w-full rounded-lg bg-teal-500 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
        >
          {isSubmitting ? 'Sending invite…' : 'Add teammate'}
        </button>
      </form>
    </Modal>
  )
}

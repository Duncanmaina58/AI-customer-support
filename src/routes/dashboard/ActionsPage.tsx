import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Plus, Trash2, X, Zap, AlertCircle, Loader2, RefreshCw, Settings2,
  History, ShieldCheck, ShieldOff, CheckCircle2, XCircle, Copy, Check,
} from 'lucide-react'
import { api } from '@/lib/api'
import type {
  ActionDefinition, CreateActionDefinitionRequest, UpdateActionDefinitionRequest,
  WebhookSecretReveal, ActionLogEntry,
} from '@/lib/types'

/* -------------------------------------------------------------------------- */
/* Page                                                                        */
/* -------------------------------------------------------------------------- */

export function ActionsPage() {
  const queryClient = useQueryClient()
  const [modal, setModal] = useState<'add' | { edit: ActionDefinition } | { secret: string } | null>(null)
  const [showLogs, setShowLogs] = useState(false)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['actions'],
    queryFn: async () => {
      const { data } = await api.get<ActionDefinition[]>('/api/actions')
      return data
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/actions/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['actions'] }),
  })
  const [deleteError, setDeleteError] = useState<string | null>(null)

  function handleDelete(action: ActionDefinition) {
    if (!confirm(`Remove "${action.displayName}"? This can't be undone.`)) return
    setDeleteError(null)
    deleteMutation.mutate(action.id, {
      onError: (err: any) => setDeleteError(err?.response?.data?.message ?? 'Could not delete this action.'),
    })
  }

  return (
    <div>
      <header className="mb-6 flex items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-white">Actions</h1>
          <p className="mt-1 max-w-xl text-sm text-muted-400">
            Let the AI do things for customers, not just answer questions — cancel an order, reset a PIN,
            check a balance — by calling your own backend through a signed webhook.
          </p>
        </div>
        <div className="flex shrink-0 gap-2">
          <button
            type="button"
            onClick={() => setShowLogs((v) => !v)}
            className="flex items-center gap-2 rounded-lg border border-ink-700 px-3.5 py-2 text-sm font-medium text-line-200 hover:bg-ink-800"
          >
            <History className="h-4 w-4" />
            {showLogs ? 'Hide audit log' : 'View audit log'}
          </button>
          <button
            type="button"
            onClick={() => setModal('add')}
            className="flex items-center gap-2 rounded-lg bg-teal-500 px-3.5 py-2 text-sm font-medium text-white hover:bg-teal-400"
          >
            <Plus className="h-4 w-4" />
            Add action
          </button>
        </div>
      </header>

      {deleteError && (
        <div className="mb-4 flex items-center gap-2 rounded-xl border border-coral-500/40 bg-coral-500/10 p-3 text-sm text-coral-500">
          <AlertCircle className="h-4 w-4 shrink-0" />
          {deleteError}
        </div>
      )}

      {showLogs && <AuditLogPanel />}

      {isLoading && (
        <div className="flex items-center gap-2 text-sm text-muted-400">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading actions…
        </div>
      )}

      {isError && (
        <div className="flex items-center gap-2 rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          <AlertCircle className="h-4 w-4 shrink-0" />
          Couldn't load actions. Is the backend running?
        </div>
      )}

      {data?.length === 0 && (
        <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-ink-700 py-16 text-center">
          <Zap className="mb-3 h-8 w-8 text-muted-400/40" />
          <p className="text-sm font-medium text-line-200">No actions registered yet</p>
          <p className="mt-1 max-w-sm text-xs text-muted-400">
            Register your first action and the AI will be able to perform it for customers who ask,
            not just talk about it.
          </p>
          <button
            type="button"
            onClick={() => setModal('add')}
            className="mt-4 flex items-center gap-1.5 rounded-lg bg-teal-500/10 px-3 py-1.5 text-sm font-medium text-teal-400 hover:bg-teal-500/20"
          >
            <Plus className="h-3.5 w-3.5" /> Add your first action
          </button>
        </div>
      )}

      {data && data.length > 0 && (
        <div className="space-y-3">
          {data.map((action) => (
            <ActionCard
              key={action.id}
              action={action}
              onEdit={() => setModal({ edit: action })}
              onDelete={() => handleDelete(action)}
              onSecretRegenerated={(secret) => setModal({ secret })}
              isDeleting={deleteMutation.isPending && deleteMutation.variables === action.id}
            />
          ))}
        </div>
      )}

      {modal === 'add' && <AddActionModal onClose={() => setModal(null)} />}

      {modal !== null && modal !== 'add' && 'edit' in modal && (
        <EditActionModal action={modal.edit} onClose={() => setModal(null)} />
      )}

      {modal !== null && modal !== 'add' && 'secret' in modal && (
        <SecretRevealModal secret={modal.secret} onClose={() => setModal(null)} />
      )}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Action card                                                                 */
/* -------------------------------------------------------------------------- */

function ActionCard({
  action,
  onEdit,
  onDelete,
  onSecretRegenerated,
  isDeleting,
}: {
  action: ActionDefinition
  onEdit: () => void
  onDelete: () => void
  onSecretRegenerated: (secret: string) => void
  isDeleting: boolean
}) {
  const queryClient = useQueryClient()

  const regenerateMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<WebhookSecretReveal>(`/api/actions/${action.id}/regenerate-secret`)
      return data
    },
    onSuccess: (data) => {
      onSecretRegenerated(data.webhookSecret)
      queryClient.invalidateQueries({ queryKey: ['actions'] })
    },
  })

  const successRate = action.totalExecutions > 0
    ? Math.round((action.successfulExecutions / action.totalExecutions) * 100)
    : null

  return (
    <div className="rounded-xl border border-ink-700 bg-ink-900 p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <Zap className="h-3.5 w-3.5 shrink-0 text-muted-400" />
            <p className="truncate text-sm font-semibold text-white">{action.displayName}</p>
            <code className="shrink-0 rounded bg-ink-800 px-1.5 py-0.5 text-[11px] text-mint-300">{action.actionType}</code>
            {!action.isActive && (
              <span className="shrink-0 rounded-full bg-muted-400/10 px-2 py-0.5 text-[11px] font-medium text-muted-400">Inactive</span>
            )}
          </div>
          <p className="mt-1 truncate text-xs text-muted-400">{action.webhookUrl}</p>
          <div className="mt-2 flex flex-wrap items-center gap-3 text-xs text-muted-400">
            <span className="flex items-center gap-1.5">
              {action.requiresVerification ? (
                <><ShieldCheck className="h-3.5 w-3.5 text-teal-400" /> Requires verification</>
              ) : (
                <><ShieldOff className="h-3.5 w-3.5" /> No verification</>
              )}
            </span>
            {action.isReadOnly && <span>Read-only</span>}
            <span>{action.timeoutSeconds}s timeout</span>
            {action.totalExecutions > 0 && (
              <span>{action.totalExecutions} run{action.totalExecutions === 1 ? '' : 's'} · {successRate}% succeeded</span>
            )}
          </div>
        </div>

        <div className="flex shrink-0 gap-1">
          <button
            type="button"
            onClick={() => regenerateMutation.mutate()}
            disabled={regenerateMutation.isPending}
            aria-label="Regenerate webhook secret"
            title="Regenerate webhook secret"
            className="rounded p-1.5 text-muted-400 hover:bg-ink-800 hover:text-line-200 disabled:opacity-50"
          >
            {regenerateMutation.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RefreshCw className="h-3.5 w-3.5" />}
          </button>
          <button
            type="button"
            onClick={onEdit}
            aria-label="Edit"
            title="Edit"
            className="rounded p-1.5 text-muted-400 hover:bg-ink-800 hover:text-line-200"
          >
            <Settings2 className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={onDelete}
            disabled={isDeleting}
            aria-label="Delete"
            title="Delete"
            className="rounded p-1.5 text-muted-400 hover:bg-coral-500/10 hover:text-coral-500 disabled:opacity-40"
          >
            {isDeleting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}
          </button>
        </div>
      </div>
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Add action modal                                                           */
/* -------------------------------------------------------------------------- */

function generateSecret(): string {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  return Array.from(bytes).map((b) => b.toString(16).padStart(2, '0')).join('')
}

function AddActionModal({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const [actionType, setActionType] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [webhookUrl, setWebhookUrl] = useState('')
  const [webhookSecret, setWebhookSecret] = useState(generateSecret())
  const [requiresVerification, setRequiresVerification] = useState(false)
  const [isReadOnly, setIsReadOnly] = useState(false)
  const [parameterSchema, setParameterSchema] = useState('')
  const [timeoutSeconds, setTimeoutSeconds] = useState(10)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [createdSecret, setCreatedSecret] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: (body: CreateActionDefinitionRequest) => api.post('/api/actions', body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['actions'] })
      setCreatedSecret(webhookSecret)
    },
    onError: (err: any) => {
      const errors: string[] | undefined = err?.response?.data?.errors
      setError(errors?.length ? errors.join(' ') : err?.response?.data?.message ?? 'Something went wrong.')
    },
  })

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    mutation.mutate({
      actionType: actionType.trim().toLowerCase(),
      displayName: displayName.trim(),
      webhookUrl: webhookUrl.trim(),
      webhookSecret,
      requiresVerification,
      isReadOnly,
      parameterSchema: parameterSchema.trim() || null,
      timeoutSeconds,
    })
  }

  function copySecret() {
    navigator.clipboard.writeText(webhookSecret)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  if (createdSecret) {
    return (
      <ModalShell title="Action created" onClose={onClose}>
        <div className="space-y-4 px-5 py-4">
          <p className="text-sm text-line-200">
            Save this webhook secret now — it won't be shown again. Your backend needs it to verify the
            X-Webhook-Signature header on every call.
          </p>
          <div className="flex items-center gap-2 rounded-lg border border-ink-700 bg-ink-950 px-3 py-2">
            <code className="flex-1 truncate text-xs text-mint-300">{createdSecret}</code>
            <button type="button" onClick={copySecret} className="shrink-0 text-muted-400 hover:text-line-200">
              {copied ? <Check className="h-4 w-4 text-mint-300" /> : <Copy className="h-4 w-4" />}
            </button>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="w-full rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white hover:bg-teal-400"
          >
            Done
          </button>
        </div>
      </ModalShell>
    )
  }

  return (
    <ModalShell title="Add action" onClose={onClose}>
      <form onSubmit={handleSubmit} className="max-h-[70vh] space-y-4 overflow-y-auto px-5 py-4">
        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">
            Action type <span className="text-muted-400">(snake_case identifier)</span>
          </label>
          <input
            value={actionType}
            onChange={(e) => setActionType(e.target.value)}
            placeholder="cancel_order"
            pattern="[a-z][a-z0-9_]{2,99}"
            required
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Display name</label>
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="Cancel an order"
            required
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Webhook URL (https only)</label>
          <input
            type="url"
            value={webhookUrl}
            onChange={(e) => setWebhookUrl(e.target.value)}
            placeholder="https://api.yourcompany.com/actions/cancel-order"
            required
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Webhook secret</label>
          <div className="flex items-center gap-2">
            <input
              value={webhookSecret}
              onChange={(e) => setWebhookSecret(e.target.value)}
              className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-xs text-mint-300 focus:border-teal-400 focus:outline-none"
            />
            <button
              type="button"
              onClick={() => setWebhookSecret(generateSecret())}
              className="shrink-0 rounded-lg border border-ink-700 px-2.5 py-2 text-xs text-line-200 hover:bg-ink-800"
            >
              Regenerate
            </button>
          </div>
          <p className="mt-1 text-[11px] text-muted-400">
            Used to sign every webhook call with HMAC-SHA256 (X-Webhook-Signature header) so your backend can verify it's really us.
          </p>
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">
            Parameter schema <span className="text-muted-400">(optional — helps the AI extract the right fields)</span>
          </label>
          <textarea
            value={parameterSchema}
            onChange={(e) => setParameterSchema(e.target.value)}
            placeholder="order_id (required), reason (optional)"
            rows={2}
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Timeout (seconds)</label>
          <input
            type="number" min={1} max={30} value={timeoutSeconds}
            onChange={(e) => setTimeoutSeconds(Number(e.target.value))}
            className="w-32 rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <label className="flex items-center gap-2 text-xs text-line-200">
          <input
            type="checkbox"
            checked={requiresVerification}
            onChange={(e) => setRequiresVerification(e.target.checked)}
            className="rounded border-ink-700 bg-ink-800 text-teal-500 focus:ring-teal-400"
          />
          Require identity verification (OTP) before running this action
        </label>

        <label className="flex items-center gap-2 text-xs text-line-200">
          <input
            type="checkbox"
            checked={isReadOnly}
            onChange={(e) => setIsReadOnly(e.target.checked)}
            className="rounded border-ink-700 bg-ink-800 text-teal-500 focus:ring-teal-400"
          />
          Read-only (skips verification even if required above — for things like balance checks)
        </label>

        {error && (
          <p className="flex items-center gap-1.5 text-xs text-coral-500">
            <AlertCircle className="h-3.5 w-3.5" /> {error}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-ink-700 px-4 py-2 text-sm text-muted-400 hover:bg-ink-800 hover:text-line-200"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={mutation.isPending}
            className="flex items-center gap-2 rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white hover:bg-teal-400 disabled:opacity-60"
          >
            {mutation.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Create action
          </button>
        </div>
      </form>
    </ModalShell>
  )
}

/* -------------------------------------------------------------------------- */
/* Edit action modal                                                         */
/* -------------------------------------------------------------------------- */

function EditActionModal({ action, onClose }: { action: ActionDefinition; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [displayName, setDisplayName] = useState(action.displayName)
  const [webhookUrl, setWebhookUrl] = useState(action.webhookUrl)
  const [requiresVerification, setRequiresVerification] = useState(action.requiresVerification)
  const [isReadOnly, setIsReadOnly] = useState(action.isReadOnly)
  const [parameterSchema, setParameterSchema] = useState(action.parameterSchema ?? '')
  const [timeoutSeconds, setTimeoutSeconds] = useState(action.timeoutSeconds)
  const [isActive, setIsActive] = useState(action.isActive)
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: (body: UpdateActionDefinitionRequest) => api.put(`/api/actions/${action.id}`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['actions'] })
      onClose()
    },
    onError: (err: any) => setError(err?.response?.data?.message ?? 'Something went wrong.'),
  })

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    mutation.mutate({
      displayName: displayName.trim(),
      webhookUrl: webhookUrl.trim(),
      requiresVerification,
      isReadOnly,
      parameterSchema: parameterSchema.trim() || null,
      timeoutSeconds,
      isActive,
    })
  }

  return (
    <ModalShell title="Edit action" subtitle={action.actionType} onClose={onClose}>
      <form onSubmit={handleSubmit} className="max-h-[70vh] space-y-4 overflow-y-auto px-5 py-4">
        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Display name</label>
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            required
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Webhook URL</label>
          <input
            type="url"
            value={webhookUrl}
            onChange={(e) => setWebhookUrl(e.target.value)}
            required
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Parameter schema</label>
          <textarea
            value={parameterSchema}
            onChange={(e) => setParameterSchema(e.target.value)}
            rows={2}
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Timeout (seconds)</label>
          <input
            type="number" min={1} max={30} value={timeoutSeconds}
            onChange={(e) => setTimeoutSeconds(Number(e.target.value))}
            className="w-32 rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <label className="flex items-center gap-2 text-xs text-line-200">
          <input
            type="checkbox"
            checked={requiresVerification}
            onChange={(e) => setRequiresVerification(e.target.checked)}
            className="rounded border-ink-700 bg-ink-800 text-teal-500 focus:ring-teal-400"
          />
          Require identity verification (OTP)
        </label>

        <label className="flex items-center gap-2 text-xs text-line-200">
          <input
            type="checkbox"
            checked={isReadOnly}
            onChange={(e) => setIsReadOnly(e.target.checked)}
            className="rounded border-ink-700 bg-ink-800 text-teal-500 focus:ring-teal-400"
          />
          Read-only
        </label>

        <label className="flex items-center gap-2 text-xs text-line-200">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="rounded border-ink-700 bg-ink-800 text-teal-500 focus:ring-teal-400"
          />
          Active (uncheck to disable without losing history)
        </label>

        {error && (
          <p className="flex items-center gap-1.5 text-xs text-coral-500">
            <AlertCircle className="h-3.5 w-3.5" /> {error}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-ink-700 px-4 py-2 text-sm text-muted-400 hover:bg-ink-800 hover:text-line-200"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={mutation.isPending}
            className="flex items-center gap-2 rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white hover:bg-teal-400 disabled:opacity-60"
          >
            {mutation.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Save changes
          </button>
        </div>
      </form>
    </ModalShell>
  )
}

/* -------------------------------------------------------------------------- */
/* Secret reveal modal (after regenerate)                                     */
/* -------------------------------------------------------------------------- */

function SecretRevealModal({ secret, onClose }: { secret: string; onClose: () => void }) {
  const [copied, setCopied] = useState(false)

  function copy() {
    navigator.clipboard.writeText(secret)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  return (
    <ModalShell title="New webhook secret" onClose={onClose}>
      <div className="space-y-4 px-5 py-4">
        <p className="text-sm text-line-200">
          Save this now — it won't be shown again. Update your backend's verification key before the old
          secret stops working.
        </p>
        <div className="flex items-center gap-2 rounded-lg border border-ink-700 bg-ink-950 px-3 py-2">
          <code className="flex-1 truncate text-xs text-mint-300">{secret}</code>
          <button type="button" onClick={copy} className="shrink-0 text-muted-400 hover:text-line-200">
            {copied ? <Check className="h-4 w-4 text-mint-300" /> : <Copy className="h-4 w-4" />}
          </button>
        </div>
        <button
          type="button"
          onClick={onClose}
          className="w-full rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white hover:bg-teal-400"
        >
          Done
        </button>
      </div>
    </ModalShell>
  )
}

/* -------------------------------------------------------------------------- */
/* Audit log panel                                                            */
/* -------------------------------------------------------------------------- */

function AuditLogPanel() {
  const { data, isLoading } = useQuery({
    queryKey: ['actions', 'logs'],
    queryFn: async () => {
      const { data } = await api.get<ActionLogEntry[]>('/api/actions/logs', { params: { take: 50 } })
      return data
    },
  })

  return (
    <div className="mb-6 rounded-xl border border-ink-700 bg-ink-900 p-4">
      <h3 className="mb-3 text-sm font-semibold text-white">Audit log</h3>

      {isLoading && (
        <div className="flex items-center gap-2 text-sm text-muted-400">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading…
        </div>
      )}

      {data?.length === 0 && <p className="text-sm text-muted-400">No actions have run yet.</p>}

      {data && data.length > 0 && (
        <div className="max-h-80 space-y-2 overflow-y-auto">
          {data.map((log) => (
            <div key={log.id} className="flex items-start gap-2.5 rounded-lg border border-ink-700 bg-ink-800 px-3 py-2">
              {log.success ? (
                <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-mint-300" />
              ) : (
                <XCircle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-coral-500" />
              )}
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <code className="text-xs text-mint-300">{log.actionType}</code>
                  <span className="text-[11px] text-muted-400">{log.customerId}</span>
                  {log.identityVerified && <ShieldCheck className="h-3 w-3 text-teal-400" />}
                </div>
                {Object.keys(log.parameters).length > 0 && (
                  <p className="mt-0.5 truncate text-[11px] text-muted-400">
                    {Object.entries(log.parameters).map(([k, v]) => `${k}=${v}`).join(', ')}
                  </p>
                )}
                {!log.success && log.errorMessage && (
                  <p className="mt-0.5 truncate text-[11px] text-coral-500">{log.errorMessage}</p>
                )}
              </div>
              <span className="shrink-0 text-[11px] text-muted-400">{new Date(log.executedAt).toLocaleString()}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Shared modal shell                                                         */
/* -------------------------------------------------------------------------- */

function ModalShell({
  title,
  subtitle,
  onClose,
  children,
}: {
  title: string
  subtitle?: string
  onClose: () => void
  children: ReactNode
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 px-4" role="presentation">
      <button type="button" aria-label="Close" onClick={onClose} className="absolute inset-0 cursor-default" tabIndex={-1} />
      <div role="dialog" aria-modal="true" className="relative w-full max-w-lg rounded-xl border border-ink-700 bg-ink-900 shadow-xl">
        <div className="flex items-center justify-between border-b border-ink-700 px-5 py-4">
          <div className="min-w-0">
            <h2 className="text-sm font-semibold text-white">{title}</h2>
            {subtitle && <p className="mt-0.5 truncate text-xs text-muted-400"><code>{subtitle}</code></p>}
          </div>
          <button type="button" onClick={onClose} aria-label="Close" className="shrink-0 rounded p-1 text-muted-400 hover:bg-ink-800">
            <X className="h-4 w-4" />
          </button>
        </div>
        {children}
      </div>
    </div>
  )
}

import { useState, type FormEvent } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2, Pencil, X, BookOpen, AlertCircle, Loader2 } from 'lucide-react'
import { api } from '@/lib/api'
import type { KnowledgeChunk, UpsertKnowledgeRequest } from '@/lib/types'

/* -------------------------------------------------------------------------- */
/* Page                                                                        */
/* -------------------------------------------------------------------------- */

export function KnowledgeBasePage() {
  const queryClient = useQueryClient()
  const [modal, setModal] = useState<'add' | { edit: KnowledgeChunk } | null>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['knowledge'],
    queryFn: async () => {
      const { data } = await api.get<KnowledgeChunk[]>('/api/knowledge')
      return data
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/knowledge/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['knowledge'] }),
  })

  return (
    <div>
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-white">Knowledge Base</h1>
          <p className="mt-1 text-sm text-muted-400">
            Everything here is searchable by the AI when it answers customer questions.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setModal('add')}
          className="flex items-center gap-2 rounded-lg bg-teal-500 px-3.5 py-2 text-sm font-medium text-white hover:bg-teal-400"
        >
          <Plus className="h-4 w-4" />
          Add entry
        </button>
      </header>

      {isLoading && (
        <div className="flex items-center gap-2 text-sm text-muted-400">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading knowledge base…
        </div>
      )}

      {isError && (
        <div className="flex items-center gap-2 rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          <AlertCircle className="h-4 w-4 shrink-0" />
          Couldn't load knowledge base. Is the backend running?
        </div>
      )}

      {data?.length === 0 && (
        <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-ink-700 py-16 text-center">
          <BookOpen className="mb-3 h-8 w-8 text-muted-400/40" />
          <p className="text-sm font-medium text-line-200">No entries yet</p>
          <p className="mt-1 text-xs text-muted-400">
            Add your first entry and the AI will start answering from it immediately.
          </p>
          <button
            type="button"
            onClick={() => setModal('add')}
            className="mt-4 flex items-center gap-1.5 rounded-lg bg-teal-500/10 px-3 py-1.5 text-sm font-medium text-teal-400 hover:bg-teal-500/20"
          >
            <Plus className="h-3.5 w-3.5" /> Add your first entry
          </button>
        </div>
      )}

      {data && data.length > 0 && (
        <div className="space-y-3">
          {data.map((chunk) => (
            <ChunkCard
              key={chunk.id}
              chunk={chunk}
              onEdit={() => setModal({ edit: chunk })}
              onDelete={() => {
                if (confirm(`Delete "${chunk.documentName}"? This cannot be undone.`))
                  deleteMutation.mutate(chunk.id)
              }}
              isDeleting={deleteMutation.isPending && deleteMutation.variables === chunk.id}
            />
          ))}
        </div>
      )}

      {modal === 'add' && (
        <UpsertModal
          mode="add"
          onClose={() => setModal(null)}
        />
      )}

      {modal !== null && modal !== 'add' && 'edit' in modal && (
        <UpsertModal
          mode="edit"
          initial={modal.edit}
          onClose={() => setModal(null)}
        />
      )}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Chunk card                                                                  */
/* -------------------------------------------------------------------------- */

function ChunkCard({
  chunk,
  onEdit,
  onDelete,
  isDeleting,
}: {
  chunk: KnowledgeChunk
  onEdit: () => void
  onDelete: () => void
  isDeleting: boolean
}) {
  const [expanded, setExpanded] = useState(false)
  const isLong = chunk.fullText.length > 200

  return (
    <div className="rounded-xl border border-ink-700 bg-ink-900 p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold text-white">{chunk.documentName}</p>
          <p className="mt-1 text-xs text-muted-400">{new Date(chunk.createdAt).toLocaleString()}</p>
        </div>
        <div className="flex shrink-0 gap-1">
          <button
            type="button"
            onClick={onEdit}
            aria-label="Edit"
            className="rounded p-1.5 text-muted-400 hover:bg-ink-800 hover:text-line-200"
          >
            <Pencil className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={onDelete}
            disabled={isDeleting}
            aria-label="Delete"
            className="rounded p-1.5 text-muted-400 hover:bg-coral-500/10 hover:text-coral-500 disabled:opacity-40"
          >
            {isDeleting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}
          </button>
        </div>
      </div>

      <p className="mt-3 whitespace-pre-wrap text-sm leading-relaxed text-line-200">
        {expanded ? chunk.fullText : chunk.textPreview}
      </p>

      {isLong && (
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="mt-2 text-xs text-teal-400 hover:text-teal-300"
        >
          {expanded ? 'Show less' : 'Show full text'}
        </button>
      )}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Add / Edit modal                                                            */
/* -------------------------------------------------------------------------- */

function UpsertModal({
  mode,
  initial,
  onClose,
}: {
  mode: 'add' | 'edit'
  initial?: KnowledgeChunk
  onClose: () => void
}) {
  const queryClient = useQueryClient()
  const [documentName, setDocumentName] = useState(initial?.documentName ?? '')
  const [text, setText] = useState(initial?.fullText ?? '')
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: async (body: UpsertKnowledgeRequest) => {
      if (mode === 'add') {
        await api.post('/api/knowledge', body)
      } else {
        await api.put(`/api/knowledge/${initial!.id}`, body)
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['knowledge'] })
      onClose()
    },
    onError: (err: any) => {
      const msg = err?.response?.data?.message ?? 'Something went wrong.'
      setError(msg)
    },
  })

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!documentName.trim()) { setError('Title is required.'); return }
    if (!text.trim())         { setError('Content is required.'); return }
    mutation.mutate({ documentName: documentName.trim(), text: text.trim() })
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 px-4"
      role="presentation"
    >
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
        className="relative w-full max-w-lg rounded-xl border border-ink-700 bg-ink-900 shadow-xl"
      >
        <div className="flex items-center justify-between border-b border-ink-700 px-5 py-4">
          <h2 className="text-sm font-semibold text-white">
            {mode === 'add' ? 'Add knowledge entry' : 'Edit knowledge entry'}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded p-1 text-muted-400 hover:bg-ink-800"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4 px-5 py-4">
          <p className="text-xs text-muted-400">
            {mode === 'add'
              ? 'Content is embedded immediately with OpenAI text-embedding-3-small and becomes searchable at once.'
              : 'If you change the content, the embedding is regenerated automatically.'}
          </p>

          <div>
            <label className="mb-1.5 block text-xs font-medium text-line-200">
              Title / document name
            </label>
            <input
              value={documentName}
              onChange={(e) => setDocumentName(e.target.value)}
              placeholder="e.g. Refund Policy, Getting Started Guide, Pricing FAQ"
              className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
            />
          </div>

          <div>
            <label className="mb-1.5 block text-xs font-medium text-line-200">
              Content
            </label>
            <textarea
              value={text}
              onChange={(e) => setText(e.target.value)}
              rows={10}
              placeholder="Paste the text you want the AI to answer from. Plain text only in Sprint 4 — PDF upload comes in Sprint 6."
              className="w-full resize-y rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm leading-relaxed text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
            />
          </div>

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
              {mode === 'add' ? 'Save & embed' : 'Update'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

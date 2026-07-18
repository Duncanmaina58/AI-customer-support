import { useState, useEffect, type FormEvent, type ReactNode } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Plus, Trash2, X, Globe, AlertCircle, Loader2, RefreshCw, Pause, Play,
  Settings2, History, CheckCircle2, Clock, AlertTriangle,
} from 'lucide-react'
import clsx from 'clsx'
import { api } from '@/lib/api'
import type {
  WebSource, WebSourceStatusUpdate, WebPageChange,
  CreateWebSourceRequest, UpdateMonitoringRequest,
} from '@/lib/types'

/* -------------------------------------------------------------------------- */
/* Tab                                                                         */
/* -------------------------------------------------------------------------- */

export function WebSourcesTab() {
  const queryClient = useQueryClient()
  const [modal, setModal] = useState<'add' | { monitoring: WebSource } | { changes: WebSource } | null>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['web-sources'],
    queryFn: async () => {
      const { data } = await api.get<WebSource[]>('/api/knowledge/web-sources')
      return data
    },
    // Keep the list itself gently refreshing too, so a source that finishes
    // crawling in another tab/session updates without a manual reload.
    refetchInterval: 15_000,
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/knowledge/web-sources/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['web-sources'] }),
  })

  return (
    <div>
      <div className="mb-4 flex items-start justify-between gap-3">
        <p className="max-w-xl text-xs text-muted-400">
          Connect your website and the AI will crawl it, stay up to date automatically, and
          answer from whatever's currently published — no manual copy-pasting.
        </p>
        <button
          type="button"
          onClick={() => setModal('add')}
          className="flex shrink-0 items-center gap-2 rounded-lg bg-teal-500 px-3.5 py-2 text-sm font-medium text-white hover:bg-teal-400"
        >
          <Plus className="h-4 w-4" />
          Add website
        </button>
      </div>

      {isLoading && (
        <div className="flex items-center gap-2 text-sm text-muted-400">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading web sources…
        </div>
      )}

      {isError && (
        <div className="flex items-center gap-2 rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
          <AlertCircle className="h-4 w-4 shrink-0" />
          Couldn't load web sources. Is the backend running?
        </div>
      )}

      {data?.length === 0 && (
        <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-ink-700 py-16 text-center">
          <Globe className="mb-3 h-8 w-8 text-muted-400/40" />
          <p className="text-sm font-medium text-line-200">No websites connected yet</p>
          <p className="mt-1 max-w-sm text-xs text-muted-400">
            Add your website and the AI will crawl it, index every page, and keep itself
            current automatically as your site changes.
          </p>
          <button
            type="button"
            onClick={() => setModal('add')}
            className="mt-4 flex items-center gap-1.5 rounded-lg bg-teal-500/10 px-3 py-1.5 text-sm font-medium text-teal-400 hover:bg-teal-500/20"
          >
            <Plus className="h-3.5 w-3.5" /> Add your first website
          </button>
        </div>
      )}

      {data && data.length > 0 && (
        <div className="space-y-3">
          {data.map((source) => (
            <SourceCard
              key={source.id}
              source={source}
              onDelete={() => {
                if (confirm(`Remove ${source.url}? Every indexed page from this site will be deleted.`))
                  deleteMutation.mutate(source.id)
              }}
              onOpenMonitoring={() => setModal({ monitoring: source })}
              onOpenChanges={() => setModal({ changes: source })}
              isDeleting={deleteMutation.isPending && deleteMutation.variables === source.id}
            />
          ))}
        </div>
      )}

      {modal === 'add' && <AddSourceModal onClose={() => setModal(null)} />}

      {modal !== null && modal !== 'add' && 'monitoring' in modal && (
        <MonitoringModal source={modal.monitoring} onClose={() => setModal(null)} />
      )}

      {modal !== null && modal !== 'add' && 'changes' in modal && (
        <ChangesModal source={modal.changes} onClose={() => setModal(null)} />
      )}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Source card                                                                 */
/* -------------------------------------------------------------------------- */

const STATUS_STYLES: Record<WebSource['status'], string> = {
  Pending:  'bg-muted-400/10 text-muted-400',
  Crawling: 'bg-teal-500/10 text-teal-400',
  Indexed:  'bg-green-500/10 text-mint-300',
  Error:    'bg-coral-500/10 text-coral-500',
  Paused:   'bg-amber-500/10 text-amber-500',
}

function SourceCard({
  source,
  onDelete,
  onOpenMonitoring,
  onOpenChanges,
  isDeleting,
}: {
  source: WebSource
  onDelete: () => void
  onOpenMonitoring: () => void
  onOpenChanges: () => void
  isDeleting: boolean
}) {
  const queryClient = useQueryClient()
  const isCrawling = source.status === 'Crawling'

  // Live progress: only poll .../status while a crawl is actually running.
  const { data: liveStatus } = useQuery({
    queryKey: ['web-sources', source.id, 'status'],
    queryFn: async () => {
      const { data } = await api.get<WebSourceStatusUpdate>(`/api/knowledge/web-sources/${source.id}/status`)
      return data
    },
    enabled: isCrawling,
    refetchInterval: isCrawling ? 2_000 : false,
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['web-sources'] })
    queryClient.invalidateQueries({ queryKey: ['web-sources', source.id, 'status'] })
  }

  const recrawlMutation = useMutation({
    mutationFn: () => api.post(`/api/knowledge/web-sources/${source.id}/recrawl`),
    onSuccess: invalidate,
  })
  const checkNowMutation = useMutation({
    mutationFn: () => api.post(`/api/knowledge/web-sources/${source.id}/check-now`),
    onSuccess: invalidate,
  })
  const pauseMutation = useMutation({
    mutationFn: () => api.post(`/api/knowledge/web-sources/${source.id}/pause`),
    onSuccess: invalidate,
  })
  const resumeMutation = useMutation({
    mutationFn: () => api.post(`/api/knowledge/web-sources/${source.id}/resume`),
    onSuccess: invalidate,
  })

  const cancelMutation = useMutation({
    mutationFn: () => api.post(`/api/knowledge/web-sources/${source.id}/cancel`),
    onSuccess: invalidate,
  })

  // When Status flips away from Crawling (finished), the list query's own
  // 15s refetch will pick it up — but nudge it immediately for snappier UX.
  useEffect(() => {
    if (liveStatus && liveStatus.status !== 'Crawling' && isCrawling) invalidate()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [liveStatus?.status])

  const progressPct = liveStatus?.estimatedTotalPages
    ? Math.min(100, Math.round((liveStatus.pagesCrawled / liveStatus.estimatedTotalPages) * 100))
    : null

  return (
    <div className="rounded-xl border border-ink-700 bg-ink-900 p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <Globe className="h-3.5 w-3.5 shrink-0 text-muted-400" />
            <p className="truncate text-sm font-semibold text-white">{source.url}</p>
            <span className={clsx('shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium', STATUS_STYLES[source.status])}>
              {source.status}
            </span>
          </div>
          <p className="mt-1 text-xs text-muted-400">
            {source.crawlMode === 'FullSite' && `Full site · depth ${source.crawlDepth} · up to ${source.maxPages} pages`}
            {source.crawlMode === 'SinglePage' && 'Single page'}
            {source.crawlMode === 'Sitemap' && `Sitemap · up to ${source.maxPages} pages`}
            {' · '}
            {source.monitoringMode === 'Adaptive' && 'Adaptive monitoring'}
            {source.monitoringMode === 'Fixed' && `Checked every ${source.fixedIntervalHours}h`}
            {source.monitoringMode === 'Manual' && 'Manual checks only'}
          </p>
        </div>

        <div className="flex shrink-0 gap-1">
          <button
            type="button"
            onClick={onOpenChanges}
            aria-label="Change log"
            title="Change log"
            className="rounded p-1.5 text-muted-400 hover:bg-ink-800 hover:text-line-200"
          >
            <History className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={onOpenMonitoring}
            aria-label="Monitoring settings"
            title="Monitoring settings"
            className="rounded p-1.5 text-muted-400 hover:bg-ink-800 hover:text-line-200"
          >
            <Settings2 className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={onDelete}
            disabled={isDeleting}
            aria-label="Remove"
            title="Remove"
            className="rounded p-1.5 text-muted-400 hover:bg-coral-500/10 hover:text-coral-500 disabled:opacity-40"
          >
            {isDeleting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}
          </button>
        </div>
      </div>

      {isCrawling && liveStatus && (
        <div className="mt-3 rounded-lg bg-ink-800 p-3">
          <div className="flex items-center justify-between text-xs text-line-200">
            <span className="flex items-center gap-1.5">
              <Loader2 className="h-3 w-3 animate-spin text-teal-400" />
              Crawling — {liveStatus.pagesCrawled} page{liveStatus.pagesCrawled === 1 ? '' : 's'} indexed
              {liveStatus.estimatedTotalPages ? ` of ~${liveStatus.estimatedTotalPages}` : ''}
            </span>
            <div className="flex items-center gap-2">
              {progressPct !== null && <span className="text-muted-400">{progressPct}%</span>}
              <button
                type="button"
                onClick={() => cancelMutation.mutate()}
                disabled={cancelMutation.isPending}
                className="rounded px-1.5 py-0.5 text-[11px] font-medium text-coral-500 hover:bg-coral-500/10 disabled:opacity-50"
              >
                Cancel
              </button>
            </div>
          </div>
          {progressPct !== null && (
            <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-ink-700">
              <div className="h-full rounded-full bg-teal-500 transition-all" style={{ width: `${progressPct}%` }} />
            </div>
          )}
          {liveStatus.currentCrawlUrl && (
            <p className="mt-1.5 truncate text-[11px] text-muted-400">{liveStatus.currentCrawlUrl}</p>
          )}
        </div>
      )}

      {source.status === 'Error' && source.errorMessage && (
        <div className="mt-3 flex items-start gap-2 rounded-lg border border-coral-500/30 bg-coral-500/5 p-3 text-xs text-coral-500">
          <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          {source.errorMessage}
        </div>
      )}

      {source.hasJsRenderedPagesWarning && (
        <div className="mt-3 flex items-start gap-2 rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-amber-500">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          Some pages returned very little text — this site may render its content with
          JavaScript, which isn't indexed yet. Consider adding the affected pages as manual
          entries instead.
        </div>
      )}

      {source.status !== 'Pending' && source.status !== 'Crawling' && (
        <div className="mt-3 flex flex-wrap items-center gap-4 text-xs text-muted-400">
          <span className="flex items-center gap-1.5">
            <CheckCircle2 className="h-3.5 w-3.5 text-mint-300" />
            {source.pagesCrawled} page{source.pagesCrawled === 1 ? '' : 's'} · {source.chunksCreated} chunks
          </span>
          {source.lastCrawledAt && (
            <span className="flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5" />
              Last crawled {new Date(source.lastCrawledAt).toLocaleString()}
            </span>
          )}
          {source.maxPagesReached && (
            <span className="text-amber-500">
              Page limit reached — add specific section URLs (e.g. /products, /faq) as separate web sources to index more
            </span>
          )}
        </div>
      )}

      {source.status !== 'Pending' && source.status !== 'Crawling' && (
        <div className="mt-3 flex flex-wrap gap-2">
          <ActionButton
            icon={RefreshCw}
            label="Re-crawl Now"
            onClick={() => recrawlMutation.mutate()}
            pending={recrawlMutation.isPending}
          />
          {source.status !== 'Paused' && (
            <ActionButton
              icon={Clock}
              label="Check Now"
              onClick={() => checkNowMutation.mutate()}
              pending={checkNowMutation.isPending}
            />
          )}
          {source.status === 'Paused' ? (
            <ActionButton
              icon={Play}
              label="Resume monitoring"
              onClick={() => resumeMutation.mutate()}
              pending={resumeMutation.isPending}
            />
          ) : (
            <ActionButton
              icon={Pause}
              label="Pause monitoring"
              onClick={() => pauseMutation.mutate()}
              pending={pauseMutation.isPending}
            />
          )}
        </div>
      )}
    </div>
  )
}

function ActionButton({
  icon: Icon,
  label,
  onClick,
  pending,
}: {
  icon: typeof RefreshCw
  label: string
  onClick: () => void
  pending: boolean
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={pending}
      className="flex items-center gap-1.5 rounded-lg border border-ink-700 px-2.5 py-1.5 text-xs font-medium text-line-200 hover:bg-ink-800 disabled:opacity-50"
    >
      {pending ? <Loader2 className="h-3 w-3 animate-spin" /> : <Icon className="h-3 w-3" />}
      {label}
    </button>
  )
}

/* -------------------------------------------------------------------------- */
/* Add website modal                                                          */
/* -------------------------------------------------------------------------- */

function AddSourceModal({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const [url, setUrl] = useState('')
  const [crawlMode, setCrawlMode] = useState<CreateWebSourceRequest['crawlMode']>('full_site')
  const [crawlDepth, setCrawlDepth] = useState(3)
  const [maxPages, setMaxPages] = useState(200)
  const [includePattern, setIncludePattern] = useState('')
  const [excludePattern, setExcludePattern] = useState('')
  const [monitoringMode, setMonitoringMode] = useState<CreateWebSourceRequest['monitoringMode']>('adaptive')
  const [fixedIntervalHours, setFixedIntervalHours] = useState(24)
  const [notifyOnChange, setNotifyOnChange] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: (body: CreateWebSourceRequest) => api.post('/api/knowledge/web-sources', body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['web-sources'] })
      onClose()
    },
    onError: (err: any) => {
      setError(err?.response?.data?.message ?? 'Something went wrong. Check the URL and try again.')
    },
  })

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!url.trim()) { setError('A website URL is required.'); return }

    mutation.mutate({
      url: url.trim(),
      crawlMode,
      crawlDepth,
      maxPages,
      includePattern: includePattern.trim() || null,
      excludePattern: excludePattern.trim() || null,
      monitoringMode,
      fixedIntervalHours: monitoringMode === 'fixed' ? fixedIntervalHours : null,
      notifyOnChange,
    })
  }

  return (
    <ModalShell title="Add website" onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-4 px-5 py-4">
        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Website URL</label>
          <input
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            placeholder="https://yourcompany.co.ke"
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
          />
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Crawl mode</label>
          <select
            value={crawlMode}
            onChange={(e) => setCrawlMode(e.target.value as CreateWebSourceRequest['crawlMode'])}
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          >
            <option value="full_site">Full site — follow links automatically</option>
            <option value="sitemap">Sitemap only — fastest, uses /sitemap.xml</option>
            <option value="single_page">Single page — just this URL</option>
          </select>
        </div>

        {crawlMode === 'full_site' && (
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1.5 block text-xs font-medium text-line-200">Crawl depth</label>
              <input
                type="number" min={1} max={5} value={crawlDepth}
                onChange={(e) => setCrawlDepth(Number(e.target.value))}
                className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
              />
            </div>
            <div>
              <label className="mb-1.5 block text-xs font-medium text-line-200">Max pages</label>
              <input
                type="number" min={1} max={1000} value={maxPages}
                onChange={(e) => setMaxPages(Number(e.target.value))}
                className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
              />
            </div>
          </div>
        )}

        {crawlMode !== 'single_page' && (
          <>
            <div>
              <label className="mb-1.5 block text-xs font-medium text-line-200">
                Include pattern <span className="text-muted-400">(optional regex)</span>
              </label>
              <input
                value={includePattern}
                onChange={(e) => setIncludePattern(e.target.value)}
                placeholder="e.g. /docs/|/help/"
                className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
              />
            </div>
            <div>
              <label className="mb-1.5 block text-xs font-medium text-line-200">
                Exclude pattern <span className="text-muted-400">(optional regex)</span>
              </label>
              <input
                value={excludePattern}
                onChange={(e) => setExcludePattern(e.target.value)}
                placeholder="e.g. /blog/|/careers/"
                className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none"
              />
            </div>
          </>
        )}

        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Keep it up to date</label>
          <select
            value={monitoringMode}
            onChange={(e) => setMonitoringMode(e.target.value as CreateWebSourceRequest['monitoringMode'])}
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          >
            <option value="adaptive">Adaptive (recommended) — checks more often if pages change a lot</option>
            <option value="fixed">Fixed interval</option>
            <option value="manual">Manual only — I'll click "Check Now" myself</option>
          </select>
        </div>

        {monitoringMode === 'fixed' && (
          <div>
            <label className="mb-1.5 block text-xs font-medium text-line-200">Check every (hours)</label>
            <input
              type="number" min={1} value={fixedIntervalHours}
              onChange={(e) => setFixedIntervalHours(Number(e.target.value))}
              className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
            />
          </div>
        )}

        <label className="flex items-center gap-2 text-xs text-line-200">
          <input
            type="checkbox"
            checked={notifyOnChange}
            onChange={(e) => setNotifyOnChange(e.target.checked)}
            className="rounded border-ink-700 bg-ink-800 text-teal-500 focus:ring-teal-400"
          />
          Email me when pages change
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
            Start crawling
          </button>
        </div>
      </form>
    </ModalShell>
  )
}

/* -------------------------------------------------------------------------- */
/* Monitoring settings modal                                                  */
/* -------------------------------------------------------------------------- */

function MonitoringModal({ source, onClose }: { source: WebSource; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [monitoringMode, setMonitoringMode] = useState(source.monitoringMode.toLowerCase() as UpdateMonitoringRequest['monitoringMode'])
  const [fixedIntervalHours, setFixedIntervalHours] = useState(source.fixedIntervalHours ?? 24)
  const [notifyOnChange, setNotifyOnChange] = useState(source.notifyOnChange)
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: (body: UpdateMonitoringRequest) =>
      api.patch(`/api/knowledge/web-sources/${source.id}/monitoring`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['web-sources'] })
      onClose()
    },
    onError: (err: any) => setError(err?.response?.data?.message ?? 'Something went wrong.'),
  })

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    mutation.mutate({
      monitoringMode,
      fixedIntervalHours: monitoringMode === 'fixed' ? fixedIntervalHours : null,
      notifyOnChange,
    })
  }

  return (
    <ModalShell title="Monitoring settings" subtitle={source.url} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-4 px-5 py-4">
        <div>
          <label className="mb-1.5 block text-xs font-medium text-line-200">Keep it up to date</label>
          <select
            value={monitoringMode}
            onChange={(e) => setMonitoringMode(e.target.value as UpdateMonitoringRequest['monitoringMode'])}
            className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          >
            <option value="adaptive">Adaptive (recommended)</option>
            <option value="fixed">Fixed interval</option>
            <option value="manual">Manual only</option>
          </select>
        </div>

        {monitoringMode === 'fixed' && (
          <div>
            <label className="mb-1.5 block text-xs font-medium text-line-200">Check every (hours)</label>
            <input
              type="number" min={1} value={fixedIntervalHours}
              onChange={(e) => setFixedIntervalHours(Number(e.target.value))}
              className="w-full rounded-lg border border-ink-700 bg-ink-800 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
            />
          </div>
        )}

        <label className="flex items-center gap-2 text-xs text-line-200">
          <input
            type="checkbox"
            checked={notifyOnChange}
            onChange={(e) => setNotifyOnChange(e.target.checked)}
            className="rounded border-ink-700 bg-ink-800 text-teal-500 focus:ring-teal-400"
          />
          Email me when pages change
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
            Save
          </button>
        </div>
      </form>
    </ModalShell>
  )
}

/* -------------------------------------------------------------------------- */
/* Change log modal                                                           */
/* -------------------------------------------------------------------------- */

function ChangesModal({ source, onClose }: { source: WebSource; onClose: () => void }) {
  const { data, isLoading } = useQuery({
    queryKey: ['web-sources', source.id, 'changes'],
    queryFn: async () => {
      const { data } = await api.get<WebPageChange[]>(`/api/knowledge/web-sources/${source.id}/changes`, {
        params: { days: 30 },
      })
      return data
    },
  })

  return (
    <ModalShell title="Change log" subtitle={`${source.url} · last 30 days`} onClose={onClose}>
      <div className="max-h-96 overflow-y-auto px-5 py-4">
        {isLoading && (
          <div className="flex items-center gap-2 text-sm text-muted-400">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading…
          </div>
        )}

        {data?.length === 0 && (
          <p className="py-6 text-center text-sm text-muted-400">No changes detected in the last 30 days.</p>
        )}

        {data && data.length > 0 && (
          <ul className="space-y-2">
            {data.map((change, i) => (
              <li key={`${change.url}-${i}`} className="flex items-start gap-2.5 rounded-lg border border-ink-700 bg-ink-800 px-3 py-2">
                <span
                  className={clsx(
                    'mt-0.5 shrink-0 rounded-full px-2 py-0.5 text-[10px] font-medium uppercase',
                    change.changeType === 'removed' ? 'bg-coral-500/10 text-coral-500' : 'bg-teal-500/10 text-teal-400',
                  )}
                >
                  {change.changeType}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-xs text-line-200">{change.url}</p>
                  <p className="mt-0.5 text-[11px] text-muted-400">{new Date(change.detectedAt).toLocaleString()}</p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </ModalShell>
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
            {subtitle && <p className="mt-0.5 truncate text-xs text-muted-400">{subtitle}</p>}
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

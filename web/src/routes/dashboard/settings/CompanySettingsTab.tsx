import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import { CopyableSecret } from '@/components/CopyableSecret'
import { Badge } from '@/components/Badge'
import type { CompanyDetails } from '@/lib/types'

const TIMEZONES = ['Africa/Nairobi', 'Africa/Kampala', 'Africa/Dar_es_Salaam', 'Africa/Kigali', 'UTC']
const CURRENCIES = ['KES', 'UGX', 'TZS', 'RWF', 'USD']

export function CompanySettingsTab() {
  const { agent } = useAuth()
  const canEdit = agent?.role === 'Owner' || agent?.role === 'Admin'
  const queryClient = useQueryClient()

  const { data: company, isLoading } = useQuery({
    queryKey: ['company'],
    queryFn: async () => {
      const { data } = await api.get<CompanyDetails>('/api/companies/me')
      return data
    },
  })

  const [name, setName] = useState('')
  const [timeZone, setTimeZone] = useState('')
  const [defaultCurrency, setDefaultCurrency] = useState('')
  const [justSaved, setJustSaved] = useState(false)

  // Seed the editable form once the company loads. react-query's structural
  // sharing means `company` only gets a new object reference when the server
  // data actually changes, so this won't clobber in-progress edits on an
  // unrelated background refetch.
  useEffect(() => {
    if (company) {
      setName(company.name)
      setTimeZone(company.timeZone)
      setDefaultCurrency(company.defaultCurrency)
    }
  }, [company])

  const updateMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.patch<CompanyDetails>('/api/companies/me', { name, timeZone, defaultCurrency })
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['company'], data)
      setJustSaved(true)
      setTimeout(() => setJustSaved(false), 2000)
    },
  })

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    updateMutation.mutate()
  }

  if (isLoading) {
    return <p className="text-sm text-muted-400">Loading company details…</p>
  }

  if (!company) {
    return (
      <div className="rounded-xl border border-coral-500/40 bg-coral-500/10 p-4 text-sm text-coral-500">
        Couldn't load company details.
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <form onSubmit={handleSubmit} className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-5">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-semibold text-white">Company details</h3>
          <Badge tone="purple">{company.plan} plan</Badge>
        </div>

        <div>
          <label htmlFor="companyName" className="mb-1.5 block text-sm text-line-200">
            Company name
          </label>
          <input
            id="companyName"
            value={name}
            disabled={!canEdit}
            onChange={(e) => setName(e.target.value)}
            className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 disabled:opacity-60 focus:border-teal-400"
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label htmlFor="timeZone" className="mb-1.5 block text-sm text-line-200">
              Time zone
            </label>
            <select
              id="timeZone"
              value={timeZone}
              disabled={!canEdit}
              onChange={(e) => setTimeZone(e.target.value)}
              className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 disabled:opacity-60 focus:border-teal-400"
            >
              {TIMEZONES.map((tz) => (
                <option key={tz} value={tz}>
                  {tz}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label htmlFor="currency" className="mb-1.5 block text-sm text-line-200">
              Default currency
            </label>
            <select
              id="currency"
              value={defaultCurrency}
              disabled={!canEdit}
              onChange={(e) => setDefaultCurrency(e.target.value)}
              className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 disabled:opacity-60 focus:border-teal-400"
            >
              {CURRENCIES.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>
        </div>

        {canEdit && (
          <div className="flex items-center gap-3">
            <button
              type="submit"
              disabled={updateMutation.isPending}
              className="rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
            >
              {updateMutation.isPending ? 'Saving…' : 'Save changes'}
            </button>
            {justSaved && (
              <span className="flex items-center gap-1 text-sm text-green-500">
                <Check className="h-4 w-4" /> Saved
              </span>
            )}
            {updateMutation.isError && (
              <span className="text-sm text-coral-500">Couldn't save — try again.</span>
            )}
          </div>
        )}
      </form>

      <div className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-5">
        <h3 className="text-sm font-semibold text-white">API access</h3>
        <CopyableSecret label="Public key (safe for the web chat widget)" value={company.publicApiKey} />
        <p className="text-xs text-muted-400">
          Your secret key was shown once when this company was created and can't be displayed again.
          Regenerating secret keys is coming in a later sprint.
        </p>
      </div>
    </div>
  )
}

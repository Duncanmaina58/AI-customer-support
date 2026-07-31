import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'
import type { CompanyDetails } from '@/lib/types'

const TIMEZONES = ['Africa/Nairobi', 'Africa/Kampala', 'Africa/Dar_es_Salaam', 'Africa/Kigali', 'UTC']
const INDUSTRY_SUGGESTIONS = ['Healthcare', 'Retail', 'Hospitality', 'Financial services', 'Education', 'Logistics', 'Real estate']

export function Step1Details({ company, onNext }: { company: CompanyDetails; onNext: () => void }) {
  const queryClient = useQueryClient()

  const [name, setName] = useState(company.name)
  const [industry, setIndustry] = useState(company.industry ?? '')
  const [logoUrl, setLogoUrl] = useState(company.logoUrl ?? '')
  const [timeZone, setTimeZone] = useState(company.timeZone)
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.patch<CompanyDetails>('/api/companies/me', {
        name,
        industry,
        logoUrl: logoUrl || null,
        timeZone,
      })
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['company'], data)
      onNext()
    },
    onError: () => setError("Couldn't save those details — try again."),
  })

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    mutation.mutate()
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {error && (
        <div role="alert" className="rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
          {error}
        </div>
      )}

      <div>
        <label htmlFor="name" className="mb-1.5 block text-sm text-line-200">
          Company name
        </label>
        <input
          id="name"
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
          className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 focus:border-teal-400"
        />
      </div>

      <div>
        <label htmlFor="industry" className="mb-1.5 block text-sm text-line-200">
          Industry
        </label>
        <input
          id="industry"
          list="industry-suggestions"
          value={industry}
          onChange={(e) => setIndustry(e.target.value)}
          placeholder="e.g. Healthcare"
          className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
        />
        <datalist id="industry-suggestions">
          {INDUSTRY_SUGGESTIONS.map((i) => (
            <option key={i} value={i} />
          ))}
        </datalist>
      </div>

      <div>
        <label htmlFor="logoUrl" className="mb-1.5 block text-sm text-line-200">
          Logo URL <span className="text-muted-400">(optional)</span>
        </label>
        <input
          id="logoUrl"
          type="url"
          value={logoUrl}
          onChange={(e) => setLogoUrl(e.target.value)}
          placeholder="https://yourcompany.com/logo.png"
          className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
        />
        <p className="mt-1 text-xs text-muted-400">
          Direct file upload isn't available yet — paste a link to an image you've already hosted somewhere.
        </p>
      </div>

      <div>
        <label htmlFor="timeZone" className="mb-1.5 block text-sm text-line-200">
          Time zone
        </label>
        <select
          id="timeZone"
          value={timeZone}
          onChange={(e) => setTimeZone(e.target.value)}
          className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 focus:border-teal-400"
        >
          {TIMEZONES.map((tz) => (
            <option key={tz} value={tz}>
              {tz}
            </option>
          ))}
        </select>
      </div>

      <div className="flex justify-end pt-2">
        <button
          type="submit"
          disabled={mutation.isPending}
          className="rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
        >
          {mutation.isPending ? 'Saving…' : 'Continue'}
        </button>
      </div>
    </form>
  )
}

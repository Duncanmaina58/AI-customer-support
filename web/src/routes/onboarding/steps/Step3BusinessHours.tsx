import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { api } from '@/lib/api'
import type { BusinessHours, CompanyDetails, DayHours } from '@/lib/types'

const DAYS: { key: keyof Omit<BusinessHours, 'closedDays'>; label: string }[] = [
  { key: 'mon', label: 'Monday' },
  { key: 'tue', label: 'Tuesday' },
  { key: 'wed', label: 'Wednesday' },
  { key: 'thu', label: 'Thursday' },
  { key: 'fri', label: 'Friday' },
  { key: 'sat', label: 'Saturday' },
  { key: 'sun', label: 'Sunday' },
]

const DEFAULT_HOURS: DayHours = { open: '08:00', close: '18:00' }

function parseBusinessHours(json: string | null): BusinessHours {
  if (json) {
    try {
      const parsed = JSON.parse(json) as Partial<BusinessHours>
      return {
        mon: parsed.mon ?? DEFAULT_HOURS,
        tue: parsed.tue ?? DEFAULT_HOURS,
        wed: parsed.wed ?? DEFAULT_HOURS,
        thu: parsed.thu ?? DEFAULT_HOURS,
        fri: parsed.fri ?? DEFAULT_HOURS,
        sat: parsed.sat,
        sun: parsed.sun,
        closedDays: parsed.closedDays ?? ['sat', 'sun'],
      }
    } catch {
      // Fall through to the default below - malformed stored JSON shouldn't crash the wizard.
    }
  }
  return {
    mon: DEFAULT_HOURS,
    tue: DEFAULT_HOURS,
    wed: DEFAULT_HOURS,
    thu: DEFAULT_HOURS,
    fri: DEFAULT_HOURS,
    closedDays: ['sat', 'sun'],
  }
}

export function Step3BusinessHours({
  company,
  onNext,
  onBack,
}: {
  company: CompanyDetails
  onNext: () => void
  onBack: () => void
}) {
  const queryClient = useQueryClient()
  const [hours, setHours] = useState<BusinessHours>(() => parseBusinessHours(company.businessHoursJson))
  const [error, setError] = useState<string | null>(null)

  function isClosed(day: string) {
    return hours.closedDays.includes(day)
  }

  function toggleClosed(day: keyof Omit<BusinessHours, 'closedDays'>) {
    setHours((prev) => ({
      ...prev,
      closedDays: isClosed(day) ? prev.closedDays.filter((d) => d !== day) : [...prev.closedDays, day],
      [day]: prev[day] ?? DEFAULT_HOURS,
    }))
  }

  function updateTime(day: keyof Omit<BusinessHours, 'closedDays'>, field: keyof DayHours, value: string) {
    setHours((prev) => ({
      ...prev,
      [day]: { ...(prev[day] ?? DEFAULT_HOURS), [field]: value },
    }))
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.patch<CompanyDetails>('/api/companies/me', {
        businessHoursJson: JSON.stringify(hours),
      })
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['company'], data)
      onNext()
    },
    onError: () => setError("Couldn't save those hours — try again."),
  })

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-400">
        When are humans available? Outside these hours the AI handles everything on its own.
      </p>

      {error && (
        <div role="alert" className="rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
          {error}
        </div>
      )}

      <div className="space-y-2">
        {DAYS.map((day) => {
          const closed = isClosed(day.key)
          const dayHours = hours[day.key] ?? DEFAULT_HOURS
          return (
            <div key={day.key} className="flex items-center gap-3 rounded-lg border border-ink-700 px-3 py-2">
              <span className="w-24 shrink-0 text-sm text-line-200">{day.label}</span>

              <label className="flex items-center gap-1.5 text-xs text-muted-400">
                <input
                  type="checkbox"
                  checked={!closed}
                  onChange={() => toggleClosed(day.key)}
                  className="accent-teal-500"
                />
                Open
              </label>

              {!closed && (
                <div className="flex flex-1 items-center justify-end gap-2">
                  <input
                    type="time"
                    value={dayHours.open}
                    onChange={(e) => updateTime(day.key, 'open', e.target.value)}
                    className="rounded-md border border-ink-700 bg-ink-950 px-2 py-1 text-sm text-line-200"
                  />
                  <span className="text-muted-400">–</span>
                  <input
                    type="time"
                    value={dayHours.close}
                    onChange={(e) => updateTime(day.key, 'close', e.target.value)}
                    className="rounded-md border border-ink-700 bg-ink-950 px-2 py-1 text-sm text-line-200"
                  />
                </div>
              )}
              {closed && <span className={clsx('flex-1 text-right text-xs text-muted-400')}>Closed</span>}
            </div>
          )
        })}
      </div>

      <div className="flex justify-between pt-2">
        <button
          type="button"
          onClick={onBack}
          className="rounded-lg border border-ink-700 px-4 py-2 text-sm text-muted-400 hover:text-line-200"
        >
          Back
        </button>
        <button
          type="button"
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending}
          className="rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
        >
          {mutation.isPending ? 'Saving…' : 'Continue'}
        </button>
      </div>
    </div>
  )
}

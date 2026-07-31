import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { api } from '@/lib/api'
import type { CompanyDetails } from '@/lib/types'

const VOICES: { value: CompanyDetails['brandVoice']; label: string; example: string }[] = [
  {
    value: 'Formal',
    label: 'Formal',
    example: '"Thank you for contacting us. We have received your request and will respond within 24 hours."',
  },
  {
    value: 'Friendly',
    label: 'Friendly',
    example: '"Hey there! Thanks for reaching out 😊 We\'ll get back to you super soon!"',
  },
  {
    value: 'Neutral',
    label: 'Neutral',
    example: '"Thanks for your message. We\'ll respond shortly."',
  },
]

export function Step2BrandVoice({
  company,
  onNext,
  onBack,
}: {
  company: CompanyDetails
  onNext: () => void
  onBack: () => void
}) {
  const queryClient = useQueryClient()
  const [brandVoice, setBrandVoice] = useState<CompanyDetails['brandVoice']>(company.brandVoice)
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.patch<CompanyDetails>('/api/companies/me', { brandVoice })
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['company'], data)
      onNext()
    },
    onError: () => setError("Couldn't save that — try again."),
  })

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-400">How should your AI sound when it replies to customers?</p>

      {error && (
        <div role="alert" className="rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
          {error}
        </div>
      )}

      <div className="space-y-2">
        {VOICES.map((voice) => (
          <label
            key={voice.value}
            className={clsx(
              'block cursor-pointer rounded-lg border p-3 transition-colors',
              brandVoice === voice.value ? 'border-teal-400 bg-teal-500/10' : 'border-ink-700 hover:bg-ink-800',
            )}
          >
            <div className="flex items-center gap-2">
              <input
                type="radio"
                name="brandVoice"
                value={voice.value}
                checked={brandVoice === voice.value}
                onChange={() => setBrandVoice(voice.value)}
                className="accent-teal-500"
              />
              <span className="text-sm font-medium text-line-200">{voice.label}</span>
            </div>
            <p className="mt-1.5 pl-6 text-sm italic text-muted-400">{voice.example}</p>
          </label>
        ))}
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

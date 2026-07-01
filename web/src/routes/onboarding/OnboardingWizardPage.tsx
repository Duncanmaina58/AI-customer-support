import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import clsx from 'clsx'
import { Check } from 'lucide-react'
import { api } from '@/lib/api'
import type { CompanyDetails } from '@/lib/types'
import { Step1Details } from '@/routes/onboarding/steps/Step1Details'
import { Step2BrandVoice } from '@/routes/onboarding/steps/Step2BrandVoice'
import { Step3BusinessHours } from '@/routes/onboarding/steps/Step3BusinessHours'
import { Step4ConnectChannel } from '@/routes/onboarding/steps/Step4ConnectChannel'

const STEPS = [
  { label: 'Company details' },
  { label: 'Brand voice' },
  { label: 'Business hours' },
  { label: 'Connect a channel' },
]

export function OnboardingWizardPage() {
  const [stepIndex, setStepIndex] = useState(0)

  const { data: company, isLoading } = useQuery({
    queryKey: ['company'],
    queryFn: async () => {
      const { data } = await api.get<CompanyDetails>('/api/companies/me')
      return data
    },
  })

  function goNext() {
    setStepIndex((i) => Math.min(i + 1, STEPS.length - 1))
  }

  function goBack() {
    setStepIndex((i) => Math.max(i - 1, 0))
  }

  if (isLoading || !company) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-ink-950">
        <div className="h-6 w-6 animate-spin rounded-full border-2 border-teal-400 border-t-transparent" />
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-ink-950 px-4 py-10">
      <div className="mx-auto max-w-xl">
        <div className="mb-8 text-center">
          <h1 className="text-xl font-semibold text-white">Let's set up {company.name}</h1>
          <p className="mt-1 text-sm text-muted-400">A few quick steps before your AI starts answering.</p>
        </div>

        <ol className="mb-8 flex items-center justify-between">
          {STEPS.map((step, i) => (
            <li key={step.label} className="flex flex-1 items-center last:flex-none">
              <div className="flex flex-col items-center gap-1.5">
                <span
                  className={clsx(
                    'flex h-7 w-7 items-center justify-center rounded-full text-xs font-medium transition-colors',
                    i < stepIndex && 'bg-teal-500 text-white',
                    i === stepIndex && 'bg-teal-500/20 text-mint-300 ring-2 ring-teal-400',
                    i > stepIndex && 'bg-ink-800 text-muted-400',
                  )}
                >
                  {i < stepIndex ? <Check className="h-3.5 w-3.5" /> : i + 1}
                </span>
                <span className={clsx('text-[11px]', i === stepIndex ? 'text-line-200' : 'text-muted-400')}>
                  {step.label}
                </span>
              </div>
              {i < STEPS.length - 1 && (
                <span className={clsx('mx-2 h-px flex-1', i < stepIndex ? 'bg-teal-500' : 'bg-ink-700')} />
              )}
            </li>
          ))}
        </ol>

        <div className="rounded-xl border border-ink-700 bg-ink-900 p-6">
          {stepIndex === 0 && <Step1Details company={company} onNext={goNext} />}
          {stepIndex === 1 && <Step2BrandVoice company={company} onNext={goNext} onBack={goBack} />}
          {stepIndex === 2 && <Step3BusinessHours company={company} onNext={goNext} onBack={goBack} />}
          {stepIndex === 3 && <Step4ConnectChannel onBack={goBack} />}
        </div>
      </div>
    </div>
  )
}

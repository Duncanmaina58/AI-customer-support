import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { AlertTriangle, MailCheck, Sparkles } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import { CopyableSecret } from '@/components/CopyableSecret'
import { PasswordStrengthMeter, isPasswordLikelyValid } from '@/components/PasswordStrengthMeter'
import { AuthBackground } from '@/components/AuthBackground'
import type { RegisterCompanyResponse } from '@/lib/types'

export function RegisterPage() {
  const { login } = useAuth()
  const navigate = useNavigate()

  const [companyName, setCompanyName] = useState('')
  const [ownerName, setOwnerName] = useState('')
  const [ownerEmail, setOwnerEmail] = useState('')
  const [ownerPassword, setOwnerPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isContinuing, setIsContinuing] = useState(false)

  // Once registration succeeds we hold onto the response just long enough to
  // show the one-time secret API key, then it's gone for good (the backend
  // never returns it again after this).
  const [result, setResult] = useState<RegisterCompanyResponse | null>(null)

  const passwordsMatch = confirmPassword.length === 0 || ownerPassword === confirmPassword

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)

    if (ownerPassword !== confirmPassword) {
      setError("Passwords don't match.")
      return
    }

    setIsSubmitting(true)
    try {
      const { data } = await api.post<RegisterCompanyResponse>('/api/auth/register', {
        companyName,
        ownerName,
        ownerEmail,
        ownerPassword,
      })
      setResult(data)
    } catch (err: any) {
      const data = err?.response?.data
      const errors: string[] | undefined = data?.errors
      if (errors?.length) {
        setError(errors.join(' '))
      } else {
        setError(data?.message ?? 'Something went wrong creating your account. That email may already be in use.')
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  async function handleContinue() {
    setIsContinuing(true)
    try {
      // Re-use the password already typed above rather than asking for it
      // again — that's the whole point of "finishing" the onboarding flow.
      await login(ownerEmail, ownerPassword)
      navigate('/dashboard', { replace: true })
    } catch {
      // Extremely unlikely right after a successful registration, but if the
      // auto-login somehow fails, send them to the normal login screen instead
      // of leaving them stuck on a dead button.
      navigate('/login', { replace: true })
    } finally {
      setIsContinuing(false)
    }
  }

  if (result) {
    return (
      <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-ink-950 px-4 py-10">
        <AuthBackground />
        <div className="relative z-[2] w-full max-w-sm animate-reveal">
          <div className="mb-6 text-center">
            <h1 className="text-lg font-semibold text-white">{result.company.name} is ready</h1>
            <p className="mt-1 text-sm text-muted-400">Save your secret API key now — you won't see it again.</p>
          </div>

          <div className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-6">
            <CopyableSecret label="Public key (safe for the web chat widget)" value={result.company.publicApiKey} />
            <CopyableSecret label="Secret key (server-to-server REST API only)" value={result.secretApiKey} />

            <div className="flex items-start gap-2 rounded-lg border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-500">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <span>
                The secret key won't be shown again after you leave this page. If you lose it, you'll need to
                regenerate one from Settings.
              </span>
            </div>

            <div className="flex items-start gap-2 rounded-lg border border-teal-500/30 bg-teal-500/5 px-3 py-2 text-xs text-line-200">
              <MailCheck className="mt-0.5 h-3.5 w-3.5 shrink-0 text-teal-400" />
              <span>
                We've sent a verification link to <span className="font-medium text-white">{ownerEmail}</span> —
                verify whenever you get a moment, no rush.
              </span>
            </div>

            <button
              type="button"
              onClick={handleContinue}
              disabled={isContinuing}
              className="w-full rounded-lg bg-teal-500 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
            >
              {isContinuing ? 'Signing you in…' : "I've saved it — continue to dashboard"}
            </button>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-ink-950 px-4 py-10">
      <AuthBackground />
      <div className="relative z-[2] w-full max-w-sm animate-reveal">
        <div className="mb-8 flex flex-col items-center gap-3 text-center">
          <div
            className="flex h-11 w-11 items-center justify-center rounded-xl text-white shadow-[0_3px_15px_rgba(99,102,241,0.3)]"
            style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
          >
            <Sparkles className="h-5 w-5" />
          </div>
          <div>
            <h1 className="text-lg font-semibold text-white">Create your company</h1>
            <p className="mt-1 text-sm text-muted-400">Start with a 5-pilot-client free trial</p>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-6">
          {error && (
            <div role="alert" className="rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
              {error}
            </div>
          )}

          <Field label="Company name" id="companyName" value={companyName} onChange={setCompanyName} placeholder="Acme Clinics Ltd" />
          <Field label="Your name" id="ownerName" value={ownerName} onChange={setOwnerName} placeholder="Amina Yusuf" />
          <Field label="Work email" id="ownerEmail" type="email" value={ownerEmail} onChange={setOwnerEmail} placeholder="you@company.com" autoComplete="email" />

          <div>
            <Field label="Password" id="ownerPassword" type="password" value={ownerPassword} onChange={setOwnerPassword} placeholder="••••••••••" autoComplete="new-password" />
            <PasswordStrengthMeter password={ownerPassword} />
          </div>

          <div>
            <Field
              label="Confirm password"
              id="confirmPassword"
              type="password"
              value={confirmPassword}
              onChange={setConfirmPassword}
              placeholder="••••••••••"
              autoComplete="new-password"
            />
            {!passwordsMatch && <p className="mt-1 text-xs text-coral-500">Passwords don't match yet.</p>}
          </div>

          <button
            type="submit"
            disabled={isSubmitting || !isPasswordLikelyValid(ownerPassword) || !passwordsMatch}
            className="w-full rounded-lg bg-teal-500 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
          >
            {isSubmitting ? 'Creating account…' : 'Create account'}
          </button>
        </form>

        <p className="mt-6 text-center text-sm text-muted-400">
          Already have an account?{' '}
          <a href="/login" className="text-mint-300 hover:underline">
            Sign in
          </a>
        </p>
      </div>
    </div>
  )
}

function Field({
  label,
  id,
  value,
  onChange,
  placeholder,
  type = 'text',
  autoComplete,
}: {
  label: string
  id: string
  value: string
  onChange: (v: string) => void
  placeholder: string
  type?: string
  autoComplete?: string
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-1.5 block text-sm text-line-200">
        {label}
      </label>
      <input
        id={id}
        type={type}
        required
        autoComplete={autoComplete}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 focus:outline-none transition-colors"
      />
    </div>
  )
}

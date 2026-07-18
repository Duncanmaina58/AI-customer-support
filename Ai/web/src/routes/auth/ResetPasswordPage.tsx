import { useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { AlertCircle, CheckCircle2, KeyRound } from 'lucide-react'
import { api } from '@/lib/api'
import { PasswordStrengthMeter, isPasswordLikelyValid } from '@/components/PasswordStrengthMeter'
import type { AuthActionResult } from '@/lib/types'

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const token = searchParams.get('token') ?? ''

  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [succeeded, setSucceeded] = useState(false)

  const passwordsMatch = confirmPassword.length === 0 || password === confirmPassword

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)

    if (password !== confirmPassword) {
      setError("Passwords don't match.")
      return
    }
    if (!token) {
      setError('This reset link is missing its token — use the link from your email directly.')
      return
    }

    setIsSubmitting(true)
    try {
      await api.post<AuthActionResult>('/api/auth/reset-password', { token, newPassword: password })
      setSucceeded(true)
    } catch (err: any) {
      const data = err?.response?.data
      const errors: string[] | undefined = data?.errors
      setError(errors?.length ? errors.join(' ') : data?.message ?? 'That reset link is invalid or has expired.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-ink-950 px-4">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-3 text-center">
          <span className="relative flex h-10 w-10 items-center justify-center">
            <span className="absolute h-10 w-10 rounded-full bg-teal-500/20" />
            <span className="absolute h-6 w-6 rounded-full bg-teal-500" />
            <span className="absolute -bottom-0.5 -right-0.5 h-3 w-3 rounded-full bg-mint-300 ring-2 ring-ink-950" />
          </span>
          <div>
            <h1 className="text-lg font-semibold text-white">Choose a new password</h1>
            <p className="mt-1 text-sm text-muted-400">Make it one you haven't used here before.</p>
          </div>
        </div>

        {succeeded ? (
          <div className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-6 text-center">
            <CheckCircle2 className="mx-auto h-8 w-8 text-mint-300" />
            <p className="text-sm text-line-200">
              Your password has been reset, and you've been signed out everywhere. Sign in with your new password.
            </p>
            <button
              type="button"
              onClick={() => navigate('/login', { replace: true })}
              className="w-full rounded-lg bg-teal-500 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400"
            >
              Go to sign in
            </button>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-6">
            {error && (
              <div role="alert" className="flex items-start gap-2 rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
                <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                {error}
              </div>
            )}

            <div>
              <label htmlFor="password" className="mb-1.5 block text-sm text-line-200">
                New password
              </label>
              <div className="relative">
                <KeyRound className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-400" />
                <input
                  id="password"
                  type="password"
                  required
                  autoComplete="new-password"
                  autoFocus
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••••"
                  className="w-full rounded-lg border border-ink-700 bg-ink-950 py-2 pl-9 pr-3 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
                />
              </div>
              <PasswordStrengthMeter password={password} />
            </div>

            <div>
              <label htmlFor="confirmPassword" className="mb-1.5 block text-sm text-line-200">
                Confirm new password
              </label>
              <input
                id="confirmPassword"
                type="password"
                required
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                placeholder="••••••••••"
                className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
              />
              {!passwordsMatch && <p className="mt-1 text-xs text-coral-500">Passwords don't match yet.</p>}
            </div>

            <button
              type="submit"
              disabled={isSubmitting || !isPasswordLikelyValid(password) || !passwordsMatch}
              className="w-full rounded-lg bg-teal-500 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
            >
              {isSubmitting ? 'Resetting…' : 'Reset password'}
            </button>
          </form>
        )}
      </div>
    </div>
  )
}

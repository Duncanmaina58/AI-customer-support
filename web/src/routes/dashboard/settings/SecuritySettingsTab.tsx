import { useState, type FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { KeyRound, ShieldCheck, ShieldAlert, AlertCircle, CheckCircle2, Loader2 } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import { PasswordStrengthMeter, isPasswordLikelyValid } from '@/components/PasswordStrengthMeter'
import type { AuthActionResult } from '@/lib/types'

export function SecuritySettingsTab() {
  const { logout } = useAuth()

  return (
    <div className="max-w-lg space-y-6">
      <VerificationStatusCard />
      <ChangePasswordCard onPasswordChanged={logout} />
    </div>
  )
}

function VerificationStatusCard() {
  const { agent } = useAuth()
  const [isSending, setIsSending] = useState(false)
  const [sent, setSent] = useState(false)

  const resendMutation = useMutation({
    mutationFn: async () => {
      setIsSending(true)
      const { data } = await api.post<AuthActionResult>('/api/auth/resend-verification')
      return data
    },
    onSuccess: () => setSent(true),
    onSettled: () => setIsSending(false),
  })

  if (!agent) return null

  return (
    <div className="rounded-xl border border-ink-700 bg-ink-900 p-5">
      <h3 className="mb-3 text-sm font-semibold text-white">Email verification</h3>
      {agent.isEmailVerified ? (
        <div className="flex items-center gap-2 text-sm text-mint-300">
          <ShieldCheck className="h-4 w-4 shrink-0" />
          <span>{agent.email} is verified.</span>
        </div>
      ) : (
        <div className="space-y-3">
          <div className="flex items-center gap-2 text-sm text-amber-500">
            <ShieldAlert className="h-4 w-4 shrink-0" />
            <span>{agent.email} isn't verified yet.</span>
          </div>
          {sent ? (
            <p className="flex items-center gap-1.5 text-xs text-mint-300">
              <CheckCircle2 className="h-3.5 w-3.5" /> Verification email sent — check your inbox.
            </p>
          ) : (
            <button
              type="button"
              onClick={() => resendMutation.mutate()}
              disabled={isSending}
              className="flex items-center gap-1.5 rounded-lg border border-ink-700 px-3 py-1.5 text-xs font-medium text-line-200 hover:bg-ink-800 disabled:opacity-60"
            >
              {isSending && <Loader2 className="h-3 w-3 animate-spin" />}
              Resend verification email
            </button>
          )}
        </div>
      )}
    </div>
  )
}

function ChangePasswordCard({ onPasswordChanged }: { onPasswordChanged: () => void }) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [succeeded, setSucceeded] = useState(false)

  const mutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<AuthActionResult>('/api/auth/change-password', {
        currentPassword,
        newPassword,
      })
      return data
    },
    onSuccess: () => setSucceeded(true),
    onError: (err: any) => {
      const data = err?.response?.data
      const errors: string[] | undefined = data?.errors
      setError(errors?.length ? errors.join(' ') : data?.message ?? 'Something went wrong.')
    },
  })

  const passwordsMatch = confirmPassword.length === 0 || newPassword === confirmPassword

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (newPassword !== confirmPassword) {
      setError("New passwords don't match.")
      return
    }
    mutation.mutate()
  }

  if (succeeded) {
    return (
      <div className="rounded-xl border border-ink-700 bg-ink-900 p-5">
        <h3 className="mb-3 text-sm font-semibold text-white">Change password</h3>
        <div className="space-y-3 text-center">
          <CheckCircle2 className="mx-auto h-7 w-7 text-mint-300" />
          <p className="text-sm text-line-200">
            Your password was changed. You've been signed out everywhere — sign in again with your new password.
          </p>
          <button
            type="button"
            onClick={onPasswordChanged}
            className="rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white hover:bg-teal-400"
          >
            Sign in again
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="rounded-xl border border-ink-700 bg-ink-900 p-5">
      <h3 className="mb-1 text-sm font-semibold text-white">Change password</h3>
      <p className="mb-4 text-xs text-muted-400">
        Changing your password signs you out of every device, including this one.
      </p>

      <form onSubmit={handleSubmit} className="space-y-4">
        {error && (
          <div role="alert" className="flex items-start gap-2 rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2 text-sm text-coral-500">
            <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            {error}
          </div>
        )}

        <div>
          <label htmlFor="currentPassword" className="mb-1.5 block text-xs font-medium text-line-200">
            Current password
          </label>
          <div className="relative">
            <KeyRound className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-400" />
            <input
              id="currentPassword"
              type="password"
              required
              autoComplete="current-password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              className="w-full rounded-lg border border-ink-700 bg-ink-950 py-2 pl-9 pr-3 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
            />
          </div>
        </div>

        <div>
          <label htmlFor="newPassword" className="mb-1.5 block text-xs font-medium text-line-200">
            New password
          </label>
          <input
            id="newPassword"
            type="password"
            required
            autoComplete="new-password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          />
          <PasswordStrengthMeter password={newPassword} />
        </div>

        <div>
          <label htmlFor="confirmNewPassword" className="mb-1.5 block text-xs font-medium text-line-200">
            Confirm new password
          </label>
          <input
            id="confirmNewPassword"
            type="password"
            required
            autoComplete="new-password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            className="w-full rounded-lg border border-ink-700 bg-ink-950 px-3 py-2 text-sm text-line-200 focus:border-teal-400 focus:outline-none"
          />
          {!passwordsMatch && <p className="mt-1 text-xs text-coral-500">Passwords don't match yet.</p>}
        </div>

        <button
          type="submit"
          disabled={mutation.isPending || !isPasswordLikelyValid(newPassword) || !passwordsMatch || !currentPassword}
          className="flex items-center gap-2 rounded-lg bg-teal-500 px-4 py-2 text-sm font-medium text-white hover:bg-teal-400 disabled:opacity-60"
        >
          {mutation.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
          Change password
        </button>
      </form>
    </div>
  )
}

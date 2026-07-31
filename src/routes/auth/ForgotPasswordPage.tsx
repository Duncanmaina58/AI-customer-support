import { useState, type FormEvent } from 'react'
import { Mail, ArrowLeft, CheckCircle2 } from 'lucide-react'
import { api } from '@/lib/api'

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [submitted, setSubmitted] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setIsSubmitting(true)
    try {
      // The backend always returns the same generic success response here,
      // whether or not the email exists — so there's nothing to branch on,
      // deliberately. See AuthController.ForgotPassword.
      await api.post('/api/auth/forgot-password', { email })
    } finally {
      setIsSubmitting(false)
      setSubmitted(true)
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
            <h1 className="text-lg font-semibold text-white">Reset your password</h1>
            <p className="mt-1 text-sm text-muted-400">We'll email you a link to choose a new one.</p>
          </div>
        </div>

        {submitted ? (
          <div className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-6 text-center">
            <CheckCircle2 className="mx-auto h-8 w-8 text-mint-300" />
            <p className="text-sm text-line-200">
              If an account exists for <span className="font-medium text-white">{email}</span>, a password reset
              link is on its way. It expires in 1 hour.
            </p>
            <a href="/login" className="inline-flex items-center gap-1.5 text-sm text-mint-300 hover:underline">
              <ArrowLeft className="h-3.5 w-3.5" /> Back to sign in
            </a>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-6">
            <div>
              <label htmlFor="email" className="mb-1.5 block text-sm text-line-200">
                Work email
              </label>
              <div className="relative">
                <Mail className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-400" />
                <input
                  id="email"
                  type="email"
                  required
                  autoComplete="email"
                  autoFocus
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@company.com"
                  className="w-full rounded-lg border border-ink-700 bg-ink-950 py-2 pl-9 pr-3 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400"
                />
              </div>
            </div>

            <button
              type="submit"
              disabled={isSubmitting}
              className="w-full rounded-lg bg-teal-500 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400 disabled:opacity-60"
            >
              {isSubmitting ? 'Sending…' : 'Send reset link'}
            </button>

            <a href="/login" className="flex items-center justify-center gap-1.5 text-sm text-muted-400 hover:text-line-200">
              <ArrowLeft className="h-3.5 w-3.5" /> Back to sign in
            </a>
          </form>
        )}
      </div>
    </div>
  )
}

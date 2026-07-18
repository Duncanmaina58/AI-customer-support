import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { CheckCircle2, XCircle, Loader2 } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import type { AuthActionResult } from '@/lib/types'

type Status = 'verifying' | 'success' | 'error'

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const { agent, updateAgent } = useAuth()
  const token = searchParams.get('token') ?? ''

  const [status, setStatus] = useState<Status>('verifying')
  const [message, setMessage] = useState('')

  useEffect(() => {
    let cancelled = false

    async function run() {
      if (!token) {
        setStatus('error')
        setMessage('This verification link is missing its token — use the link from your email directly.')
        return
      }

      try {
        const { data } = await api.post<AuthActionResult>('/api/auth/verify-email', { token })
        if (cancelled) return
        setStatus('success')
        setMessage(data.message)
        // If they're already logged in on this device, reflect it immediately
        // instead of waiting for their next login to pick up isEmailVerified.
        if (agent) updateAgent({ isEmailVerified: true })
      } catch (err: any) {
        if (cancelled) return
        setStatus('error')
        setMessage(err?.response?.data?.message ?? 'This verification link is invalid or has expired.')
      }
    }

    run()
    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token])

  return (
    <div className="flex min-h-screen items-center justify-center bg-ink-950 px-4">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-3 text-center">
          <span className="relative flex h-10 w-10 items-center justify-center">
            <span className="absolute h-10 w-10 rounded-full bg-teal-500/20" />
            <span className="absolute h-6 w-6 rounded-full bg-teal-500" />
            <span className="absolute -bottom-0.5 -right-0.5 h-3 w-3 rounded-full bg-mint-300 ring-2 ring-ink-950" />
          </span>
          <h1 className="text-lg font-semibold text-white">Email verification</h1>
        </div>

        <div className="space-y-4 rounded-xl border border-ink-700 bg-ink-900 p-6 text-center">
          {status === 'verifying' && (
            <>
              <Loader2 className="mx-auto h-8 w-8 animate-spin text-teal-400" />
              <p className="text-sm text-muted-400">Verifying your email…</p>
            </>
          )}

          {status === 'success' && (
            <>
              <CheckCircle2 className="mx-auto h-8 w-8 text-mint-300" />
              <p className="text-sm text-line-200">{message}</p>
              <button
                type="button"
                onClick={() => navigate(agent ? '/' : '/login', { replace: true })}
                className="w-full rounded-lg bg-teal-500 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-400"
              >
                {agent ? 'Go to dashboard' : 'Go to sign in'}
              </button>
            </>
          )}

          {status === 'error' && (
            <>
              <XCircle className="mx-auto h-8 w-8 text-coral-500" />
              <p className="text-sm text-line-200">{message}</p>
              <p className="text-xs text-muted-400">
                You can request a fresh link from Settings &gt; Security once you're signed in.
              </p>
              <button
                type="button"
                onClick={() => navigate(agent ? '/' : '/login', { replace: true })}
                className="w-full rounded-lg border border-ink-700 px-3 py-2 text-sm text-line-200 hover:bg-ink-800"
              >
                {agent ? 'Back to dashboard' : 'Back to sign in'}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}

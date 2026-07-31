import { useState } from 'react'
import { MailWarning, Loader2, CheckCircle2 } from 'lucide-react'
import { api } from '@/lib/api'
import { useAuth } from '@/context/useAuth'
import type { AuthActionResult } from '@/lib/types'

/**
 * Soft-gate nudge (see AuthController's class doc comment): an unverified
 * agent can use the whole product, this is just a persistent-but-dismissible-
 * feeling reminder, not a wall. Renders nothing once verified or dismissed
 * for this session.
 */
export function VerifyEmailBanner() {
  const { agent } = useAuth()
  const [dismissed, setDismissed] = useState(false)
  const [isSending, setIsSending] = useState(false)
  const [sent, setSent] = useState(false)

  if (!agent || agent.isEmailVerified || dismissed) return null

  async function handleResend() {
    setIsSending(true)
    try {
      await api.post<AuthActionResult>('/api/auth/resend-verification')
      setSent(true)
    } finally {
      setIsSending(false)
    }
  }

  return (
    <div className="mb-6 flex items-center justify-between gap-3 rounded-xl border border-amber-500/30 bg-amber-500/5 px-4 py-3">
      <div className="flex items-center gap-2.5">
        <MailWarning className="h-4 w-4 shrink-0 text-amber-500" />
        <p className="text-sm text-line-200">
          {sent
            ? <>Verification email sent to <span className="font-medium text-white">{agent.email}</span> — check your inbox.</>
            : <>Verify <span className="font-medium text-white">{agent.email}</span> to make sure you never lose access to your account.</>}
        </p>
      </div>
      <div className="flex shrink-0 items-center gap-3">
        {!sent && (
          <button
            type="button"
            onClick={handleResend}
            disabled={isSending}
            className="flex items-center gap-1.5 whitespace-nowrap rounded-lg bg-amber-500/10 px-3 py-1.5 text-xs font-medium text-amber-500 hover:bg-amber-500/20 disabled:opacity-60"
          >
            {isSending ? <Loader2 className="h-3 w-3 animate-spin" /> : <CheckCircle2 className="h-3 w-3" />}
            Resend email
          </button>
        )}
        <button
          type="button"
          onClick={() => setDismissed(true)}
          className="text-xs text-muted-400 hover:text-line-200"
        >
          Dismiss
        </button>
      </div>
    </div>
  )
}

import { useMemo } from 'react'
import { Check, X } from 'lucide-react'
import clsx from 'clsx'

export interface PasswordCriteria {
  minLength: boolean
  variety: boolean
  noWhitespaceIssue: boolean
}

/**
 * Mirrors the server's PasswordPolicy (Api.Infrastructure.Security) closely
 * enough for live feedback — the server is still the authority (it also
 * checks the common-password denylist and email/name containment, which
 * can't be meaningfully replicated client-side), but this gives the person
 * typing instant signal instead of finding out only after submitting.
 */
export function evaluatePassword(password: string): { score: number; criteria: PasswordCriteria } {
  const hasUpper = /[A-Z]/.test(password)
  const hasLower = /[a-z]/.test(password)
  const hasDigit = /[0-9]/.test(password)
  const hasSymbol = /[^A-Za-z0-9]/.test(password)
  const varietyCount = [hasUpper, hasLower, hasDigit, hasSymbol].filter(Boolean).length

  const criteria: PasswordCriteria = {
    minLength: password.length >= 10,
    variety: varietyCount >= 3,
    noWhitespaceIssue: password.trim() === password && password.length > 0,
  }

  let score = 0
  if (password.length >= 10) score++
  if (password.length >= 14) score++
  if (varietyCount >= 3) score++
  if (varietyCount === 4) score++

  return { score: Math.min(score, 4), criteria }
}

const LEVELS = [
  { label: 'Too weak', color: 'bg-coral-500', text: 'text-coral-500' },
  { label: 'Weak', color: 'bg-coral-500', text: 'text-coral-500' },
  { label: 'Fair', color: 'bg-amber-500', text: 'text-amber-500' },
  { label: 'Good', color: 'bg-teal-500', text: 'text-teal-400' },
  { label: 'Strong', color: 'bg-mint-300', text: 'text-mint-300' },
]

export function PasswordStrengthMeter({ password }: { password: string }) {
  const { score, criteria } = useMemo(() => evaluatePassword(password), [password])

  if (password.length === 0) return null

  const level = LEVELS[score]

  return (
    <div className="mt-2 space-y-2">
      <div className="flex items-center gap-2">
        <div className="flex flex-1 gap-1">
          {[0, 1, 2, 3].map((segment) => (
            <div
              key={segment}
              className={clsx(
                'h-1.5 flex-1 rounded-full transition-colors',
                segment < score ? level.color : 'bg-ink-700',
              )}
            />
          ))}
        </div>
        <span className={clsx('shrink-0 text-xs font-medium', level.text)}>{level.label}</span>
      </div>

      <ul className="space-y-1">
        <CriteriaRow met={criteria.minLength} label="At least 10 characters" />
        <CriteriaRow met={criteria.variety} label="Mix of uppercase, lowercase, numbers & symbols (at least 3)" />
      </ul>
    </div>
  )
}

function CriteriaRow({ met, label }: { met: boolean; label: string }) {
  return (
    <li className={clsx('flex items-center gap-1.5 text-[11px]', met ? 'text-mint-300' : 'text-muted-400')}>
      {met ? <Check className="h-3 w-3 shrink-0" /> : <X className="h-3 w-3 shrink-0" />}
      {label}
    </li>
  )
}

/** Used to gate submit buttons — matches the server's floor (length + variety), not the full policy (denylist/email-containment are server-only checks). */
export function isPasswordLikelyValid(password: string): boolean {
  const { criteria } = evaluatePassword(password)
  return criteria.minLength && criteria.variety
}

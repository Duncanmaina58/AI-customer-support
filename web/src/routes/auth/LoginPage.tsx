import { useState, useCallback, useRef, type FormEvent, type KeyboardEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { AlertCircle, Eye, EyeOff, Lock, Mail, ArrowRight, ShieldCheck, Sparkles } from 'lucide-react'
import { useAuth } from '@/context/useAuth'
import { AuthBackground } from '@/components/AuthBackground'

// ═══════════════════════════════════════════════════════════════════
//  LOGIN PAGE — split-screen, Deep Space AI aesthetic.
//  Same visual language as the Landing page: rotating neural core on the
//  left (desktop only), glass sign-in card on the right. Auth logic below
//  is unchanged from the original plain form — only the shell is new.
// ═══════════════════════════════════════════════════════════════════

/** The same orb / neural-core visual used on the landing page hero, sized for a side panel. */
function AICorePanel() {
  return (
    <div className="relative hidden flex-1 flex-col items-center justify-center overflow-hidden lg:flex">
      <div className="relative flex h-[280px] w-[280px] items-center justify-center">
        <div className="absolute h-[220px] w-[220px] animate-[spin_20s_linear_infinite] rounded-full border border-indigo-500/15">
          <div className="absolute -top-1 left-1/2 h-2 w-2 -translate-x-1/2 rounded-full bg-indigo-400 shadow-[0_0_15px_rgba(129,140,248,0.8)]" />
        </div>
        <div className="absolute h-[250px] w-[250px] animate-[spin_30s_linear_infinite_reverse] rounded-full border border-pink-500/10">
          <div className="absolute -bottom-1 left-1/2 h-2 w-2 -translate-x-1/2 rounded-full bg-pink-400 shadow-[0_0_15px_rgba(236,72,153,0.8)]" />
        </div>
        <div className="absolute h-[280px] w-[280px] animate-[spin_40s_linear_infinite] rounded-full border border-purple-500/8">
          <div className="absolute top-1/2 -right-1 h-2 w-2 -translate-y-1/2 rounded-full bg-purple-400 shadow-[0_0_15px_rgba(168,85,247,0.8)]" />
        </div>

        <div
          className="relative flex h-[150px] w-[150px] items-center justify-center rounded-full"
          style={{
            background: 'conic-gradient(from 0deg, #6366f1, #a855f7, #ec4899, #6366f1)',
            backgroundSize: '200% 200%',
            animation: 'gradient-shift 4s ease infinite',
            boxShadow: '0 0 50px rgba(99,102,241,0.35), 0 0 100px rgba(168,85,247,0.15), inset 0 0 50px rgba(255,255,255,0.08)',
          }}
        >
          <div className="absolute inset-[3px] rounded-full bg-[#0f0f1a] z-[1]" />
          <div
            className="relative z-[2] flex h-[120px] w-[120px] items-center justify-center rounded-full border border-purple-500/20"
            style={{
              background:
                'radial-gradient(circle at 30% 30%, rgba(99,102,241,0.25), transparent 60%), radial-gradient(circle at 70% 70%, rgba(236,72,153,0.15), transparent 60%), #13131f',
            }}
          >
            <div className="flex flex-col items-center gap-1.5">
              <div className="flex gap-5">
                <div
                  className="h-3.5 w-6 rounded-full"
                  style={{
                    background: 'linear-gradient(180deg, #818cf8, #c084fc)',
                    boxShadow: '0 0 15px rgba(129,140,248,0.5), 0 0 30px rgba(192,132,252,0.2)',
                    animation: 'pulse-glow 2.5s ease-in-out infinite',
                  }}
                />
                <div
                  className="h-3.5 w-6 rounded-full"
                  style={{
                    background: 'linear-gradient(180deg, #818cf8, #c084fc)',
                    boxShadow: '0 0 15px rgba(129,140,248,0.5), 0 0 30px rgba(192,132,252,0.2)',
                    animation: 'pulse-glow 2.5s ease-in-out infinite 0.4s',
                  }}
                />
              </div>
              <div
                className="h-[2px] w-8 rounded-sm"
                style={{ background: 'linear-gradient(90deg, #818cf8, #c084fc)', boxShadow: '0 0 8px rgba(129,140,248,0.3)' }}
              />
            </div>
          </div>
        </div>

        <div className="absolute -bottom-14 left-1/2 flex -translate-x-1/2 gap-2">
          {[
            { text: 'AI Online', color: 'rgba(99,102,241,0.15)', text_color: '#818cf8', border: 'rgba(99,102,241,0.25)' },
            { text: '24/7', color: 'rgba(236,72,153,0.15)', text_color: '#f472b6', border: 'rgba(236,72,153,0.25)' },
            { text: 'Neural', color: 'rgba(168,85,247,0.15)', text_color: '#c084fc', border: 'rgba(168,85,247,0.25)' },
          ].map((badge) => (
            <div
              key={badge.text}
              className="rounded-full border px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wider backdrop-blur-xl"
              style={{ background: badge.color, color: badge.text_color, borderColor: badge.border }}
            >
              {badge.text}
            </div>
          ))}
        </div>
      </div>

      <div className="mt-20 max-w-[320px] text-center animate-reveal" style={{ animationDelay: '0.2s' }}>
        <h2 className="mb-2 text-2xl font-extrabold leading-tight tracking-tight text-slate-50">
          Welcome back to your{' '}
          <span className="bg-gradient-to-r from-indigo-400 via-purple-400 to-pink-400 bg-clip-text text-transparent">
            AI Agent
          </span>
        </h2>
        <p className="text-sm leading-relaxed text-slate-400">
          Intelligent, empathetic, and always learning. Sign in to see how it's doing.
        </p>
      </div>
    </div>
  )
}

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const [emailFocused, setEmailFocused] = useState(false)
  const [passwordFocused, setPasswordFocused] = useState(false)

  const emailRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)

  const handleSubmit = useCallback(
    async (e: FormEvent) => {
      e.preventDefault()
      setError(null)
      setIsSubmitting(true)
      try {
        await login(email, password)
        const redirectTo = (location.state as { from?: Location })?.from?.pathname ?? '/dashboard'
        navigate(redirectTo, { replace: true })
      } catch (err: any) {
        if (err?.response?.status === 423) {
          setError(err.response.data?.message ?? 'Too many failed sign-in attempts. Try again shortly.')
        } else {
          setError('That email or password is incorrect.')
        }
        setTimeout(() => emailRef.current?.focus(), 100)
      } finally {
        setIsSubmitting(false)
      }
    },
    [login, email, password, navigate, location.state],
  )

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter' && e.currentTarget === emailRef.current && password.length === 0) {
        passwordRef.current?.focus()
      }
    },
    [password.length],
  )

  return (
    <div className="relative flex min-h-screen w-full overflow-hidden bg-ink-950">
      <AuthBackground />

      <div className="relative z-[2] flex h-full w-full flex-col lg:flex-row">
        <AICorePanel />

        <div className="flex w-full flex-1 flex-col justify-center px-6 py-14 lg:w-[460px] lg:min-w-[460px] lg:flex-none lg:px-10">
          <div className="mx-auto w-full max-w-[380px] animate-reveal">
            <div className="mb-6 flex items-center gap-2.5">
              <div
                className="flex h-9 w-9 items-center justify-center rounded-[10px] text-white shadow-[0_3px_15px_rgba(99,102,241,0.25)]"
                style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
              >
                <Sparkles className="h-4 w-4" />
              </div>
              <span className="text-lg font-bold tracking-tight text-white">Asupport</span>
            </div>

            <h1 className="mb-1 text-2xl font-bold tracking-tight text-white">Welcome back</h1>
            <p className="mb-6 text-sm text-muted-400">Sign in to your AI-powered support dashboard</p>

            {error && (
              <div
                role="alert"
                className="mb-4 flex items-start gap-2 rounded-lg border border-coral-500/40 bg-coral-500/10 px-3 py-2.5 text-sm text-coral-400"
              >
                <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                {error}
              </div>
            )}

            <form onSubmit={handleSubmit} noValidate className="space-y-4">
              <div>
                <label htmlFor="email" className="mb-1.5 block text-xs font-semibold tracking-wide text-muted-400">
                  Work email
                </label>
                <div className="relative flex items-center">
                  <Mail
                    className={`pointer-events-none absolute left-3.5 h-4 w-4 transition-colors ${
                      emailFocused ? 'text-teal-400' : 'text-muted-400'
                    }`}
                  />
                  <input
                    ref={emailRef}
                    id="email"
                    type="email"
                    autoComplete="email"
                    autoFocus
                    required
                    value={email}
                    onChange={(e) => {
                      setEmail(e.target.value)
                      if (error) setError(null)
                    }}
                    onFocus={() => setEmailFocused(true)}
                    onBlur={() => setEmailFocused(false)}
                    onKeyDown={handleKeyDown}
                    placeholder="you@company.com"
                    className="w-full rounded-xl border border-ink-700 bg-ink-900 py-2.5 pl-10 pr-3 text-sm text-line-200 placeholder:text-muted-400 transition-all duration-300 focus:border-teal-400 focus:bg-teal-500/[0.05] focus:outline-none focus:ring-2 focus:ring-teal-500/20"
                  />
                </div>
              </div>

              <div>
                <div className="mb-1.5 flex items-center justify-between">
                  <label htmlFor="password" className="block text-xs font-semibold tracking-wide text-muted-400">
                    Password
                  </label>
                  <a href="/forgot-password" className="text-xs font-semibold text-mint-300 hover:underline">
                    Forgot?
                  </a>
                </div>
                <div className="relative flex items-center">
                  <Lock
                    className={`pointer-events-none absolute left-3.5 h-4 w-4 transition-colors ${
                      passwordFocused ? 'text-teal-400' : 'text-muted-400'
                    }`}
                  />
                  <input
                    ref={passwordRef}
                    id="password"
                    type={showPassword ? 'text' : 'password'}
                    autoComplete="current-password"
                    required
                    value={password}
                    onChange={(e) => {
                      setPassword(e.target.value)
                      if (error) setError(null)
                    }}
                    onFocus={() => setPasswordFocused(true)}
                    onBlur={() => setPasswordFocused(false)}
                    placeholder="••••••••"
                    className="w-full rounded-xl border border-ink-700 bg-ink-900 py-2.5 pl-10 pr-10 text-sm text-line-200 placeholder:text-muted-400 transition-all duration-300 focus:border-teal-400 focus:bg-teal-500/[0.05] focus:outline-none focus:ring-2 focus:ring-teal-500/20"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    tabIndex={-1}
                    className="absolute right-3 rounded-md p-1 text-muted-400 transition-colors hover:text-line-200"
                  >
                    {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                  </button>
                </div>
              </div>

              <button
                type="submit"
                disabled={isSubmitting}
                className="group flex w-full items-center justify-center gap-2 rounded-xl bg-teal-500 py-2.5 text-sm font-semibold text-white transition-all hover:bg-teal-400 disabled:opacity-60"
              >
                {isSubmitting ? (
                  'Signing you in…'
                ) : (
                  <>
                    Sign in
                    <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" />
                  </>
                )}
              </button>
            </form>

            <p className="mt-6 text-center text-sm text-muted-400">
              New company?{' '}
              <a href="/register" className="font-semibold text-mint-300 hover:underline">
                Create an account
              </a>
            </p>

            <div className="mt-4 flex items-center justify-center gap-1.5 rounded-lg border border-green-500/10 bg-green-500/[0.04] py-2">
              <ShieldCheck className="h-3.5 w-3.5 text-green-500" />
              <span className="text-[10px] font-medium text-green-400">Your data stays private to your company</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

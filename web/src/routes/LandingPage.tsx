import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  ArrowRight,
  Bot,
  MessagesSquare,
  Zap,
  ShieldCheck,
  BarChart3,
  Ticket,
  Globe,
  Sparkles,
  CheckCircle2,
  Menu,
  X,
} from 'lucide-react'

// ═══════════════════════════════════════════════════════════════════
//  LANDING PAGE — matches the Login Page's Deep Space AI aesthetic.
//  Same palette (indigo → violet → pink on #0a0a0f), same particle /
//  orb language, same type system. Fully responsive, animated,
//  dependency-free (pure CSS keyframes, shared with index.css).
// ═══════════════════════════════════════════════════════════════════

const NAV_LINKS = [
  { href: '#features', label: 'Features' },
  { href: '#channels', label: 'Channels' },
  { href: '#how-it-works', label: 'How it works' },
  { href: '/pricing', label: 'Pricing' },
]

const FEATURES = [
  {
    icon: Bot,
    title: 'AI that actually resolves tickets',
    desc: 'A retrieval-grounded assistant answers from your own knowledge base — not generic guesses — and escalates cleanly when it should.',
    color: '#6366f1',
    bg: 'rgba(99,102,241,0.1)',
  },
  {
    icon: MessagesSquare,
    title: 'Every channel, one inbox',
    desc: 'Web chat, WhatsApp, email, and more feed into a single conversation stream your team can see and act on.',
    color: '#8b5cf6',
    bg: 'rgba(139,92,246,0.1)',
  },
  {
    icon: BarChart3,
    title: 'Analytics that matter',
    desc: 'Containment rate, response time, CSAT, and escalation reasons — live, so you always know where the gaps are.',
    color: '#ec4899',
    bg: 'rgba(236,72,153,0.1)',
  },
  {
    icon: Ticket,
    title: 'Escalations with full context',
    desc: 'When AI hands off to a human, nothing is lost — the full conversation and reasoning travels with the ticket.',
    color: '#10b981',
    bg: 'rgba(16,185,129,0.1)',
  },
  {
    icon: ShieldCheck,
    title: 'Built for trust',
    desc: 'Role-based access, audit-ready logs, and data handling designed around real support-team compliance needs.',
    color: '#f59e0b',
    bg: 'rgba(245,158,11,0.1)',
  },
  {
    icon: Globe,
    title: 'Priced for East Africa',
    desc: 'Transparent KES pricing with M-Pesa support, built for teams growing across the region — not retrofitted for it.',
    color: '#3b82f6',
    bg: 'rgba(59,130,246,0.1)',
  },
]

const STEPS = [
  {
    n: '01',
    title: 'Connect your channels',
    desc: 'Plug in your web widget, WhatsApp Business number, and inbox in minutes — no engineering lift required.',
  },
  {
    n: '02',
    title: 'Teach it your business',
    desc: 'Feed in docs, FAQs, and policies. The AI grounds every answer in what you actually offer.',
  },
  {
    n: '03',
    title: 'Let it run, stay in control',
    desc: 'Watch containment climb in the dashboard while your team only steps in for what truly needs a human.',
  },
]

const STATS = [
  { value: '24/7', label: 'Always-on coverage' },
  { value: '<2s', label: 'Median first response' },
  { value: '7+', label: 'Supported channels' },
  { value: 'KES', label: 'Native local pricing' },
]

function FloatingParticles() {
  const particles = Array.from({ length: 24 }, (_, i) => ({
    id: i,
    left: `${(i * 37) % 100}%`,
    delay: `${(i * 1.3) % 15}s`,
    duration: `${15 + ((i * 7) % 15)}s`,
    size: i % 4 === 0 ? 3 : 2,
    color: i % 3 === 0 ? 'rgba(99,102,241,0.4)' : i % 3 === 1 ? 'rgba(236,72,153,0.35)' : 'rgba(168,85,247,0.4)',
  }))

  return (
    <div className="pointer-events-none fixed inset-0 z-[1] overflow-hidden">
      {particles.map((p) => (
        <div
          key={p.id}
          className="absolute rounded-full"
          style={{
            left: p.left,
            width: `${p.size}px`,
            height: `${p.size}px`,
            background: p.color,
            animation: `particle ${p.duration} linear infinite`,
            animationDelay: p.delay,
          }}
        />
      ))}
    </div>
  )
}

/** The same orb / neural-core visual language as the login page, scaled up as a hero centerpiece. */
function AICoreHero() {
  return (
    <div className="relative mx-auto flex h-[280px] w-[280px] items-center justify-center sm:h-[360px] sm:w-[360px]">
      <div className="absolute h-[75%] w-[75%] animate-[spin_20s_linear_infinite] rounded-full border border-indigo-500/15">
        <div className="absolute -top-1 left-1/2 h-2.5 w-2.5 -translate-x-1/2 rounded-full bg-indigo-400 shadow-[0_0_15px_rgba(129,140,248,0.8)]" />
      </div>
      <div className="absolute h-[90%] w-[90%] animate-[spin_30s_linear_infinite_reverse] rounded-full border border-pink-500/10">
        <div className="absolute -bottom-1 left-1/2 h-2.5 w-2.5 -translate-x-1/2 rounded-full bg-pink-400 shadow-[0_0_15px_rgba(236,72,153,0.8)]" />
      </div>
      <div className="absolute h-full w-full animate-[spin_40s_linear_infinite] rounded-full border border-purple-500/8">
        <div className="absolute top-1/2 -right-1 h-2.5 w-2.5 -translate-y-1/2 rounded-full bg-purple-400 shadow-[0_0_15px_rgba(168,85,247,0.8)]" />
      </div>

      <div
        className="relative flex h-[55%] w-[55%] items-center justify-center rounded-full"
        style={{
          background: 'conic-gradient(from 0deg, #6366f1, #a855f7, #ec4899, #6366f1)',
          backgroundSize: '200% 200%',
          animation: 'gradient-shift 4s ease infinite',
          boxShadow: '0 0 60px rgba(99,102,241,0.35), 0 0 120px rgba(168,85,247,0.15), inset 0 0 50px rgba(255,255,255,0.08)',
        }}
      >
        <div className="absolute inset-[3px] rounded-full bg-[#0f0f1a] z-[1]" />
        <div
          className="relative z-[2] flex h-[85%] w-[85%] items-center justify-center rounded-full border border-purple-500/20"
          style={{
            background:
              'radial-gradient(circle at 30% 30%, rgba(99,102,241,0.25), transparent 60%), radial-gradient(circle at 70% 70%, rgba(236,72,153,0.15), transparent 60%), #13131f',
          }}
        >
          <div className="flex flex-col items-center gap-2">
            <div className="flex gap-6">
              <div
                className="h-4 w-7 rounded-full"
                style={{
                  background: 'linear-gradient(180deg, #818cf8, #c084fc)',
                  boxShadow: '0 0 15px rgba(129,140,248,0.5), 0 0 30px rgba(192,132,252,0.2)',
                  animation: 'pulse-glow 2.5s ease-in-out infinite',
                }}
              />
              <div
                className="h-4 w-7 rounded-full"
                style={{
                  background: 'linear-gradient(180deg, #818cf8, #c084fc)',
                  boxShadow: '0 0 15px rgba(129,140,248,0.5), 0 0 30px rgba(192,132,252,0.2)',
                  animation: 'pulse-glow 2.5s ease-in-out infinite 0.4s',
                }}
              />
            </div>
            <div
              className="h-[2px] w-9 rounded-sm"
              style={{ background: 'linear-gradient(90deg, #818cf8, #c084fc)', boxShadow: '0 0 8px rgba(129,140,248,0.3)' }}
            />
          </div>
        </div>
      </div>

      {/* Orbiting mini chat bubbles — "conversations flowing through the core" */}
      <div
        className="absolute -left-6 top-8 rounded-xl border border-indigo-500/20 bg-indigo-500/[0.1] px-3 py-2 text-[11px] text-indigo-200 opacity-0 backdrop-blur-xl sm:-left-10"
        style={{ animation: 'slide-up 0.6s ease-out 0.8s forwards' }}
      >
        “Where's my order?”
      </div>
      <div
        className="absolute -right-4 bottom-10 rounded-xl border border-emerald-500/20 bg-emerald-500/[0.1] px-3 py-2 text-[11px] text-emerald-200 opacity-0 backdrop-blur-xl sm:-right-8"
        style={{ animation: 'slide-up 0.6s ease-out 1.1s forwards' }}
      >
        Resolved in 4s ✓
      </div>
    </div>
  )
}

export function LandingPage() {
  const [mobileNavOpen, setMobileNavOpen] = useState(false)
  const [scrolled, setScrolled] = useState(false)

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8)
    window.addEventListener('scroll', onScroll)
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  return (
    <div className="relative min-h-screen w-full overflow-x-hidden bg-[#0a0a0f] font-['Inter',sans-serif] text-slate-200">
      {/* ─── Background layers, matching the login page ─── */}
      <div
        className="pointer-events-none fixed inset-0 z-0"
        style={{
          background: `
            radial-gradient(ellipse at 20% 10%, rgba(99,102,241,0.09) 0%, transparent 50%),
            radial-gradient(ellipse at 80% 20%, rgba(236,72,153,0.06) 0%, transparent 50%),
            radial-gradient(ellipse at 50% 90%, rgba(139,92,246,0.05) 0%, transparent 50%),
            #0a0a0f
          `,
        }}
      />
      <div
        className="pointer-events-none fixed inset-0 z-[1]"
        style={{
          backgroundImage: `
            linear-gradient(rgba(255,255,255,0.015) 1px, transparent 1px),
            linear-gradient(90deg, rgba(255,255,255,0.015) 1px, transparent 1px)
          `,
          backgroundSize: '60px 60px',
          maskImage: 'radial-gradient(ellipse at 50% 0%, black 30%, transparent 70%)',
          WebkitMaskImage: 'radial-gradient(ellipse at 50% 0%, black 30%, transparent 70%)',
        }}
      />
      <FloatingParticles />

      {/* ─── Nav ─── */}
      <header
        className={[
          'sticky top-0 z-30 transition-all duration-300',
          scrolled ? 'border-b border-white/[0.06] bg-[#0a0a0f]/80 backdrop-blur-xl' : 'border-b border-transparent bg-transparent',
        ].join(' ')}
      >
        <div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-4">
          <div className="flex items-center gap-2.5">
            <div
              className="flex h-9 w-9 items-center justify-center rounded-[10px] text-white shadow-[0_3px_15px_rgba(99,102,241,0.25)]"
              style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
            >
              <Sparkles className="h-4 w-4" />
            </div>
            <span className="text-lg font-bold tracking-tight text-slate-50">Asupport</span>
          </div>

          <nav className="hidden items-center gap-8 lg:flex">
            {NAV_LINKS.map((l) => (
              <a key={l.href} href={l.href} className="text-sm font-medium text-slate-400 transition-colors hover:text-slate-100">
                {l.label}
              </a>
            ))}
          </nav>

          <div className="hidden items-center gap-3 lg:flex">
            <Link to="/login" className="text-sm font-medium text-slate-300 transition-colors hover:text-white">
              Sign in
            </Link>
            <Link
              to="/register"
              className="group flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold text-white shadow-[0_3px_15px_rgba(99,102,241,0.25)] transition-all duration-300 hover:-translate-y-0.5 hover:shadow-[0_6px_25px_rgba(99,102,241,0.35)]"
              style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
            >
              Get started
              <ArrowRight className="h-3.5 w-3.5 transition-transform duration-300 group-hover:translate-x-1" />
            </Link>
          </div>

          <button
            onClick={() => setMobileNavOpen((v) => !v)}
            className="flex h-9 w-9 items-center justify-center rounded-lg border border-white/10 text-slate-300 lg:hidden"
            aria-label="Toggle menu"
          >
            {mobileNavOpen ? <X className="h-4 w-4" /> : <Menu className="h-4 w-4" />}
          </button>
        </div>

        {mobileNavOpen && (
          <div className="border-t border-white/[0.06] bg-[#0a0a0f]/95 px-6 py-4 backdrop-blur-xl lg:hidden">
            <div className="flex flex-col gap-3">
              {NAV_LINKS.map((l) => (
                <a key={l.href} href={l.href} onClick={() => setMobileNavOpen(false)} className="text-sm font-medium text-slate-300">
                  {l.label}
                </a>
              ))}
              <div className="mt-2 flex flex-col gap-2 border-t border-white/[0.06] pt-4">
                <Link to="/login" className="rounded-lg border border-white/10 px-4 py-2 text-center text-sm font-medium text-slate-200">
                  Sign in
                </Link>
                <Link
                  to="/register"
                  className="rounded-lg px-4 py-2 text-center text-sm font-semibold text-white"
                  style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
                >
                  Get started
                </Link>
              </div>
            </div>
          </div>
        )}
      </header>

      <main className="relative z-[2]">
        {/* ─── Hero ─── */}
        <section className="mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-6 pb-20 pt-14 lg:grid-cols-2 lg:pt-20">
          <div style={{ animation: 'slide-up 0.7s ease-out forwards' }}>
            <div className="mb-5 inline-flex items-center gap-2 rounded-full border border-indigo-500/20 bg-indigo-500/[0.08] px-3 py-1.5">
              <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 trend-dot" />
              <span className="text-[11px] font-semibold uppercase tracking-wider text-indigo-300">AI Customer Support, live</span>
            </div>
            <h1 className="text-4xl font-extrabold leading-[1.1] tracking-tight text-slate-50 sm:text-5xl lg:text-6xl">
              Support that{' '}
              <span className="bg-gradient-to-r from-indigo-400 via-purple-400 to-pink-400 bg-clip-text text-transparent">
                resolves itself
              </span>
            </h1>
            <p className="mt-5 max-w-lg text-base leading-relaxed text-slate-400 sm:text-lg">
              A multi-tenant AI support platform built for East African teams — grounded answers,
              every channel in one place, and escalation only when it's truly needed.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row">
              <Link
                to="/register"
                className="group flex items-center justify-center gap-2 rounded-xl px-6 py-3 text-sm font-semibold text-white shadow-[0_3px_20px_rgba(99,102,241,0.3)] transition-all duration-300 hover:-translate-y-0.5 hover:shadow-[0_8px_30px_rgba(99,102,241,0.4)]"
                style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
              >
                Start free
                <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" />
              </Link>
              <a
                href="#how-it-works"
                className="flex items-center justify-center gap-2 rounded-xl border border-white/10 bg-white/[0.02] px-6 py-3 text-sm font-medium text-slate-200 transition-all duration-300 hover:border-indigo-500/30 hover:bg-white/[0.05]"
              >
                See how it works
              </a>
            </div>

            <div className="mt-10 grid grid-cols-2 gap-6 sm:grid-cols-4">
              {STATS.map((s) => (
                <div key={s.label}>
                  <div className="font-mono text-xl font-bold text-slate-50 sm:text-2xl">{s.value}</div>
                  <div className="mt-0.5 text-[11px] text-slate-500">{s.label}</div>
                </div>
              ))}
            </div>
          </div>

          <div className="relative flex items-center justify-center" style={{ animation: 'scale-in 0.8s ease-out 0.2s forwards', opacity: 0 }}>
            <AICoreHero />
          </div>
        </section>

        {/* ─── Logos / trust strip ─── */}
        <section className="border-y border-white/[0.06] bg-white/[0.01] py-6">
          <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-center gap-x-10 gap-y-3 px-6 text-[11px] font-semibold uppercase tracking-wider text-slate-600">
            <span>Web Chat</span>
            <span>WhatsApp</span>
            <span>Email</span>
            <span>Messenger</span>
            <span>Telegram</span>
            <span>Instagram</span>
            <span>M-Pesa</span>
          </div>
        </section>

        {/* ─── Features ─── */}
        <section id="features" className="mx-auto max-w-7xl px-6 py-24">
          <div className="mx-auto mb-14 max-w-2xl text-center">
            <span className="text-[11px] font-semibold uppercase tracking-[0.15em] text-indigo-400">Why teams switch</span>
            <h2 className="mt-3 text-3xl font-bold tracking-tight text-slate-50 sm:text-4xl">
              Everything your support desk needs, <em className="font-light not-italic text-slate-400">in one platform</em>
            </h2>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map((f, i) => (
              <div
                key={f.title}
                className="glass-card animate-reveal rounded-2xl p-6"
                style={{ animationDelay: `${0.05 * i}s` }}
              >
                <div
                  className="mb-4 flex h-11 w-11 items-center justify-center rounded-xl"
                  style={{ background: f.bg, border: `1px solid ${f.color}20` }}
                >
                  <f.icon className="h-5 w-5" style={{ color: f.color }} />
                </div>
                <h3 className="mb-2 text-[15px] font-semibold text-slate-50">{f.title}</h3>
                <p className="text-sm leading-relaxed text-slate-400">{f.desc}</p>
              </div>
            ))}
          </div>
        </section>

        {/* ─── Channels ─── */}
        <section id="channels" className="border-y border-white/[0.06] bg-white/[0.01] py-24">
          <div className="mx-auto max-w-7xl px-6">
            <div className="grid grid-cols-1 items-center gap-12 lg:grid-cols-2">
              <div>
                <span className="text-[11px] font-semibold uppercase tracking-[0.15em] text-indigo-400">Omnichannel</span>
                <h2 className="mt-3 text-3xl font-bold tracking-tight text-slate-50 sm:text-4xl">
                  Meet your customers where they already are
                </h2>
                <p className="mt-4 max-w-md text-sm leading-relaxed text-slate-400 sm:text-base">
                  One AI brain, one knowledge base, one escalation flow — powering your web widget,
                  WhatsApp Business number, and inbox at the same time.
                </p>
                <ul className="mt-6 space-y-3">
                  {['Unified conversation history across channels', 'Real-time hand-off to your human team', 'Consistent brand voice everywhere'].map(
                    (item) => (
                      <li key={item} className="flex items-start gap-2.5 text-sm text-slate-300">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-400" />
                        {item}
                      </li>
                    ),
                  )}
                </ul>
              </div>

              <div className="glass-card rounded-2xl p-6">
                <div className="mb-2 flex items-center gap-2">
                  <span className="h-2 w-2 rounded-full bg-emerald-400 trend-dot" />
                  <span className="text-[11px] font-semibold uppercase tracking-wider text-slate-500">Live conversation</span>
                </div>
                <div className="mt-4 space-y-3">
                  <div className="ml-auto max-w-[80%] rounded-xl rounded-br-sm border border-white/[0.06] bg-white/[0.03] px-3.5 py-2.5 text-sm text-slate-300">
                    Do you deliver to Kisii?
                  </div>
                  <div className="max-w-[85%] rounded-xl rounded-bl-sm border border-indigo-500/20 bg-indigo-500/[0.1] px-3.5 py-2.5 text-sm text-indigo-200">
                    Yes — Kisii is one of our standard delivery zones, usually 1–2 business days. Want me to check current stock for you?
                  </div>
                  <div className="ml-auto max-w-[80%] rounded-xl rounded-br-sm border border-white/[0.06] bg-white/[0.03] px-3.5 py-2.5 text-sm text-slate-300">
                    Yes please
                  </div>
                  <div className="flex w-fit items-center gap-1 rounded-xl border border-purple-500/20 bg-purple-500/10 px-3 py-2.5">
                    <div className="h-1 w-1 animate-[typing_1.4s_ease-in-out_infinite] rounded-full bg-purple-400" />
                    <div className="h-1 w-1 animate-[typing_1.4s_ease-in-out_infinite_0.2s] rounded-full bg-purple-400" />
                    <div className="h-1 w-1 animate-[typing_1.4s_ease-in-out_infinite_0.4s] rounded-full bg-purple-400" />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* ─── How it works ─── */}
        <section id="how-it-works" className="mx-auto max-w-7xl px-6 py-24">
          <div className="mx-auto mb-14 max-w-2xl text-center">
            <span className="text-[11px] font-semibold uppercase tracking-[0.15em] text-indigo-400">How it works</span>
            <h2 className="mt-3 text-3xl font-bold tracking-tight text-slate-50 sm:text-4xl">Live in an afternoon, not a quarter</h2>
          </div>

          <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
            {STEPS.map((s) => (
              <div key={s.n} className="glass-card rounded-2xl p-7">
                <span
                  className="font-mono text-3xl font-bold text-transparent"
                  style={{
                    WebkitTextStroke: '1px rgba(99,102,241,0.4)',
                  }}
                >
                  {s.n}
                </span>
                <h3 className="mt-3 text-[15px] font-semibold text-slate-50">{s.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-slate-400">{s.desc}</p>
              </div>
            ))}
          </div>
        </section>

        {/* ─── CTA ─── */}
        <section className="mx-auto max-w-7xl px-6 pb-24">
          <div
            className="relative overflow-hidden rounded-3xl border border-indigo-500/20 px-8 py-16 text-center sm:px-16"
            style={{
              background:
                'radial-gradient(ellipse at 50% 0%, rgba(99,102,241,0.15) 0%, transparent 60%), linear-gradient(180deg, rgba(139,92,246,0.06), rgba(10,10,15,0.4))',
            }}
          >
            <Zap className="mx-auto mb-4 h-8 w-8 text-indigo-400" />
            <h2 className="text-3xl font-bold tracking-tight text-slate-50 sm:text-4xl">Ready to stop drowning in tickets?</h2>
            <p className="mx-auto mt-3 max-w-md text-sm text-slate-400 sm:text-base">
              Set up your first AI-powered channel today. No credit card required to start.
            </p>
            <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
              <Link
                to="/register"
                className="group flex items-center gap-2 rounded-xl px-6 py-3 text-sm font-semibold text-white shadow-[0_3px_20px_rgba(99,102,241,0.3)] transition-all duration-300 hover:-translate-y-0.5 hover:shadow-[0_8px_30px_rgba(99,102,241,0.4)]"
                style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
              >
                Create free account
                <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" />
              </Link>
              <Link to="/pricing" className="text-sm font-semibold text-indigo-300 underline-offset-4 hover:underline">
                View pricing
              </Link>
            </div>
          </div>
        </section>

        {/* ─── Footer ─── */}
        <footer className="border-t border-white/[0.06] px-6 py-10">
          <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-4 sm:flex-row">
            <div className="flex items-center gap-2">
              <div
                className="flex h-7 w-7 items-center justify-center rounded-lg text-white"
                style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
              >
                <Sparkles className="h-3.5 w-3.5" />
              </div>
              <span className="text-sm font-semibold text-slate-300">Asupport</span>
            </div>
            <p className="text-xs text-slate-600">© {new Date().getFullYear()} Asupport. Built for East African support teams.</p>
          </div>
        </footer>
      </main>
    </div>
  )
}

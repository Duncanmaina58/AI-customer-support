/**
 * Shared ambient background for every auth page (Register, Forgot/Reset
 * Password, Verify Email) — the same particle field + radial glow language
 * as the Login page, factored out so each page doesn't repeat ~40 lines of
 * decorative markup. Render as an absolutely-positioned first child inside a
 * `relative` full-height wrapper.
 */
export function AuthBackground() {
  const particles = Array.from({ length: 16 }, (_, i) => ({
    id: i,
    left: `${(i * 41) % 100}%`,
    delay: `${(i * 1.1) % 15}s`,
    duration: `${15 + ((i * 6) % 15)}s`,
    size: i % 4 === 0 ? 3 : 2,
    color: i % 3 === 0 ? 'rgba(99,102,241,0.35)' : i % 3 === 1 ? 'rgba(236,72,153,0.3)' : 'rgba(168,85,247,0.35)',
  }))

  return (
    <>
      <div
        className="pointer-events-none fixed inset-0 z-0"
        style={{
          background: `
            radial-gradient(ellipse at 20% 10%, rgba(99,102,241,0.08) 0%, transparent 50%),
            radial-gradient(ellipse at 85% 80%, rgba(236,72,153,0.05) 0%, transparent 50%),
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
          maskImage: 'radial-gradient(ellipse at center, black 30%, transparent 70%)',
          WebkitMaskImage: 'radial-gradient(ellipse at center, black 30%, transparent 70%)',
        }}
      />
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
    </>
  )
}

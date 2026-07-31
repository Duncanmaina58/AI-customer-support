import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import { Menu, Sparkles } from 'lucide-react'
import { Sidebar, MobileSidebar } from '@/components/Sidebar'
import { VerifyEmailBanner } from '@/components/VerifyEmailBanner'

export function DashboardLayout() {
  const [mobileNavOpen, setMobileNavOpen] = useState(false)

  return (
    <div className="flex h-screen overflow-hidden bg-ink-950">
      <Sidebar />
      <MobileSidebar open={mobileNavOpen} onClose={() => setMobileNavOpen(false)} />

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Mobile-only top bar — the full sidebar takes over from lg: up. */}
        <header className="flex items-center gap-3 border-b border-ink-700 bg-ink-900/80 px-4 py-3 backdrop-blur-xl lg:hidden">
          <button
            onClick={() => setMobileNavOpen(true)}
            aria-label="Open menu"
            className="rounded-lg p-2 text-muted-400 hover:bg-ink-800 hover:text-line-200"
          >
            <Menu className="h-5 w-5" />
          </button>
          <div className="flex items-center gap-2">
            <span
              className="flex h-6 w-6 items-center justify-center rounded-md"
              style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
            >
              <Sparkles className="h-3 w-3 text-white" />
            </span>
            <span className="text-sm font-semibold text-white">Asupport</span>
          </div>
        </header>

        <main className="flex-1 overflow-y-auto">
          <div className="mx-auto max-w-6xl px-5 py-6 sm:px-8 sm:py-8">
            <VerifyEmailBanner />
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}

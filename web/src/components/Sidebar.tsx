import { NavLink } from 'react-router-dom'
import { LayoutGrid, MessagesSquare, BookOpen, Ticket, FlaskConical, Zap, BarChart3, Settings, LogOut, X } from 'lucide-react'
import { useAuth } from '@/context/useAuth'
import clsx from 'clsx'

const NAV_ITEMS = [
  { to: '/dashboard', label: 'Overview', icon: LayoutGrid, end: true },
  { to: '/dashboard/analytics', label: 'Analytics', icon: BarChart3 },
  { to: '/dashboard/conversations', label: 'Conversations', icon: MessagesSquare },
  { to: '/dashboard/knowledge-base', label: 'Knowledge base', icon: BookOpen },
  { to: '/dashboard/tickets', label: 'Tickets', icon: Ticket },
  { to: '/dashboard/sandbox', label: 'Sandbox', icon: FlaskConical },
  { to: '/dashboard/actions', label: 'Actions', icon: Zap },
  { to: '/dashboard/settings', label: 'Settings', icon: Settings },
]

function BrandMark() {
  return (
    <span className="relative flex h-8 w-8 shrink-0 items-center justify-center">
      <span
        className="absolute h-8 w-8 rounded-full opacity-30 blur-[6px]"
        style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)' }}
      />
      <span
        className="relative flex h-7 w-7 items-center justify-center rounded-[9px]"
        style={{ background: 'linear-gradient(135deg, #6366f1, #a855f7)', boxShadow: '0 2px 10px rgba(99,102,241,0.35)' }}
      >
        <span className="h-2 w-2 rounded-full bg-white/90" />
      </span>
      <span className="absolute -bottom-0.5 -right-0.5 h-2.5 w-2.5 rounded-full bg-emerald-400 ring-2 ring-ink-900 trend-dot" />
    </span>
  )
}

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const { agent, logout } = useAuth()

  return (
    <>
      <div className="flex items-center gap-2.5 px-5 py-5">
        <BrandMark />
        <div className="min-w-0 leading-tight">
          <p className="truncate text-sm font-semibold text-white">Asupport</p>
          <p className="truncate text-[11px] text-muted-400">Support Platform</p>
        </div>
      </div>

      <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-2">
        {NAV_ITEMS.map(({ to, label, icon: Icon, end }) => (
          <NavLink
            key={to}
            to={to}
            end={end}
            onClick={onNavigate}
            className={({ isActive }) =>
              clsx(
                'group relative flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-all duration-200',
                isActive
                  ? 'bg-gradient-to-r from-teal-500/15 to-purple-500/10 text-mint-300 ring-1 ring-teal-500/20'
                  : 'text-muted-400 hover:bg-ink-800 hover:text-line-200',
              )
            }
          >
            {({ isActive }) => (
              <>
                <span
                  className={clsx(
                    'absolute left-0 top-1/2 h-4 w-[3px] -translate-y-1/2 rounded-full bg-gradient-to-b from-teal-400 to-purple-400 transition-opacity duration-200',
                    isActive ? 'opacity-100' : 'opacity-0',
                  )}
                />
                <Icon className="h-4 w-4 shrink-0" strokeWidth={2} />
                {label}
              </>
            )}
          </NavLink>
        ))}
      </nav>

      <div className="border-t border-ink-700 p-3">
        <div className="flex items-center gap-3 rounded-xl px-3 py-2">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-teal-500/25 to-purple-500/25 text-sm font-semibold text-mint-300 ring-1 ring-teal-500/20">
            {agent?.name.charAt(0).toUpperCase()}
          </div>
          <div className="min-w-0 flex-1 leading-tight">
            <p className="truncate text-sm text-line-200">{agent?.name}</p>
            <p className="truncate text-[11px] text-muted-400">{agent?.role}</p>
          </div>
          <button
            onClick={logout}
            aria-label="Log out"
            className="rounded-md p-1.5 text-muted-400 transition-colors hover:bg-ink-800 hover:text-coral-500"
          >
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </>
  )
}

export function Sidebar() {
  return (
    <aside className="hidden h-screen w-64 shrink-0 flex-col border-r border-ink-700 bg-ink-900/90 backdrop-blur-xl lg:flex">
      <SidebarContent />
    </aside>
  )
}

/** Slide-over drawer used on small screens, toggled from DashboardLayout's mobile header. */
export function MobileSidebar({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <div
      className={clsx(
        'fixed inset-0 z-40 lg:hidden',
        open ? 'pointer-events-auto' : 'pointer-events-none',
      )}
      aria-hidden={!open}
    >
      <div
        className={clsx(
          'absolute inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300',
          open ? 'opacity-100' : 'opacity-0',
        )}
        onClick={onClose}
      />
      <div
        className={clsx(
          'absolute left-0 top-0 flex h-full w-72 max-w-[85vw] flex-col border-r border-ink-700 bg-ink-900 transition-transform duration-300 ease-out',
          open ? 'translate-x-0' : '-translate-x-full',
        )}
      >
        <button
          onClick={onClose}
          aria-label="Close menu"
          className="absolute right-3 top-4 rounded-lg p-1.5 text-muted-400 hover:bg-ink-800 hover:text-line-200"
        >
          <X className="h-4 w-4" />
        </button>
        <SidebarContent onNavigate={onClose} />
      </div>
    </div>
  )
}

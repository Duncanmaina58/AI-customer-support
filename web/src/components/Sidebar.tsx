import { NavLink } from 'react-router-dom'
import { LayoutGrid, MessagesSquare, BookOpen, Ticket, Settings, LogOut } from 'lucide-react'
import { useAuth } from '@/context/useAuth'
import clsx from 'clsx'

const NAV_ITEMS = [
  { to: '/', label: 'Overview', icon: LayoutGrid, end: true },
  { to: '/conversations', label: 'Conversations', icon: MessagesSquare },
  { to: '/knowledge-base', label: 'Knowledge base', icon: BookOpen },
  { to: '/tickets', label: 'Tickets', icon: Ticket },
  { to: '/settings', label: 'Settings', icon: Settings },
]

export function Sidebar() {
  const { agent, logout } = useAuth()

  return (
    <aside className="flex h-screen w-64 flex-col border-r border-ink-700 bg-ink-900">
      <div className="flex items-center gap-2.5 px-5 py-5">
        {/* Signature mark: two overlapping signal dots — "a conversation, being heard". */}
        <span className="relative flex h-8 w-8 items-center justify-center">
          <span className="absolute h-8 w-8 rounded-full bg-teal-500/20" />
          <span className="absolute h-5 w-5 rounded-full bg-teal-500" />
          <span className="absolute -bottom-0.5 -right-0.5 h-2.5 w-2.5 rounded-full bg-mint-300 ring-2 ring-ink-900" />
        </span>
        <div className="leading-tight">
          <p className="text-sm font-semibold text-white">Asupport</p>
          <p className="text-[11px] text-muted-400">Support Platform</p>
        </div>
      </div>

      <nav className="flex-1 space-y-1 px-3 py-2">
        {NAV_ITEMS.map(({ to, label, icon: Icon, end }) => (
          <NavLink
            key={to}
            to={to}
            end={end}
            className={({ isActive }) =>
              clsx(
                'flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors',
                isActive
                  ? 'bg-teal-500/15 text-mint-300'
                  : 'text-muted-400 hover:bg-ink-800 hover:text-line-200',
              )
            }
          >
            <Icon className="h-4 w-4" strokeWidth={2} />
            {label}
          </NavLink>
        ))}
      </nav>

      <div className="border-t border-ink-700 p-3">
        <div className="flex items-center gap-3 rounded-lg px-3 py-2">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-teal-500/20 text-sm font-medium text-mint-300">
            {agent?.name.charAt(0).toUpperCase()}
          </div>
          <div className="min-w-0 flex-1 leading-tight">
            <p className="truncate text-sm text-line-200">{agent?.name}</p>
            <p className="truncate text-[11px] text-muted-400">{agent?.role}</p>
          </div>
          <button
            onClick={logout}
            aria-label="Log out"
            className="rounded-md p-1.5 text-muted-400 hover:bg-ink-800 hover:text-coral-500"
          >
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </aside>
  )
}

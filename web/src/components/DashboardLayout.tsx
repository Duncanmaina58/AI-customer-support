import { Outlet } from 'react-router-dom'
import { Sidebar } from '@/components/Sidebar'

export function DashboardLayout() {
  return (
    <div className="flex h-screen overflow-hidden bg-ink-950">
      <Sidebar />
      <main className="flex-1 overflow-y-auto">
        <div className="mx-auto max-w-6xl px-8 py-8">
          <Outlet />
        </div>
      </main>
    </div>
  )
}

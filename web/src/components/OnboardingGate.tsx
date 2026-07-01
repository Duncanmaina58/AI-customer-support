import { Navigate, Outlet } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api'
import type { CompanyDetails } from '@/lib/types'

export function OnboardingGate() {
  const { data: company, isLoading } = useQuery({
    queryKey: ['company'],
    queryFn: async () => {
      const { data } = await api.get<CompanyDetails>('/api/companies/me')
      return data
    },
    staleTime: 60_000,
  })

  if (isLoading) {
    return (
      <div className="flex h-screen items-center justify-center bg-ink-950">
        <div className="h-6 w-6 animate-spin rounded-full border-2 border-teal-400 border-t-transparent" />
      </div>
    )
  }

  if (company && !company.onboardingCompletedAt) {
    return <Navigate to="/onboarding" replace />
  }

  return <Outlet />
}

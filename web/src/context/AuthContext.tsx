import { useEffect, useState, type ReactNode } from 'react'
import { api, getStoredToken, getStoredRefreshToken, setStoredToken, setStoredRefreshToken, clearTokens } from '@/lib/api'
import type { Agent, LoginResponse } from '@/lib/types'
import { AuthContext } from '@/context/auth-context'

const AGENT_STORAGE_KEY = 'asp_agent'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [agent, setAgent] = useState<Agent | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    // Restore session from localStorage on first load. We trust the cached
    // Agent record for the UI shell; any actual API call will silently
    // refresh an expired access token (see api.ts response interceptor), or
    // fall back to redirecting to /login if the refresh token is also gone.
    const token = getStoredToken()
    const storedAgent = localStorage.getItem(AGENT_STORAGE_KEY)
    if (token && storedAgent) {
      setAgent(JSON.parse(storedAgent))
    }
    setIsLoading(false)
  }, [])

  async function login(email: string, password: string) {
    const { data } = await api.post<LoginResponse>('/api/auth/login', { email, password })
    setStoredToken(data.accessToken)
    setStoredRefreshToken(data.refreshToken)
    localStorage.setItem(AGENT_STORAGE_KEY, JSON.stringify(data.agent))
    setAgent(data.agent)
  }

  async function logout() {
    const refreshToken = getStoredRefreshToken()
    if (refreshToken) {
      try {
        // Best-effort - revoke this session's refresh token server-side so it
        // can't be used again, but don't block clearing local state on it.
        await api.post('/api/auth/logout', { refreshToken })
      } catch {
        // Already logged out, network hiccup, whatever - we're clearing
        // local state regardless, so there's nothing actionable here.
      }
    }
    clearTokens()
    localStorage.removeItem(AGENT_STORAGE_KEY)
    setAgent(null)
  }

  return (
    <AuthContext.Provider value={{ agent, isLoading, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

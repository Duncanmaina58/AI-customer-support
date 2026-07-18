import { createContext } from 'react'
import type { Agent } from '@/lib/types'

export interface AuthContextValue {
  agent: Agent | null
  isLoading: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  /** Patches the cached Agent (e.g. after email verification succeeds) without a full re-login. */
  updateAgent: (patch: Partial<Agent>) => void
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined)

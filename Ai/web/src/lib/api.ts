import axios, { type InternalAxiosRequestConfig } from 'axios'

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export const api = axios.create({
  baseURL: API_BASE_URL,
})

const TOKEN_STORAGE_KEY = 'asp_access_token'
const REFRESH_TOKEN_STORAGE_KEY = 'asp_refresh_token'

export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_STORAGE_KEY)
}

export function setStoredToken(token: string | null) {
  if (token) {
    localStorage.setItem(TOKEN_STORAGE_KEY, token)
  } else {
    localStorage.removeItem(TOKEN_STORAGE_KEY)
  }
}

export function getStoredRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)
}

export function setStoredRefreshToken(token: string | null) {
  if (token) {
    localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, token)
  } else {
    localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY)
  }
}

export function clearTokens() {
  setStoredToken(null)
  setStoredRefreshToken(null)
}

api.interceptors.request.use((config) => {
  const token = getStoredToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Concurrent requests that all 401 at the same moment (e.g. a page that fires
// five queries on load with one stale token) should trigger exactly ONE
// refresh call, not five racing ones. Every 401 handler awaits this same
// in-flight promise instead of starting its own.
let refreshPromise: Promise<string | null> | null = null

async function refreshAccessToken(): Promise<string | null> {
  const refreshToken = getStoredRefreshToken()
  if (!refreshToken) return null

  try {
    // Plain axios, not the `api` instance - going through `api` here would
    // re-enter this same interceptor and could loop.
    const { data } = await axios.post<{ accessToken: string; refreshToken: string }>(
      `${API_BASE_URL}/api/auth/refresh`,
      { refreshToken },
    )
    setStoredToken(data.accessToken)
    setStoredRefreshToken(data.refreshToken)
    return data.accessToken
  } catch {
    clearTokens()
    return null
  }
}

interface RetryableConfig extends InternalAxiosRequestConfig {
  _retriedAfterRefresh?: boolean
}

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config as RetryableConfig | undefined

    // Only the *unauthenticated* auth endpoints are excluded from the refresh-retry
    // dance (they don't send a token in the first place, so a 401 from them is a
    // real credential/token failure, not an expired-access-token situation).
    // change-password and resend-verification ARE authenticated [Authorize]
    // endpoints under the same /api/auth/ prefix and should retry-after-refresh
    // like any other protected call.
    const publicAuthEndpoints = ['/api/auth/login', '/api/auth/register', '/api/auth/refresh', '/api/auth/forgot-password', '/api/auth/reset-password', '/api/auth/verify-email']
    const isPublicAuthEndpoint = publicAuthEndpoints.some((path) => originalRequest?.url?.includes(path))

    if (error.response?.status === 401 && originalRequest && !originalRequest._retriedAfterRefresh && !isPublicAuthEndpoint) {
      originalRequest._retriedAfterRefresh = true

      refreshPromise ??= refreshAccessToken().finally(() => {
        refreshPromise = null
      })
      const newAccessToken = await refreshPromise

      if (newAccessToken) {
        originalRequest.headers.Authorization = `Bearer ${newAccessToken}`
        return api(originalRequest)
      }

      // Refresh itself failed - tokens are already cleared by refreshAccessToken;
      // let ProtectedRoute redirect to /login on the next render rather than
      // forcing a hard reload here.
    }

    return Promise.reject(error)
  },
)

import { useCallback, useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'

export interface WidgetMessage {
  id: string
  role: 'user' | 'ai'
  text: string
  /** True while an AI message is still streaming in token by token. */
  isStreaming?: boolean
}

type ConnectionState = 'connecting' | 'connected' | 'disconnected' | 'reconnecting'

const HUB_PATH = '/hubs/chat'
const SESSION_STORAGE_KEY = 'asp_widget_session_id'

/** One stable customer identity per browser, persisted across page loads/reloads. */
function getOrCreateSessionId(): string {
  let id = localStorage.getItem(SESSION_STORAGE_KEY)
  if (!id) {
    id = crypto.randomUUID()
    localStorage.setItem(SESSION_STORAGE_KEY, id)
  }
  return id
}

/**
 * Manages the SignalR connection to ChatHub for a given company's public key.
 * Handles: connect/reconnect lifecycle, joining the company group, sending
 * messages, and accumulating streamed AI tokens into a single message bubble.
 */
export function useChatHub(apiBaseUrl: string, companyPublicKey: string | null) {
  const [messages, setMessages] = useState<WidgetMessage[]>([])
  const [connectionState, setConnectionState] = useState<ConnectionState>('connecting')
  const [isAiTyping, setIsAiTyping] = useState(false)

  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const sessionIdRef = useRef<string>(getOrCreateSessionId())
  const streamingMessageIdRef = useRef<string | null>(null)

  useEffect(() => {
    if (!companyPublicKey) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}${HUB_PATH}`)
      .withAutomaticReconnect([0, 1000, 3000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connectionRef.current = connection

    connection.on('TypingStart', () => {
      setIsAiTyping(true)
      // Open a fresh streaming bubble that ReceiveToken will append into.
      const id = crypto.randomUUID()
      streamingMessageIdRef.current = id
      setMessages((prev) => [...prev, { id, role: 'ai', text: '', isStreaming: true }])
    })

    connection.on('ReceiveToken', (token: string) => {
      const id = streamingMessageIdRef.current
      if (!id) return
      setMessages((prev) =>
        prev.map((m) => (m.id === id ? { ...m, text: m.text + token } : m)),
      )
    })

    connection.on('ReplyComplete', (payload: { conversationId: string; fullText: string }) => {
      const id = streamingMessageIdRef.current
      setIsAiTyping(false)
      streamingMessageIdRef.current = null
      if (!id) return
      setMessages((prev) =>
        prev.map((m) => (m.id === id ? { ...m, text: payload.fullText, isStreaming: false } : m)),
      )
    })

    connection.on('Error', (message: string) => {
      setIsAiTyping(false)
      const id = streamingMessageIdRef.current ?? crypto.randomUUID()
      streamingMessageIdRef.current = null
      setMessages((prev) => {
        const exists = prev.some((m) => m.id === id)
        const errorText = `⚠️ ${message}`
        return exists
          ? prev.map((m) => (m.id === id ? { ...m, text: errorText, isStreaming: false } : m))
          : [...prev, { id, role: 'ai', text: errorText, isStreaming: false }]
      })
    })

    connection.onreconnecting(() => setConnectionState('reconnecting'))
    connection.onreconnected(() => {
      setConnectionState('connected')
      connection.invoke('JoinCompanyGroup', companyPublicKey).catch(() => {})
    })
    connection.onclose(() => setConnectionState('disconnected'))

    setConnectionState('connecting')
    connection
      .start()
      .then(() => {
        setConnectionState('connected')
        return connection.invoke('JoinCompanyGroup', companyPublicKey)
      })
      .catch(() => setConnectionState('disconnected'))

    return () => {
      connection.stop().catch(() => {})
      connectionRef.current = null
    }
  }, [apiBaseUrl, companyPublicKey])

  const sendMessage = useCallback(
    (text: string) => {
      const trimmed = text.trim()
      if (!trimmed || !companyPublicKey) return

      setMessages((prev) => [
        ...prev,
        { id: crypto.randomUUID(), role: 'user', text: trimmed },
      ])

      connectionRef.current
        ?.invoke('SendMessage', companyPublicKey, sessionIdRef.current, trimmed)
        .catch(() => {
          setMessages((prev) => [
            ...prev,
            { id: crypto.randomUUID(), role: 'ai', text: '⚠️ Could not send — please check your connection.' },
          ])
        })
    },
    [companyPublicKey],
  )

  return { messages, sendMessage, connectionState, isAiTyping }
}

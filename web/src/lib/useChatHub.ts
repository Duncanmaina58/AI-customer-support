import { useCallback, useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'

export interface WidgetMessage {
  id: string
  role: 'user' | 'ai' | 'agent'
  text: string
  /** True while an AI message is still streaming in token by token. */
  isStreaming?: boolean
}

type ConnectionState = 'connecting' | 'connected' | 'disconnected' | 'reconnecting'

interface HistoryResponse {
  conversationId: string
  escalated: boolean
  isSandbox: boolean
  csatScore: number | null
  messages: { role: 'User' | 'Ai' | 'Agent'; text: string }[]
}

const HUB_PATH = '/hubs/chat'
const DEFAULT_SESSION_STORAGE_KEY = 'asp_widget_session_id'

/** One stable customer identity per browser (per storage key), persisted across page loads/reloads. */
function getOrCreateSessionId(storageKey: string): string {
  let id = localStorage.getItem(storageKey)
  if (!id) {
    id = crypto.randomUUID()
    localStorage.setItem(storageKey, id)
  }
  return id
}

function toWidgetRole(serverRole: 'User' | 'Ai' | 'Agent'): WidgetMessage['role'] {
  if (serverRole === 'User') return 'user'
  if (serverRole === 'Agent') return 'agent'
  return 'ai'
}

/**
 * Manages the SignalR connection to ChatHub for a given key — either a
 * company's production PublicApiKey (the real widget) or its SandboxToken
 * (Sprint 6 private test chat). ChatHub resolves either transparently; this
 * hook doesn't need to know which one it was handed.
 *
 * `sessionStorageKey` defaults to the widget's key — pass a different one
 * (e.g. for the sandbox test page) so a production widget session and a
 * sandbox test session never accidentally collide into the same conversation
 * if both are ever opened in the same browser.
 */
export function useChatHub(
  apiBaseUrl: string,
  connectionKey: string | null,
  sessionStorageKey: string = DEFAULT_SESSION_STORAGE_KEY,
) {
  const [messages, setMessages] = useState<WidgetMessage[]>([])
  const [connectionState, setConnectionState] = useState<ConnectionState>('connecting')
  const [isAiTyping, setIsAiTyping] = useState(false)
  const [isEscalated, setIsEscalated] = useState(false)
  const [isSandbox, setIsSandbox] = useState(false)
  const [csatScore, setCsatScore] = useState<number | null>(null)

  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const sessionIdRef = useRef<string>(getOrCreateSessionId(sessionStorageKey))
  const streamingMessageIdRef = useRef<string | null>(null)
  const conversationIdRef = useRef<string | null>(null)

  useEffect(() => {
    if (!connectionKey) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}${HUB_PATH}`, {
        // Sprint 8 security audit: the widget authenticates via a public key
        // argument (JoinCompanyGroup/SendMessage), never cookies — and the
        // server's WidgetCors policy no longer allows credentialed requests
        // (see Program.cs). Must match on both sides, or strict browsers will
        // block the connection instead of just omitting cookies.
        withCredentials: false,
      })
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

    connection.on('ReplyComplete', (payload: { conversationId: string; fullText: string; isSandbox?: boolean }) => {
      const id = streamingMessageIdRef.current
      setIsAiTyping(false)
      streamingMessageIdRef.current = null
      if (typeof payload.isSandbox === 'boolean') setIsSandbox(payload.isSandbox)
      if (payload.conversationId) conversationIdRef.current = payload.conversationId
      if (id) {
        setMessages((prev) => {
          const exists = prev.some((m) => m.id === id)
          return exists
            ? prev.map((m) => (m.id === id ? { ...m, text: payload.fullText, isStreaming: false } : m))
            : [...prev, { id: crypto.randomUUID(), role: 'ai', text: payload.fullText }]
        })
      } else {
        // No streaming bubble was open (e.g. the "added to your open ticket"
        // acknowledgement, which skips the typing/streaming steps entirely).
        setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: 'ai', text: payload.fullText }])
      }
    })

    // A human agent replied from the dashboard — not streamed, just appears.
    connection.on('AgentReply', (payload: { text: string }) => {
      setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: 'agent', text: payload.text }])
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

    async function joinAndRehydrate() {
      try {
        await connection.invoke('JoinCompanyGroup', connectionKey)
        const history = await connection.invoke<HistoryResponse | null>(
          'GetHistory', connectionKey, sessionIdRef.current,
        )
        if (history) {
          setIsEscalated(history.escalated)
          setIsSandbox(history.isSandbox)
          setCsatScore(history.csatScore)
          conversationIdRef.current = history.conversationId
          setMessages(
            history.messages.map((m) => ({
              id: crypto.randomUUID(),
              role: toWidgetRole(m.role),
              text: m.text,
            })),
          )
        }
      } catch {
        // Non-fatal — worst case the widget just starts with no prior history.
      }
    }

    connection.onreconnecting(() => setConnectionState('reconnecting'))
    connection.onreconnected(() => {
      setConnectionState('connected')
      joinAndRehydrate()
    })
    connection.onclose(() => setConnectionState('disconnected'))

    setConnectionState('connecting')
    connection
      .start()
      .then(() => {
        setConnectionState('connected')
        return joinAndRehydrate()
      })
      .catch(() => setConnectionState('disconnected'))

    return () => {
      connection.stop().catch(() => {})
      connectionRef.current = null
    }
  }, [apiBaseUrl, connectionKey, sessionStorageKey])

  const sendMessage = useCallback(
    (text: string) => {
      const trimmed = text.trim()
      if (!trimmed || !connectionKey) return

      setMessages((prev) => [
        ...prev,
        { id: crypto.randomUUID(), role: 'user', text: trimmed },
      ])

      connectionRef.current
        ?.invoke('SendMessage', connectionKey, sessionIdRef.current, trimmed)
        .catch(() => {
          setMessages((prev) => [
            ...prev,
            { id: crypto.randomUUID(), role: 'ai', text: '⚠️ Could not send — please check your connection.' },
          ])
        })
    },
    [connectionKey],
  )

  /** Submits a 1-5 star rating for the current conversation. Resolves false if there's no conversation yet or it was already rated. */
  const submitCsatRating = useCallback(async (score: number): Promise<boolean> => {
    const conversationId = conversationIdRef.current
    if (!conversationId || !connectionRef.current) return false

    try {
      const accepted = await connectionRef.current.invoke<boolean>('SubmitCsatRating', conversationId, score)
      if (accepted) setCsatScore(score)
      return accepted
    } catch {
      return false
    }
  }, [])

  return {
    messages,
    sendMessage,
    connectionState,
    isAiTyping,
    isEscalated,
    isSandbox,
    csatScore,
    submitCsatRating,
    hasConversation: !!conversationIdRef.current,
  }
}

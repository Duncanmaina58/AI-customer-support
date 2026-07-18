import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Send, MessageCircle, FlaskConical, Star, X } from 'lucide-react'
import { useChatHub, type WidgetMessage } from '@/lib/useChatHub'
import { API_BASE_URL } from '@/lib/api'

/**
 * Renders a full chat interface wired to ChatHub. Used by:
 *   - WidgetPage (/widget/chat?key=pub_xxx) — the real embeddable widget
 *   - SandboxChatPage (/test/{sandboxToken}) — Sprint 6 private test chat
 *   - SandboxPage's inline dashboard test panel
 *
 * `connectionKey` is either a PublicApiKey or a SandboxToken — ChatHub
 * resolves either transparently, this component doesn't need to know which.
 */
export function ChatPanel({
  connectionKey,
  sessionStorageKey,
  headerTitle,
  missingKeyMessage,
  className = 'h-screen',
}: {
  connectionKey: string | null
  sessionStorageKey?: string
  headerTitle: string
  missingKeyMessage: string
  className?: string
}) {
  const {
    messages,
    sendMessage,
    connectionState,
    isAiTyping,
    isEscalated,
    isSandbox,
    csatScore,
    submitCsatRating,
    hasConversation,
  } = useChatHub(API_BASE_URL, connectionKey, sessionStorageKey)
  const [draft, setDraft] = useState('')
  const [csatDismissed, setCsatDismissed] = useState(false)
  const scrollRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages])

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!draft.trim() || connectionState !== 'connected') return
    sendMessage(draft)
    setDraft('')
  }

  if (!connectionKey) {
    return (
      <div className={`flex items-center justify-center bg-ink-950 p-6 text-center ${className}`}>
        <p className="text-sm text-coral-500">{missingKeyMessage}</p>
      </div>
    )
  }

  // Show the rating prompt once there's been at least one exchange, not tied
  // to any particular resolution moment — the customer can rate whenever they
  // feel done, and dismissing it is always an option (it should never feel
  // like a gate blocking the conversation).
  const showCsatPrompt = hasConversation && messages.length > 1 && !csatDismissed

  return (
    <div className={`flex flex-col bg-ink-950 ${className}`}>
      <header className="flex items-center gap-2.5 border-b border-ink-700 px-4 py-3">
        <span className="relative flex h-7 w-7 items-center justify-center">
          <span className="absolute h-7 w-7 rounded-full bg-teal-500/20" />
          <span className="absolute h-4 w-4 rounded-full bg-teal-500" />
        </span>
        <div className="leading-tight">
          <p className="text-sm font-medium text-white">{headerTitle}</p>
          <p className="text-[11px] text-muted-400">
            {connectionState === 'connected' && 'We typically reply instantly'}
            {connectionState === 'connecting' && 'Connecting…'}
            {connectionState === 'reconnecting' && 'Reconnecting…'}
            {connectionState === 'disconnected' && 'Connection lost — retrying…'}
          </p>
        </div>
        {isSandbox && (
          <span className="ml-auto flex items-center gap-1 rounded-full bg-amber-500/10 px-2 py-1 text-[10px] font-semibold uppercase tracking-wide text-amber-400">
            <FlaskConical className="h-3 w-3" /> Sandbox
          </span>
        )}
      </header>

      {isEscalated && (
        <div className="mx-4 mt-3 rounded-lg border border-teal-500/30 bg-teal-500/10 px-3 py-2 text-center text-xs text-mint-300">
          Your ticket is open — a support agent will reply here.
        </div>
      )}

      <div ref={scrollRef} className="flex-1 space-y-3 overflow-y-auto px-4 py-4">
        {messages.length === 0 && (
          <div className="flex h-full flex-col items-center justify-center gap-2 text-center text-muted-400">
            <MessageCircle className="h-8 w-8 text-teal-500/50" />
            <p className="text-sm">Send a message to start chatting.</p>
          </div>
        )}

        {messages.map((m) => (
          <MessageBubble key={m.id} message={m} />
        ))}

        {isAiTyping && !messages.some((m) => m.isStreaming) && <TypingIndicator />}
      </div>

      {(showCsatPrompt || csatScore !== null) && (
        <CsatBar
          score={csatScore}
          onSubmit={submitCsatRating}
          onDismiss={() => setCsatDismissed(true)}
        />
      )}

      <form onSubmit={handleSubmit} className="flex items-center gap-2 border-t border-ink-700 p-3">
        <input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Type a message…"
          disabled={connectionState !== 'connected'}
          className="flex-1 rounded-full border border-ink-700 bg-ink-900 px-4 py-2 text-sm text-line-200 placeholder:text-muted-400 focus:border-teal-400 disabled:opacity-60"
        />
        <button
          type="submit"
          disabled={!draft.trim() || connectionState !== 'connected'}
          aria-label="Send message"
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-teal-500 text-white transition-colors hover:bg-teal-400 disabled:opacity-50"
        >
          <Send className="h-4 w-4" />
        </button>
      </form>
    </div>
  )
}

/**
 * CSAT rating bar — shown once, dismissible, never blocks the conversation.
 * Once a rating is submitted (or was already submitted in an earlier
 * session, per GetHistory), shows a small non-interactive confirmation
 * instead of the interactive stars.
 */
function CsatBar({
  score,
  onSubmit,
  onDismiss,
}: {
  score: number | null
  onSubmit: (score: number) => Promise<boolean>
  onDismiss: () => void
}) {
  const [hoverScore, setHoverScore] = useState<number | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [justSubmitted, setJustSubmitted] = useState(false)

  async function handleRate(value: number) {
    if (score !== null || isSubmitting) return
    setIsSubmitting(true)
    const accepted = await onSubmit(value)
    setIsSubmitting(false)
    if (accepted) setJustSubmitted(true)
  }

  if (score !== null) {
    return (
      <div className="flex items-center justify-center gap-1.5 border-t border-ink-800 bg-ink-900/50 px-4 py-2 text-xs text-muted-400">
        <span>Thanks for your feedback —</span>
        <span className="flex items-center gap-0.5">
          {[1, 2, 3, 4, 5].map((n) => (
            <Star key={n} className={`h-3 w-3 ${n <= score ? 'fill-amber-400 text-amber-400' : 'text-ink-700'}`} />
          ))}
        </span>
      </div>
    )
  }

  return (
    <div className="flex items-center justify-between gap-2 border-t border-ink-800 bg-ink-900/50 px-4 py-2">
      <span className="text-xs text-muted-400">
        {justSubmitted ? 'Thanks for your feedback!' : 'How would you rate this conversation?'}
      </span>
      {!justSubmitted && (
        <div className="flex items-center gap-2">
          <div className="flex items-center gap-0.5" onMouseLeave={() => setHoverScore(null)}>
            {[1, 2, 3, 4, 5].map((n) => (
              <button
                key={n}
                type="button"
                aria-label={`Rate ${n} out of 5`}
                disabled={isSubmitting}
                onMouseEnter={() => setHoverScore(n)}
                onClick={() => handleRate(n)}
                className="p-0.5 disabled:opacity-50"
              >
                <Star
                  className={`h-4 w-4 transition-colors ${
                    (hoverScore ?? 0) >= n ? 'fill-amber-400 text-amber-400' : 'text-ink-600 hover:text-ink-500'
                  }`}
                />
              </button>
            ))}
          </div>
          <button type="button" onClick={onDismiss} aria-label="Dismiss" className="text-muted-400 hover:text-line-200">
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}
    </div>
  )
}

function MessageBubble({ message }: { message: WidgetMessage }) {
  const isUser = message.role === 'user'
  return (
    <div className={`flex flex-col ${isUser ? 'items-end' : 'items-start'}`}>
      {message.role === 'agent' && (
        <span className="mb-0.5 px-1 text-[10px] font-medium uppercase tracking-wide text-mint-300">
          Support agent
        </span>
      )}
      <div
        className={`max-w-[80%] rounded-2xl px-3.5 py-2 text-sm leading-relaxed ${
          isUser
            ? 'rounded-br-sm bg-teal-500 text-white'
            : 'rounded-bl-sm bg-ink-800 text-line-200'
        }`}
      >
        {message.text}
        {message.isStreaming && (
          <span className="ml-0.5 inline-block h-3.5 w-1.5 animate-pulse bg-current align-middle" />
        )}
      </div>
    </div>
  )
}

function TypingIndicator() {
  return (
    <div className="flex justify-start">
      <div className="flex items-center gap-1 rounded-2xl rounded-bl-sm bg-ink-800 px-3.5 py-2.5">
        {[0, 1, 2].map((i) => (
          <span
            key={i}
            className="h-1.5 w-1.5 animate-bounce rounded-full bg-muted-400"
            style={{ animationDelay: `${i * 120}ms` }}
          />
        ))}
      </div>
    </div>
  )
}

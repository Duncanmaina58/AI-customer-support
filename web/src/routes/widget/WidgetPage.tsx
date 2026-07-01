import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Send, MessageCircle } from 'lucide-react'
import { useChatHub, type WidgetMessage } from '@/lib/useChatHub'
import { API_BASE_URL } from '@/lib/api'

/**
 * Renders inside the loader.js-injected iframe at /widget/chat?key=pub_xxx.
 * Deliberately outside ProtectedRoute / DashboardLayout — this page has no
 * concept of a logged-in agent, only an anonymous customer talking to a
 * company's AI via the public widget key.
 */
export function WidgetPage() {
  const companyPublicKey = new URLSearchParams(window.location.search).get('key')

  const { messages, sendMessage, connectionState, isAiTyping } = useChatHub(
    API_BASE_URL,
    companyPublicKey,
  )
  const [draft, setDraft] = useState('')
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

  if (!companyPublicKey) {
    return (
      <div className="flex h-screen items-center justify-center bg-ink-950 p-6 text-center">
        <p className="text-sm text-coral-500">
          Missing widget key. Add <code>data-key="pub_xxx"</code> to your embed script tag.
        </p>
      </div>
    )
  }

  return (
    <div className="flex h-screen flex-col bg-ink-950">
      <header className="flex items-center gap-2.5 border-b border-ink-700 px-4 py-3">
        <span className="relative flex h-7 w-7 items-center justify-center">
          <span className="absolute h-7 w-7 rounded-full bg-teal-500/20" />
          <span className="absolute h-4 w-4 rounded-full bg-teal-500" />
        </span>
        <div className="leading-tight">
          <p className="text-sm font-medium text-white">Chat with us</p>
          <p className="text-[11px] text-muted-400">
            {connectionState === 'connected' && 'We typically reply instantly'}
            {connectionState === 'connecting' && 'Connecting…'}
            {connectionState === 'reconnecting' && 'Reconnecting…'}
            {connectionState === 'disconnected' && 'Connection lost — retrying…'}
          </p>
        </div>
      </header>

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

function MessageBubble({ message }: { message: WidgetMessage }) {
  const isUser = message.role === 'user'
  return (
    <div className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
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

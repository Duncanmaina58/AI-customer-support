import { ChatPanel } from '@/components/ChatPanel'

/**
 * Renders inside the loader.js-injected iframe at /widget/chat?key=pub_xxx.
 * Deliberately outside ProtectedRoute / DashboardLayout — this page has no
 * concept of a logged-in agent, only an anonymous customer talking to a
 * company's AI via the public widget key.
 */
export function WidgetPage() {
  const companyPublicKey = new URLSearchParams(window.location.search).get('key')

  return (
    <ChatPanel
      connectionKey={companyPublicKey}
      headerTitle="Chat with us"
      missingKeyMessage='Missing widget key. Add data-key="pub_xxx" to your embed script tag.'
    />
  )
}

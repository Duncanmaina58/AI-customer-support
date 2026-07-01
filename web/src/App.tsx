import { Routes, Route } from 'react-router-dom'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { OnboardingGate } from '@/components/OnboardingGate'
import { DashboardLayout } from '@/components/DashboardLayout'
import { PlaceholderPage } from '@/components/PlaceholderPage'
import { LoginPage } from '@/routes/auth/LoginPage'
import { RegisterPage } from '@/routes/auth/RegisterPage'
import { OnboardingWizardPage } from '@/routes/onboarding/OnboardingWizardPage'
import { OverviewPage } from '@/routes/dashboard/OverviewPage'
import { ConversationsPage } from '@/routes/dashboard/ConversationsPage'
import { KnowledgeBasePage } from '@/routes/dashboard/KnowledgeBasePage'
import { SettingsPage } from '@/routes/dashboard/SettingsPage'
import { WidgetPage } from '@/routes/widget/WidgetPage'

export default function App() {
  return (
    <Routes>
      {/* Anonymous: the embeddable widget runs in an iframe here, no auth */}
      <Route path="/widget/chat" element={<WidgetPage />} />

      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />

      <Route element={<ProtectedRoute />}>
        <Route path="onboarding" element={<OnboardingWizardPage />} />

        <Route element={<OnboardingGate />}>
          <Route element={<DashboardLayout />}>
            <Route index element={<OverviewPage />} />
            <Route path="conversations" element={<ConversationsPage />} />
            <Route path="knowledge-base" element={<KnowledgeBasePage />} />
            <Route
              path="tickets"
              element={<PlaceholderPage title="Tickets" blurb="Escalations that need a human. Coming in Sprint 5." />}
            />
            <Route path="settings" element={<SettingsPage />} />
          </Route>
        </Route>
      </Route>
    </Routes>
  )
}

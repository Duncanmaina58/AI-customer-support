import { Routes, Route } from 'react-router-dom'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { OnboardingGate } from '@/components/OnboardingGate'
import { DashboardLayout } from '@/components/DashboardLayout'
import { LoginPage } from '@/routes/auth/LoginPage'
import { RegisterPage } from '@/routes/auth/RegisterPage'
import { ForgotPasswordPage } from '@/routes/auth/ForgotPasswordPage'
import { ResetPasswordPage } from '@/routes/auth/ResetPasswordPage'
import { VerifyEmailPage } from '@/routes/auth/VerifyEmailPage'
import { PricingPage } from '@/routes/PricingPage'
import { OnboardingWizardPage } from '@/routes/onboarding/OnboardingWizardPage'
import { OverviewPage } from '@/routes/dashboard/OverviewPage'
import { ConversationsPage } from '@/routes/dashboard/ConversationsPage'
import { KnowledgeBasePage } from '@/routes/dashboard/KnowledgeBasePage'
import { TicketsPage } from '@/routes/dashboard/TicketsPage'
import { AnalyticsPage } from '@/routes/dashboard/AnalyticsPage'
import { SandboxPage } from '@/routes/dashboard/SandboxPage'
import { SettingsPage } from '@/routes/dashboard/SettingsPage'
import { WidgetPage } from '@/routes/widget/WidgetPage'
import { SandboxChatPage } from '@/routes/sandbox/SandboxChatPage'

export default function App() {
  return (
    <Routes>
      {/* Anonymous: embeddable widget loads in an iframe here */}
      <Route path="/widget/chat" element={<WidgetPage />} />

      {/* Anonymous: Sprint 6 sandbox test chat, freely shareable */}
      <Route path="/test/:token" element={<SandboxChatPage />} />

      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/pricing" element={<PricingPage />} />

      <Route element={<ProtectedRoute />}>
        <Route path="onboarding" element={<OnboardingWizardPage />} />

        <Route element={<OnboardingGate />}>
          <Route element={<DashboardLayout />}>
            <Route index element={<OverviewPage />} />
            <Route path="conversations" element={<ConversationsPage />} />
            <Route path="knowledge-base" element={<KnowledgeBasePage />} />
            <Route path="tickets" element={<TicketsPage />} />
            <Route path="analytics" element={<AnalyticsPage />} />
            <Route path="sandbox" element={<SandboxPage />} />
            <Route
              path="settings"
              element={<SettingsPage />}
            />
          </Route>
        </Route>
      </Route>
    </Routes>
  )
}

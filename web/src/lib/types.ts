export interface Agent {
  id: string
  name: string
  email: string
  role: 'Owner' | 'Admin' | 'Agent'
  companyId: string
}

export interface LoginResponse {
  accessToken: string
  expiresAtUtc: string
  refreshToken: string
  agent: Agent
}

export interface CompanySummary {
  id: string
  name: string
  plan: string
  publicApiKey: string
}

/** Returned once, at sign-up, by POST /api/auth/register. */
export interface RegisterCompanyResponse {
  company: CompanySummary
  secretApiKey: string
}

export interface CompanyDetails {
  id: string
  name: string
  plan: string
  publicApiKey: string
  defaultCurrency: string
  timeZone: string
  industry: string | null
  logoUrl: string | null
  brandVoice: 'Formal' | 'Friendly' | 'Neutral'
  primaryLanguage: string
  businessHoursJson: string | null
  onboardingCompletedAt: string | null
  createdAt: string
}

export interface DayHours {
  open: string
  close: string
}

/** Decoded shape of CompanyDetails.businessHoursJson. */
export interface BusinessHours {
  mon?: DayHours
  tue?: DayHours
  wed?: DayHours
  thu?: DayHours
  fri?: DayHours
  sat?: DayHours
  sun?: DayHours
  closedDays: string[]
}

export interface ChannelConnection {
  id: string
  channel: 'WebChat' | 'WhatsApp' | 'Email' | 'Messenger' | 'Telegram' | 'Instagram' | 'MobileSdk'
  status: 'Active' | 'Paused' | 'Error'
  displayInfo: string | null
  lastVerifiedAt: string | null
  createdAt: string
}

export interface ConnectWebChatResponse {
  channel: ChannelConnection
  embedScriptTag: string
}

export interface AgentListItem {
  id: string
  name: string
  email: string
  role: 'Owner' | 'Admin' | 'Agent'
  isActive: boolean
  lastActiveAt: string | null
  createdAt: string
}

/** Returned once, at invite time, by POST /api/agents/invite. */
export interface InviteAgentResponse {
  agent: AgentListItem
  temporaryPassword: string
}

export interface Conversation {
  id: string
  channel: 'WebChat' | 'WhatsApp' | 'Email' | 'Messenger' | 'Telegram' | 'Instagram' | 'MobileSdk'
  customerId: string
  customerDisplayName: string | null
  status: 'Open' | 'Pending' | 'Resolved' | 'Escalated'
  createdAt: string
  messageCount: number
}

export interface Message {
  id: string
  role: 'User' | 'Ai' | 'Agent' | 'System'
  content: string
  sentAt: string
}

/** Sprint 3: Analytics summary from GET /api/analytics/summary */
export interface AnalyticsSummary {
  totalConversations: number
  openConversations: number
  resolvedConversations: number
  escalatedConversations: number
  resolutionRate: number
  tokensUsedThisMonth: number
  monthlyTokenBudget: number
}

/** Sprint 4: knowledge base chunk returned by GET /api/knowledge */
export interface KnowledgeChunk {
  id: string
  documentId: string
  documentName: string
  textPreview: string
  fullText: string
  chunkIndex: number
  createdAt: string
}

export interface UpsertKnowledgeRequest {
  documentName: string
  text: string
}

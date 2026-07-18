export interface Agent {
  id: string
  name: string
  email: string
  role: 'Owner' | 'Admin' | 'Agent'
  companyId: string
  isEmailVerified: boolean
}

export interface LoginResponse {
  accessToken: string
  expiresAtUtc: string
  refreshToken: string
  agent: Agent
}

// ---- Auth hardening ----

export interface AuthActionResult {
  success: boolean
  message: string
}

export interface AuthActionErrorResponse {
  message: string
  errors?: string[]
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
  escalationRulesJson: string | null
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
  /** Only meaningful for channel === 'Email': 'Webhook' | 'Imap' | null. */
  inboundMode: 'Webhook' | 'Imap' | null
}

export interface ConnectWebChatResponse {
  channel: ChannelConnection
  embedScriptTag: string
}

/** Sprint 5 follow-up: two inbound modes, client's choice — Webhook (Brevo,
 *  paid plan required for inbound parsing) or Imap (MailKit, free, any mailbox). */
export interface ConnectEmailRequest {
  mode: 'Webhook' | 'Imap'
  senderEmail: string
  senderName: string
  imapHost?: string
  imapPort?: number
  smtpHost?: string
  smtpPort?: number
  username?: string
  password?: string
}

export interface ConnectEmailResponse {
  channel: ChannelConnection
  /** null for Imap-mode connections — there's no webhook to configure. */
  brevoWebhookUrl: string | null
}

/** Sprint 6: Messenger + Telegram */
export interface ConnectMessengerRequest {
  pageAccessToken: string
  pageId: string
}

export interface ConnectTelegramRequest {
  botToken: string
}

export interface SandboxInfo {
  sandboxToken: string
  testLinkPath: string
}

/** Sprint 7: analytics */
export interface AnalyticsSummary {
  totalConversations: number
  conversationsTrendPercent: number | null
  openConversations: number
  resolvedConversations: number
  escalatedConversations: number
  resolutionRate: number
  /** % of conversations resolved by the AI alone, without ever escalating to a human. */
  containmentRate: number
  containmentRateTrendPercent: number | null
  avgFirstResponseSeconds: number | null
  avgFirstResponseTrendPercent: number | null
  tokensUsedThisMonth: number
  monthlyTokenBudget: number
  csatAverageScore: number | null
  csatRatingCount: number
}

export interface DailyConversationCount {
  date: string
  count: number
}

export interface ChannelBreakdown {
  channel: string
  count: number
}

export interface TopQuestion {
  question: string
  count: number
}

export interface EscalationReasonBreakdown {
  reason: string
  count: number
}

export interface DailyTokenUsage {
  date: string
  tokens: number
}

export interface CsatDistributionBucket {
  score: number
  count: number
}

export interface CsatTrendPoint {
  date: string
  averageScore: number | null
  ratingCount: number
}

export interface CsatSummary {
  averageScore: number | null
  ratingCount: number
  distribution: CsatDistributionBucket[]
  trend: CsatTrendPoint[]
}

/** Sprint 7: billing */
export interface BillingPlan {
  plan: 'Starter' | 'Growth' | 'Enterprise'
  name: string
  priceKes: number
  conversationLimit: number
  channelLimit: number
  knowledgeBaseLimit: number
  agentLimit: number
  features: string[]
}

export interface BillingInfo {
  currentPlan: 'Starter' | 'Growth' | 'Enterprise'
  currentPlanPriceKes: number
  monthlyTokenBudget: number
  tokensUsedThisMonth: number
  percentUsed: number
  currentPeriodStartAt: string
  nextResetAt: string
  billingPhoneNumber: string | null
}

export interface InitiateMpesaPaymentRequest {
  plan: string
  phoneNumber: string
}

export interface InitiateMpesaPaymentResponse {
  transactionId: string
  checkoutRequestId: string
}

export interface MpesaTransactionStatus {
  transactionId: string
  status: 'Pending' | 'Success' | 'Failed' | 'Cancelled'
  resultDescription: string | null
  mpesaReceiptNumber: string | null
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

// ---- Sprint 4 (Web Crawling update) ----

export type WebCrawlMode = 'FullSite' | 'SinglePage' | 'Sitemap'
export type WebSourceStatus = 'Pending' | 'Crawling' | 'Indexed' | 'Error' | 'Paused'
export type WebSourceMonitoringMode = 'Adaptive' | 'Fixed' | 'Manual'
export type WebPageStatus = 'Active' | 'Removed' | 'Error'

export interface WebSource {
  id: string
  url: string
  crawlMode: WebCrawlMode
  crawlDepth: number
  includePattern: string | null
  excludePattern: string | null
  maxPages: number
  status: WebSourceStatus
  pagesCrawled: number
  chunksCreated: number
  monitoringMode: WebSourceMonitoringMode
  fixedIntervalHours: number | null
  notifyOnChange: boolean
  lastCrawledAt: string | null
  errorMessage: string | null
  hasJsRenderedPagesWarning: boolean
  maxPagesReached: boolean
  createdAt: string
}

export interface WebSourceStatusUpdate {
  id: string
  status: WebSourceStatus
  pagesCrawled: number
  estimatedTotalPages: number | null
  currentCrawlUrl: string | null
  errorMessage: string | null
}

export interface WebPage {
  id: string
  url: string
  title: string | null
  status: WebPageStatus
  checkCount: number
  changeCount: number
  lastCheckedAt: string | null
  lastChangedAt: string | null
  nextCheckAt: string
  contentLength: number
}

export interface WebPageChange {
  url: string
  changeType: 'changed' | 'removed'
  detectedAt: string
}

export interface CreateWebSourceRequest {
  url: string
  crawlMode: 'full_site' | 'single_page' | 'sitemap'
  crawlDepth: number
  includePattern: string | null
  excludePattern: string | null
  maxPages: number
  monitoringMode: 'adaptive' | 'fixed' | 'manual'
  fixedIntervalHours: number | null
  notifyOnChange: boolean
}

export interface UpdateMonitoringRequest {
  monitoringMode: 'adaptive' | 'fixed' | 'manual'
  fixedIntervalHours: number | null
  notifyOnChange: boolean
}

// ---- Sprint 5: Tickets ----

export interface TicketListItem {
  id: string
  ticketNumber: number
  subject: string
  status: 'Open' | 'InProgress' | 'Resolved' | 'Closed'
  priority: 'Low' | 'Medium' | 'High' | 'Urgent'
  assignedTeam: string | null
  assignedToName: string | null
  escalationReason: string | null
  conversationChannel: string
  customerIdentifier: string
  createdAt: string
  resolvedAt: string | null
}

export interface TicketMessage {
  id: string
  role: 'User' | 'Ai' | 'Agent' | 'System'
  content: string
  sentAt: string
}

export interface TicketDetail {
  id: string
  ticketNumber: number
  subject: string
  status: 'Open' | 'InProgress' | 'Resolved' | 'Closed'
  priority: 'Low' | 'Medium' | 'High' | 'Urgent'
  assignedTeam: string | null
  assignedToId: string | null
  assignedToName: string | null
  escalationReason: string | null
  conversationId: string
  conversationChannel: string
  customerIdentifier: string
  customerDisplayName: string | null
  createdAt: string
  resolvedAt: string | null
  messages: TicketMessage[]
}

export interface UpdateTicketStatusRequest {
  status: string
}

export interface AgentReplyRequest {
  message: string
}

export interface AssignTicketRequest {
  agentId: string | null
  team: string | null
}

/**
 * Decoded/encoded shape of CompanyDetails.escalationRulesJson.
 * Field names are camelCase here but bind case-insensitively to the C# side's
 * PascalCase EscalationRules class (EscalationService.cs) — see its doc comment.
 * All fields optional so a partial or empty object still round-trips safely.
 */
export interface EscalationRules {
  escalateOnLowConfidence: boolean
  confidenceThreshold: number
  escalateOnAgentRequest: boolean
  escalateOnPaymentKeywords: boolean
  defaultAssignedTeam: string
}

export const DEFAULT_ESCALATION_RULES: EscalationRules = {
  escalateOnLowConfidence: true,
  confidenceThreshold: 0.6,
  escalateOnAgentRequest: true,
  escalateOnPaymentKeywords: false,
  defaultAssignedTeam: 'Support',
}

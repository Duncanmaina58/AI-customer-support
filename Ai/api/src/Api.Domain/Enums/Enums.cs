namespace Api.Domain.Enums;

public enum BrandVoice
{
    Formal,
    Friendly,
    Neutral
}

public enum CompanyPlan
{
    Starter,
    Growth,
    Enterprise
}

public enum ChannelType
{
    WebChat,
    WhatsApp,
    Email,
    Messenger,
    Telegram,
    Instagram,
    MobileSdk
}

public enum ConversationStatus
{
    Open,
    Pending,
    Resolved,
    Escalated
}

public enum MessageRole
{
    User,
    Ai,
    Agent,
    System
}

public enum TicketStatus
{
    Open,
    InProgress,
    Resolved,
    Closed
}

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Urgent
}

public enum ChannelConnectionStatus
{
    Active,
    Paused,
    Error
}

public enum AgentRole
{
    Owner,
    Admin,
    Agent
}

/// <summary>Sprint 7: lifecycle of a single M-Pesa STK Push payment attempt.</summary>
public enum MpesaTransactionStatus
{
    /// <summary>STK push sent to the phone; waiting for the customer to enter their PIN (or cancel/time out).</summary>
    Pending,
    Success,
    Failed,
    Cancelled
}

/// <summary>What kind of event was recorded in a WebPageChangeLog row (change-history feed for the dashboard).</summary>
public enum WebPageChangeType
{
    Added,
    Changed,
    Removed,
}

// -----------------------------------------------------------------------
// Sprint 4 (Web Crawling): a company's live website as a knowledge source,
// kept fresh automatically via three-tier change detection. See WebSource,
// WebPage, WebCrawlerService, ContentFreshnessService.
// -----------------------------------------------------------------------

/// <summary>How a WebSource discovers the set of pages to crawl.</summary>
public enum WebCrawlMode
{
    /// <summary>Follow internal links breadth-first up to CrawlDepth (or use the sitemap if found).</summary>
    FullSite,
    /// <summary>Index exactly one URL, no link following.</summary>
    SinglePage,
    /// <summary>Force sitemap.xml discovery even if BFS would also work.</summary>
    Sitemap
}

/// <summary>Lifecycle status of a WebSource (a connected website).</summary>
public enum WebSourceStatus
{
    Pending,
    Crawling,
    Indexed,
    Error,
    Paused
}

/// <summary>How often ContentFreshnessService re-checks this source's pages.</summary>
public enum WebSourceMonitoringMode
{
    /// <summary>AdaptiveCheckScheduler sets NextCheckAt per-page based on each page's historical change rate.</summary>
    Adaptive,
    /// <summary>Every page on this source is checked every FixedIntervalHours, regardless of change history.</summary>
    Fixed,
    /// <summary>Never checked automatically — only via the manual "Check Now" action.</summary>
    Manual
}

/// <summary>Lifecycle status of a single crawled WebPage.</summary>
public enum WebPageStatus
{
    Active,
    Removed,
    Error
}

/// <summary>Distinguishes where a KnowledgeChunk's content came from.</summary>
public enum KnowledgeSourceType
{
    /// <summary>Uploaded/pasted document or manual knowledge-base entry (Sprint 4 original).</summary>
    Document,
    /// <summary>Crawled from a company's live website (Sprint 4 web crawling addition).</summary>
    Web
}

// -----------------------------------------------------------------------
// Auth hardening: email verification + password reset use the same
// single-use, hashed, expiring token pattern as RefreshToken, distinguished
// by purpose. See Api.Domain.Entities.AgentSecurityToken.
// -----------------------------------------------------------------------

/// <summary>What an AgentSecurityToken is for — one table, two purposes, since both are "prove you control this inbox" flows.</summary>
public enum AgentSecurityTokenType
{
    EmailVerification,
    PasswordReset,
}


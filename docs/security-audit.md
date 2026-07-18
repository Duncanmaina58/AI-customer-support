# Security Audit — Sprint 8 (Launch Readiness)

**Date:** 2026-07-12
**Scope:** Every controller and background service in `api/src`, all inbound
webhook integrations, CORS configuration, secrets handling, and the frontend's
handling of credentials. Goal: the Sprint 8 checklist item "Zero data leakage
in final security review of all endpoints."

**Method:** Manual code review of every controller (`Api/Controllers/*.cs`),
every `[AllowAnonymous]` endpoint, the tenant-isolation query-filter mechanism,
CORS policy configuration, JWT configuration, password hashing, and all three
inbound social webhook integrations, cross-referenced against the existing
`TenantIsolationTests.cs` suite. This was a code-level audit, not a
penetration test — a third-party pentest is still recommended before scaling
past the pilot cohort (see "Recommendations" below).

## Findings & remediations

### 1. WhatsApp/Messenger webhooks accepted unverified requests — FIXED
**Severity: High.** `WhatsAppWebhookController` and `MessengerWebhookController`
had no cryptographic verification of inbound POSTs. Anyone who discovered or
guessed a company's webhook URL (`/webhook/whatsapp/{companyId}`) could POST a
forged payload that would be processed as a real customer message — consuming
AI token budget, polluting conversation history, and potentially triggering
false escalations/tickets.

**Fix:** added `VerifyMetaSignatureAttribute` (`Api/Filters/`), an
`IAsyncResourceFilter` that recomputes the HMAC-SHA256 of the raw request body
using a platform-wide `Meta:AppSecret` and compares it to Meta's
`X-Hub-Signature-256` header (constant-time comparison) before model binding
runs. Applied to both webhook POST endpoints.

**Action required before launch:** set `Meta:AppSecret` (from the Meta App
Dashboard → Settings → Basic) in production configuration. Until it's set, the
filter fails *open* with a warning log rather than breaking webhooks outright
— confirm the warning is gone from production logs before onboarding real
pilot traffic.

### 2. Telegram webhook accepted unverified requests — FIXED
**Severity: High.** Same class of issue as #1. Telegram supports a
`secret_token` mechanism (set via `setWebhook`, verified against the
`X-Telegram-Bot-Api-Secret-Token` header) that wasn't implemented.

**Fix:** `TelegramCredentials` now carries a per-connection random
`SecretToken`, generated and registered with Telegram at connect time
(`ChannelsController.ConnectTelegram`). `TelegramWebhookController` verifies it
(constant-time) on every inbound update. Connections made before this change
have `SecretToken == null` and are accepted-but-logged until reconnected —
**action required:** ask any already-connected pilot clients to disconnect and
reconnect their Telegram bot once this ships, or reconnect it yourself during
their onboarding walkthrough.

### 3. Widget CORS policy combined `AllowAnyOrigin` with `AllowCredentials` — FIXED
**Severity: Medium.** `WidgetCorsPolicy` (used by the public chat hub) allowed
any origin *and* credentialed requests simultaneously. The widget never
actually uses cookie-based auth (it authenticates via a public key argument
over SignalR — see `ChatHub`), so this bought no functionality, only risk: any
website could make a credentialed cross-origin call against the API.

**Fix:** removed `AllowCredentials()` from both `WidgetCorsPolicy` and
`DashboardCorsPolicy` (the dashboard also doesn't use cookies — it sends a
Bearer JWT from `localStorage`, confirmed in `web/src/lib/api.ts`). Matched on
the client side: `useChatHub.ts`'s `HubConnectionBuilder` now explicitly sets
`withCredentials: false` so strict browsers don't block the now-non-credentialed
connection.

### 4. No rate limiting on any endpoint — FIXED
**Severity: Medium.** Nothing stood between a scripted client and unlimited
requests to any endpoint, including login/register (credential stuffing risk)
and the AI chat pipeline itself (a cost-DoS vector — each message costs real
Groq/Cohere tokens).

**Fix:** `Program.cs` now registers a global sliding-window limiter (300
req/min, partitioned per authenticated agent or per client IP for anonymous
callers) covering every endpoint, plus a tighter dedicated policy (10 req/min)
on `AuthController` specifically. Webhook and health-check paths are exempted
from the *general* limiter since they're provider-driven traffic already
hardened by signature verification (#1, #2) — a legitimate Meta/Telegram retry
burst shouldn't trip a generic limiter meant for human/script traffic.

### 5. `RemoteIpAddress` was the proxy's IP, not the client's — FIXED
**Severity: Low, but undermines #4 and audit logging.** Render (and most PaaS)
terminates TLS and proxies every request through an edge load balancer.
Without `ForwardedHeadersOptions`, every request appears to originate from the
same internal IP — collapsing the new rate limiter's per-IP partitioning into
one shared bucket, and making the `RemoteIp` field now attached to every
structured log line useless.

**Fix:** `app.UseForwardedHeaders()` configured to trust `X-Forwarded-For` /
`X-Forwarded-Proto`, with `KnownNetworks`/`KnownProxies` cleared (standard for
PaaS deployments with no fixed, publishable proxy CIDR — safe here because the
app has no reachable network path except through that proxy).

### 6. Unhandled exceptions could leak internal detail to clients — FIXED
**Severity: Low-Medium.** No global exception handler was registered. In
Production, ASP.NET Core's default behaviour without one is *usually* safe
(a bare 500 with no body), but this was implicit rather than guaranteed, and
gave no structured log record of what actually failed.

**Fix:** `app.UseExceptionHandler(...)` in non-Development environments logs
the full exception via Serilog and returns a fixed, generic JSON error body —
never the exception message, stack trace, or any internal type/connection
info.

### 7. Structured logging — DONE (supports #1–#6's audit trail)
Serilog now backs every log call in the app (`Program.cs`), writing compact
JSON to stdout, with:
- `UseSerilogRequestLogging()` — one structured line per HTTP request
  (method, path, status, elapsed ms), enriched with `CompanyId` (from the JWT)
  and `RemoteIp` when available.
- A bootstrap logger that catches startup failures before configuration/DI
  is even up.
- EF Core's per-query command logs demoted to `Warning` (they're noise at
  `Information` and were never actionable).

No sensitive values (passwords, API keys, JWTs, webhook secrets) are logged
anywhere in the codebase — verified by grepping every `_logger.Log*` call
against a list of secret-shaped field names; the only matches were connection
identifiers (`Host`, `CompanyId`) and exception objects, never raw credential
values.

## Reviewed and found OK (no change needed)

- **Tenant isolation:** every `ITenantScoped` entity is covered by
  `AppDbContext`'s global query filter (reflection-based, applied automatically
  to new entities — confirmed for the Sprint 4 web-crawling additions too, see
  `TenantIsolationTests.WebSource_and_WebPage_queries_only_return_rows_for_the_current_tenant`).
  Background services correctly use `IgnoreQueryFilters()` + explicit
  `CompanyId`/`WebSourceId` predicates, never a bare unfiltered query.
- **Password hashing:** `PasswordHasher<Agent>` (ASP.NET Core's built-in,
  PBKDF2-based) — no custom/weak hashing.
- **JWT configuration:** issuer/audience/lifetime/signing-key all validated;
  `Jwt:SigningKey` throws on startup if unconfigured rather than silently
  falling back to a weak default.
- **Swagger:** only mounted in `Development` — not reachable in Production.
- **M-Pesa callback / sandbox token lookup / pricing endpoint:** the three
  other `[AllowAnonymous]` endpoints are legitimately public (server-to-server
  callback, opaque-token lookup with no sensitive data returned, and public
  pricing info respectively) — no change needed.
- **Credentials at rest:** channel credentials (WhatsApp tokens, Telegram bot
  tokens, etc.) are stored encrypted (`Api.Infrastructure.Security` credential
  protector), not plaintext.
- **Email inbound webhook (Brevo):** unlike Meta/Telegram, Brevo's inbound
  parse webhook doesn't offer a shared-secret/HMAC signing mechanism to verify
  against — its own security model relies on the webhook URL itself being
  unguessable. `webhook/email/{companyId}` already uses a GUID (122 bits of
  entropy) as that unguessable component, which is the accepted mitigation
  for this specific provider. Flagged here rather than silently skipped: if
  Brevo adds signing support in the future, wire it in the same way as
  findings #1/#2.

## Recommendations before scaling past the pilot cohort

1. **Set `Meta:AppSecret` in production** — see finding #1. Confirm the "UNVERIFIED"
   warning stops appearing in logs.
2. **Reconnect any already-live Telegram bots** so they pick up a `SecretToken`
   — see finding #2.
3. **Rotate all secrets** referenced in `appsettings.Development.json` before
   this codebase (or its git history) is ever shared beyond the current team —
   this file is gitignored, but confirm it stays that way (`git check-ignore
   api/src/Api/appsettings.Development.json` should print the path).
4. **Run the Sprint 8 load test** (`api/tools/LoadTest`) against a staging
   deployment and confirm p95 < 5s at 50 concurrent conversations before
   onboarding pilot #1 — see `docs/load-testing.md`.
5. **Third-party penetration test** once past the pilot stage — this audit is
   thorough code review, not adversarial testing (no fuzzing, no auth-bypass
   attempts against the deployed instance, no dependency CVE scan).
6. **Dependency scanning** — run `dotnet list package --vulnerable` (or enable
   Dependabot/GitHub security alerts on the repo) periodically; not done as
   part of this audit.

---

## Addendum — full authentication rebuild

The original auth flow (register/login/refresh/logout with rotating, hashed
refresh tokens) was already sound at the mechanics level — the gaps were
everything *around* it that a real SaaS product needs. This addendum covers
what was added; see `AuthController.cs`'s class-level doc comment for the
design principles applied throughout.

### What was missing, and what was built

- **No password strength requirements.** Registration accepted any string as
  a password, including `"a"`. Added `PasswordPolicy`
  (`Api.Infrastructure/Security/PasswordPolicy.cs`) — minimum length, character
  variety, a common-password denylist, and email/name-containment checks —
  enforced server-side on register, reset, and change-password. The frontend's
  `PasswordStrengthMeter` gives live feedback but the server is always the
  authority.
- **No email verification.** Any email address, real or not, could register
  and use the product indefinitely. Added a token-based verification flow
  (soft-gated — see the design note in `AuthController`'s doc comment for why
  it doesn't block product use, just nudges via a dashboard banner).
- **No forgot/reset password flow.** A user who forgot their password had no
  recovery path at all. Added `forgot-password` / `reset-password`, with the
  standard no-enumeration behaviour (same response whether or not the email
  exists) and single-use, hour-lifetime tokens.
- **No change-password flow.** No way to rotate your password while logged
  in short of losing access and using reset. Added `change-password`
  (requires the current password even though already authenticated — a
  hijacked-but-logged-in session shouldn't be able to silently lock out the
  real owner).
- **No account lockout.** Login had no defense against an online brute-force
  guesser beyond the Sprint 8 rate limiter (which is IP/agent-partitioned, not
  account-partitioned — a distributed attacker rotating IPs could still
  hammer one specific account). Added a 5-attempts/15-minute lockout tracked
  per-Agent, independent of the rate limiter.
- **No security notification emails.** Password changes, resets, lockouts,
  and sign-ins from a new location happened silently. This is precisely how
  account takeovers go unnoticed by the legitimate owner. Added all four as
  branded HTML emails (`AuthEmailService`).
- **Password reset/change didn't revoke sessions.** Not present before there
  was a reset/change flow at all, but worth stating as a design decision now
  that there is one: both actions revoke every refresh token for that agent,
  full stop — a password change should end every session, not just prompt a
  re-login on the device that changed it.

### Design decisions worth flagging explicitly

- **Soft-gated verification, not hard-gated.** An unverified agent can use
  the entire product. This matches how most successful SaaS products actually
  behave (Slack, Notion, etc.) and avoids a UX regression against the existing
  flow, which already shows a security-sensitive secret API key immediately
  after registration. If the business wants a harder gate later (e.g.
  blocking payment-method changes or secret-key regeneration until verified),
  that's a small, additive change — `agent.EmailVerifiedAt is not null` is
  already available everywhere it'd be needed.
- **"New sign-in" detection is IP-comparison only**, not real device
  fingerprinting or geo-IP lookup (no network access to a geo-IP service was
  available while building this, and IPs are trivially spoofable/shared
  besides) — it's a courtesy heads-up, not a security control. Don't treat
  its absence-of-alert as "definitely the real owner."
- **The common-password denylist is small and curated**, not a breach corpus.
  Recommendation #6 above (dependency scanning) has a sibling here: consider
  wiring in the HaveIBeenPwned Pwned Passwords API (k-anonymity range query,
  no plaintext password ever leaves the server) for real breach-corpus
  coverage once the API is reachable from the production environment.
- **Invited teammates (`AgentsController.Invite`) now also get a verification
  email** — previously only self-registered owners did. Every Agent row now
  goes through the same "prove you control this inbox" flow, not just the
  first person at a company.


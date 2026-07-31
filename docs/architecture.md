# Architecture

## Layering

```
Api                 ← HTTP layer: controllers, Program.cs, DTOs, JWT/Swagger wiring
 └─ Api.Application  ← use-case interfaces (ICurrentTenantProvider, IAppDbContext, IAiProvider...)
     └─ Api.Domain    ← entities, enums. Zero external dependencies on purpose.
 └─ Api.Infrastructure ← EF Core, Postgres/pgvector, JWT issuance. Implements Application's interfaces.
```

Dependency direction always points inward: `Api → Application → Domain`, and
`Infrastructure → Application + Domain`. `Domain` never references EF Core,
ASP.NET Core, or any other framework — it's pure C#, which is why
`Api.Domain` is the only project that builds with zero NuGet packages.

## Multi-tenancy

Every tenant-scoped entity (`Agent`, `Conversation`, `Message`, `Ticket`,
`KnowledgeChunk`) implements `ITenantScoped { Guid CompanyId; }`.
`AppDbContext.OnModelCreating` walks the EF Core model at startup and installs
a **global query filter** on every one of them:

```csharp
modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.CompanyId == _tenantProvider.CompanyId);
```

`_tenantProvider` is `ICurrentTenantProvider`, implemented by
`HttpCurrentTenantProvider`, which reads the `company_id` claim out of the
validated JWT for the current request. **No controller or service needs to
remember to filter by company — it is structurally impossible to forget.**
The one deliberate escape hatch is `.IgnoreQueryFilters()`, used only in
`AuthController.Login` and `CompaniesController.Register` (there is no
authenticated tenant yet at those two points by definition).

See `tests/Api.Tests/TenantIsolationTests.cs` for the test that proves this.

## Auth

JWT bearer tokens, issued by `JwtTokenService` on successful login. Claims:

| Claim         | Purpose                                            |
|---------------|-----------------------------------------------------|
| `sub`         | Agent id                                            |
| `company_id`  | Drives the multi-tenant query filter                |
| `agent_id`    | Convenience duplicate of `sub`                       |
| `role`        | `Owner` / `Admin` / `Agent` — for future `[Authorize(Roles = ...)]` |

Refresh tokens are opaque random strings (`JwtTokenService.CreateRefreshToken`)
— storing/rotating them against a `RefreshTokens` table is not wired up yet.

## Data model (Sprint 1 slice)

`Company` (tenant root) → `Agent`, `Conversation` → `Message`, `Ticket`;
`KnowledgeChunk` (RAG, pgvector). Matches the ER diagram from the product
roadmap. Action Engine / billing / sentiment tables are Phase 2+, not in this
slice.

## Sprint 2 — Onboarding & team management

Added on top of the Sprint 1 foundation:

- **Secret API key fix**: `POST /api/companies/register` previously generated a
  secret key, hashed it, and discarded the plaintext — nobody could ever see
  it. It's now returned once in `RegisterCompanyResponse.SecretApiKey`, same
  one-time-reveal pattern as agent invite passwords below.
- **`GET/PATCH /api/companies/me`** — company settings (name, time zone,
  currency). `Company` is the tenant root, not `ITenantScoped`, so these
  filter by `_currentTenant.CompanyId` explicitly rather than relying on the
  global query filter.
- **`AgentsController`** — team management:
  - `GET /api/agents` — any teammate can see the roster.
  - `GET /api/agents/me` — current agent's own profile.
  - `POST /api/agents/invite` *(Owner/Admin)* — creates a new Agent with a
    one-time temporary password (no email service yet, so the admin shares it
    out-of-band). Can only assign `Admin` or `Agent` — never `Owner`.
  - `PATCH /api/agents/{id}` *(Owner/Admin)* — change role/active status.
    Blocked from modifying your own account, and blocked from demoting or
    deactivating the company's last active Owner.
- **Fixed a real cross-tenant bug**: `Agent.Email` was uniquely indexed per
  `(CompanyId, Email)`, but `AuthController.Login` looks up agents by email
  alone (no tenant context yet at login). Two companies could have raced to
  register the same email, after which login would non-deterministically pick
  one. Email is now globally unique at the DB level, matching what `Register`
  already enforced at the app level.
- **Frontend**: `/register` now shows the one-time secret-key reveal, then
  auto-logs the new owner in (no retyping the password). `/settings` has
  Company and Team tabs, the latter with invite/role/deactivate flows wired to
  the endpoints above.

All of the above was verified two ways: a full manual code-review pass (still
no NuGet access in the authoring sandbox), and a mock API server matching the
real DTOs' exact JSON shape, driven end-to-end with Playwright through the
actual rendered UI (login → settings → invite → role change → deactivate).
Still needs a real `dotnet build`/`dotnet test` pass on a machine with NuGet
access before merging.

## Sprint 1 & 2 closure (gap-analysis follow-up)

A deep-dive spec doc surfaced real gaps against what "Sprint 1" and "Sprint 2"
actually require. Closed in this pass:

**Sprint 1:**
- `POST /api/auth/refresh` — rotates a refresh token (single-use; reusing an
  already-rotated token revokes *all* of that agent's active tokens, since
  that pattern only happens on token theft). `POST /api/auth/logout` revokes
  one session. Both backed by a new `RefreshTokens` table.
- `Register` relocated from `CompaniesController` to `AuthController` —
  `/api/auth/register`, matching the spec's grouping (login/register/refresh/
  logout all live together).
- Fixed a bug while in there: the secret API key generated at registration
  was hashed and stored but the *plaintext* was never returned anywhere —
  it's now revealed once, same pattern as agent invite passwords.

**Sprint 2:**
- Onboarding wizard, steps 1-4: company details, brand voice, business
  hours, connect a channel. `Company` gained `Industry`, `LogoUrl`,
  `BrandVoice`, `PrimaryLanguage`, `BusinessHoursJson`,
  `OnboardingCompletedAt` (plus `MonthlyTokenBudget`/`TokensUsedThisMonth`/
  `EscalationRulesJson` for Sprint 4/5, added now to avoid a third migration
  touching this table again).
- New `ChannelConnections` table — one row per connected channel,
  credentials encrypted via ASP.NET Core's Data Protection API (AES under
  the hood, ships in the shared framework, no extra NuGet package).
- `ChannelsController`: connect/verify WhatsApp (calls Meta's Graph API to
  prove the token actually works before saving anything), send a test
  message, activate Web Chat (returns the embed script tag), disconnect.
- `WhatsAppWebhookController` at `/webhook/whatsapp/{companyId}` — Meta's
  verification handshake (GET) and inbound message handling (POST). Scope
  is deliberately just "echo the message back" per the spec — turning
  inbound messages into real `Conversation`/`Message` rows and running them
  through the AI pipeline is Sprint 3, not this.
- Minimal `ChatHub` (SignalR) mapped at `/hubs/chat` — connection lifecycle
  only. Real message routing is Sprint 3; this just satisfies "the hub
  exists" for the Web Chat connection option.
- Fixed a real bug caught via end-to-end testing: the onboarding wizard's
  "Finish setup" button used `invalidateQueries` (async background refetch)
  immediately followed by `navigate('/')` — a race condition where the
  dashboard gate could read the still-stale cached company data and bounce
  straight back to the wizard. Fixed by using the mutation's own response to
  update the cache synchronously before navigating.

### Migration note — you already have one applied

You already ran `dotnet ef migrations add InitialCreate` + `database update`
against the old model. Since then the model gained two tables and several
`Company` columns. You have two options:

**Option A (recommended for a dev DB with no real data yet) — start clean:**
```bash
dotnet ef database drop --project src/Api.Infrastructure --startup-project src/Api --force
rm -rf src/Api.Infrastructure/Persistence/Migrations
dotnet ef migrations add InitialCreate --project src/Api.Infrastructure --startup-project src/Api -o Persistence/Migrations
dotnet ef database update --project src/Api.Infrastructure --startup-project src/Api
```

**Option B — additive migration, keeps existing data:**
```bash
dotnet ef migrations add AddOnboardingAndChannels --project src/Api.Infrastructure --startup-project src/Api -o Persistence/Migrations
dotnet ef database update --project src/Api.Infrastructure --startup-project src/Api
```

### Testing the WhatsApp webhook locally

Meta needs a public HTTPS URL to call, so for local development use
[ngrok](https://ngrok.com): `ngrok http 5080`, then in your Meta App
dashboard's WhatsApp webhook config, set the callback URL to
`https://<your-ngrok-subdomain>.ngrok-free.app/webhook/whatsapp/<your-company-id>`
and the verify token to whatever you set `WhatsApp:VerifyToken` to in
`appsettings.Development.json`.

### Resolved in Sprint 3: widget CORS

~~The CORS policy only allows the dashboard's own origin...~~ — see the new
**Sprint 3 — Live channels** section below for how this was resolved
(`WidgetCorsPolicy` + `RequireCors` on the SignalR hub endpoint).

## Build status

This foundation was originally written in a network-restricted sandbox where
`api.nuget.org` was blocked, so the first pass couldn't be restore-verified.
It has since been **built and run successfully** on a real machine with NuGet
access. One real bug surfaced and was fixed: `Api.Infrastructure` is a plain
class library (no implicit ASP.NET Core framework reference), so it needs an
explicit

```xml
<FrameworkReference Include="Microsoft.AspNetCore.App" />
```

in `Api.Infrastructure.csproj` — without it, NuGet fights over transitive
versions of `Microsoft.Extensions.Configuration.Abstractions` pulled in by
EF Core Design vs. the pinned version, and restore fails with a hard
`NU1605` downgrade error. This is now in place and confirmed working.

## Sprint 3 — Live channels (Website Chat Widget + Real-Time SignalR)

**Goal: a customer can chat with the AI on a client's website in real time,
and WhatsApp messages get a real (placeholder) AI reply instead of an echo.**

### The shared problem: no JWT on inbound traffic

Both entry points this sprint builds — the SignalR `ChatHub` and the
WhatsApp webhook — receive traffic from anonymous third parties (a website
visitor, Meta's servers). Neither has a `company_id` JWT claim, so
`ICurrentTenantProvider.CompanyId` is `null` and the global EF Core query
filter would silently return nothing for every tenant-scoped query.

The fix is `ConversationService` (`Api.Infrastructure/Services`), the single
place allowed to call `.IgnoreQueryFilters()` outside of auth bootstrapping.
Every method takes an explicit `companyId` parameter supplied by the caller
(the WhatsApp webhook's `{companyId}` route segment; the widget's public-key
lookup in `ChatHub.SendMessage`) and applies it as a hand-written predicate.
`ConversationServiceTests.cs` proves this holds even when two different
companies' customers share the same external identifier (e.g. the same phone
number messaging two different companies' WhatsApp numbers) — they land in
two distinct, correctly-isolated conversations.

### Message pipeline (shared by both channels)

```
inbound message
  → ConversationService.GetOrCreateAsync   (find/create the open conversation)
  → ConversationService.AppendMessageAsync (store the customer's message, role=User)
  → ConversationService.GetRecentHistoryLinesAsync (last 10 messages, for AI context)
  → IAiProvider.GenerateReplyAsync         (PlaceholderAiProvider this sprint)
  → ConversationService.AppendMessageAsync (store the AI's reply, role=Ai)
  → deliver back via the originating channel (SignalR stream / WhatsApp send)
```

`PlaceholderAiProvider` (`Api.Infrastructure/AI`) implements `IAiProvider`
with a canned-but-context-aware reply — enough to prove the full round trip
end-to-end without an OpenAI key. Sprint 4 swaps in `OpenAiProvider` behind
the same interface; the registration in `DependencyInjection.cs` is the only
line that changes.

### ChatHub (`/hubs/chat`)

Anonymous (`[AllowAnonymous]`), keyed by the company's `PublicApiKey` rather
than a JWT. Protocol:

| Direction | Method | Purpose |
|---|---|---|
| Client → Server | `JoinCompanyGroup(publicKey)` | called once on connect |
| Client → Server | `SendMessage(publicKey, sessionId, text)` | send a customer message |
| Server → Client | `TypingStart` | AI is generating a reply |
| Server → Client | `ReceiveToken(token)` | one streamed word |
| Server → Client | `ReplyComplete({conversationId, fullText})` | stream finished |
| Server → Client | `Error(message)` | unrecoverable error for this send |

Streaming is word-by-word at 40ms/word — real token-level streaming arrives
in Sprint 4 once `OpenAiProvider` can stream from the OpenAI API directly;
for now `PlaceholderAiProvider` returns the full string and the hub itself
chunks it, which is enough to prove the widget's rendering and the
`ReceiveToken`/`ReplyComplete` contract both work.

### Widget (embeddable on any website)

- `web/public/widget-loader.js` — framework-free vanilla JS. Reads its own
  `<script>` tag's `data-key` and `src` origin, injects a floating chat
  bubble, and lazily creates an iframe pointed at
  `{origin}/widget/chat?key={publicKey}` on first click.
- `web/src/routes/widget/WidgetPage.tsx` — the actual chat UI, rendered
  inside that iframe. Deliberately mounted outside `ProtectedRoute` /
  `DashboardLayout` in `App.tsx` — there is no agent identity here, only an
  anonymous customer.
- `web/src/lib/useChatHub.ts` — the SignalR client hook. Generates and
  persists a stable per-browser `sessionId` in `localStorage` (this is the
  `CustomerId` that ends up on the `Conversation` row), handles
  reconnect/backoff, and assembles streamed tokens into message bubbles.
- `web/public/widget-test.html` — a deliberately plain, build-tool-free HTML
  page for manually verifying the embed works with zero dependency on this
  repo's own React/Vite stack (this is what a client's actual website looks
  like from the widget's point of view).

`ChannelsController.ConnectWebChat` now generates a script tag that points
at a real, working `widget-loader.js` (driven by the new `Widget:BaseUrl`
config key, defaulting to the local Vite dev server) instead of the
Sprint 2 placeholder CDN URL that didn't resolve to anything.

### CORS: two policies

Program.cs now registers `DashboardCorsPolicy` (origin-restricted, used for
everything by default via `app.UseCors(...)`) and `WidgetCorsPolicy`
(`SetIsOriginAllowed(_ => true)`, applied only to the `/hubs/chat` endpoint
via `.RequireCors(WidgetCorsPolicy)`). The widget is embedded on arbitrary
client websites, so origin-allowlisting isn't meaningful here — tenant
isolation comes from the public key lookup inside the hub, not from CORS.
Per-company origin validation (so a stolen public key can't be used from an
unexpected domain) is a Sprint 6 hardening task, noted inline in `Program.cs`.

### WhatsApp webhook: from echo to real AI pipeline

`WhatsAppWebhookController.Receive` now runs every inbound text message
through the full pipeline above instead of Sprint 2's echo stub. Each
message in a batch is processed independently (`ProcessSingleMessageAsync`)
so one failure (e.g. a WhatsApp send error) doesn't drop the others. The
controller still always returns `200 OK` to Meta regardless of internal
outcome, per Meta's retry/webhook-suspension behavior — unchanged from
Sprint 2.

### Dashboard: real analytics, conversation transcripts

- New `GET /api/analytics/summary` (`AnalyticsController`) — conversation
  counts by status, resolution rate, token usage vs. budget. Tenant-scoped
  for free via the global query filter (no explicit `CompanyId` predicate
  needed — this endpoint *does* run with a valid JWT/agent context).
- `OverviewPage.tsx` now renders that data live (polled every 15s) instead
  of static em-dashes.
- `ConversationsPage.tsx` gained a click-through transcript modal
  (`GET /api/conversations/{id}/messages`, already existed from Sprint 1/2 —
  just wasn't wired into the UI yet), polled every 3s so a tester can watch
  a real conversation come in live through the widget or WhatsApp.

### Sprint 3 checklist (from the Phase 1 deep-dive doc)

- [x] SignalR ChatHub — SendMessage, ReceiveToken, ReplyComplete events work
- [x] Widget loader.js embeds correctly on a plain HTML test page
- [x] Chat widget UI renders and messages display in real time
- [x] WhatsApp inbound → normalise → process → reply flow working end-to-end
- [x] Conversations and Messages saved correctly in DB
- [x] All channel webhook endpoints return 200 correctly

## Sprint 4 — RAG pipeline (real AI from your knowledge base)

**Goal: AI answers from the company's own uploaded knowledge, not a placeholder.**

### What changed

The DI registration in `DependencyInjection.cs` is the main change:

```diff
- services.AddScoped<IAiProvider, PlaceholderAiProvider>();
+ services.AddScoped<OpenAiProvider>();
+ services.AddScoped<IAiProvider>(sp     => sp.GetRequiredService<OpenAiProvider>());
+ services.AddScoped<IEmbeddingProvider>(sp => sp.GetRequiredService<OpenAiProvider>());
+ services.AddScoped<RagService>();
+ services.AddSingleton<SystemPromptBuilder>();
```

Everything else (`ConversationService`, `ChatHub`, `WhatsAppWebhookController`,
`AppDbContext`, `KnowledgeChunk` entity, all existing tests) is either unchanged
or backward-compatible additions.

### New components

**`OpenAiProvider`** (`Api.Infrastructure/AI/`)  
Implements both `IAiProvider` and `IEmbeddingProvider` via the OpenAI REST API
using a named `HttpClient` ("openai", registered in DI). Uses:
- `text-embedding-3-small` (1536 dims, configurable via `OpenAI:EmbeddingModel`)
- `gpt-4o-mini` (configurable via `OpenAI:ChatModel`)

Registered as a single scoped instance forwarded to both interfaces so only
one HTTP connection pool entry is used per request/hub invocation.

**`RagService`** (`Api.Infrastructure/AI/`)  
1. Early-exit if no knowledge chunks exist for the company (avoids a paid API
   call on fresh deployments).
2. Embeds the customer's query via `IEmbeddingProvider`.
3. Runs a pgvector cosine-distance nearest-neighbour query (raw SQL via
   `Database.SqlQuery<T>`) to retrieve the top 4 semantically similar chunks.
4. Returns them as `["[DocumentName]\nText", ...]` for the system prompt.

**Why raw SQL for the vector search?** `KnowledgeChunk.Embedding` is `float[]`
with a `HasConversion` to `Pgvector.Vector`. EF Core's LINQ translator cannot
apply the `CosineDistance()` extension method through a ValueConverter (the
method is registered for `Vector` operands, not `float[]` operands). Raw SQL
with a FormattableString is the reliable escape hatch — `{companyId}` and
`{topK}` are parameterized Npgsql parameters; the vector literal is inlined
from model-generated float values (no user input, no injection risk). A
`{vectorStr}::vector` cast converts the text parameter to pgvector inside
PostgreSQL before the `<=>` operator runs.

**`SystemPromptBuilder`** (`Api.Infrastructure/AI/`)  
Pure, stateless function (registered as singleton). Assembles the system
prompt from company name, brand voice, primary language, and retrieved chunks.
Enforces a strict "answer only from context / say you don't know / never
hallucinate" instruction set to keep AI replies grounded.

**`KnowledgeController`** (`Api/Controllers/`)  
Full CRUD for knowledge base entries:

| Method | Route | What it does |
|--------|-------|--------------|
| GET    | `/api/knowledge` | List all chunks for the company (tenant-filtered) |
| GET    | `/api/knowledge/{id}` | Single chunk with full text |
| POST   | `/api/knowledge` | Create chunk → embed → save (synchronous, ~300ms) |
| PUT    | `/api/knowledge/{id}` | Update; re-embeds only if text changed |
| DELETE | `/api/knowledge/{id}` | Hard-delete (vector index updated by Postgres) |

Embedding on create/update is synchronous in Sprint 4. Sprint 6 moves this to
a Hangfire background job for large-document ingestion.

### Updated pipeline (ChatHub + WhatsApp webhook)

```
inbound message
  → GetOrCreateConversation
  → AppendMessage (User)
  → GetRecentHistoryLines          (last 10 messages for GPT context)
  → RagService.RetrieveAsync ◄─── NEW: pgvector cosine search
  → IAiProvider.GenerateReplyAsync ◄─── NOW: GPT-4o-mini with grounded prompt
  → stream tokens / send reply
  → AppendMessage (Ai)
  → IncrementTokenUsageAsync ◄─── NEW: atomic UPDATE on Company.TokensUsedThisMonth
```

RAG is **non-fatal**: if the embedding call fails (no API key in dev, rate
limit, network error), both ChatHub and the WhatsApp webhook catch the
exception, log a warning, and proceed with empty `knowledgeChunks`. The AI
will correctly say it doesn't have that information — the user gets a
graceful response rather than a crash.

### Token tracking

`ConversationService.IncrementTokenUsageAsync` uses EF Core 8's
`ExecuteUpdateAsync` to issue a single `UPDATE Company SET TokensUsedThisMonth
= TokensUsedThisMonth + @tokens WHERE Id = @companyId` — an atomic increment
that's safe under concurrent conversation load without optimistic-concurrency
conflicts. The OverviewPage polls `/api/analytics/summary` every 15s and
updates the token usage bar chart in real time.

### No new DB migration required

The `KnowledgeChunks` table and the `vector(1536)` column were created in the
Sprint 1/2 initial migration. Sprint 4 only populates them.

**Production performance note**: once a company has > ~1 000 knowledge chunks,
add an HNSW index for sub-millisecond retrieval:
```sql
CREATE INDEX ON "KnowledgeChunks"
    USING hnsw ("Embedding" vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);
```
This can be run CONCURRENTLY on a live database with no downtime:
```sql
CREATE INDEX CONCURRENTLY ...
```

### Sprint 4 checklist

- [x] `OpenAiProvider` calls GPT-4o-mini and text-embedding-3-small correctly
- [x] `RagService` retrieves semantically relevant chunks via pgvector `<=>`
- [x] `SystemPromptBuilder` grounds the model in company context + chunks
- [x] `KnowledgeController` CRUD — create embeds immediately, update re-embeds on text change
- [x] `KnowledgeBasePage` UI — list / add / edit / delete entries
- [x] Token usage tracked atomically after every AI reply
- [x] RAG failure is non-fatal for both widget and WhatsApp
- [x] No new EF Core migration required (schema from Sprint 1/2)
- [x] `Sprint4Tests` — SystemPromptBuilder unit tests + PlaceholderAiProvider compat

> **Correction (added during the Sprint 5 pass):** the section above still
> describes `OpenAiProvider` because that's what this doc said when written.
> The actual running code was switched to `GroqChatProvider` (chat, via Groq's
> OpenAI-compatible endpoint, model `llama-3.3-70b-versatile`) and
> `CohereEmbeddingProvider` (embeddings, `embed-multilingual-v3.0`, 1024 dims)
> before Sprint 5 began — both are free-tier, which matters for a KES-priced
> SME product (see `docs/migrations/sprint5-tickets-email.sql`'s
> `cohere-1024-dim` companion migration and `DependencyInjection.cs`'s
> `// ---- Sprint 4: AI ----` block for the real registrations). Functionally
> equivalent to the narrative above — `IAiProvider` / `IEmbeddingProvider` are
> unchanged — just naming the real classes here so this doc stops lying about
> which HTTP APIs `RagService` actually calls.

## Sprint 5 — Ticket system + Email channel

**Goal: when the AI can't (or shouldn't) handle a conversation alone, a human
finds out — via a ticket, not by the customer giving up.**

### What was already there vs. what this pass added

Auditing the uploaded codebase against the Phase 1 deep-dive's Sprint 5
checklist found the backend already close to complete — `TicketService`,
`EscalationService`, `TicketsController`, `EmailWebhookController`,
`BrevoEmailClient`, `ChannelsController.ConnectEmail`, the
`ConversationService` email helpers, and the `sprint5-tickets-email.sql`
migration were all present, wired into `ChatHub` and
`WhatsAppWebhookController`, and internally consistent. This pass's job was
closing the real gaps rather than rebuilding what already worked:

- **Security**: `appsettings.json` (not gitignored — only
  `appsettings.Development.json`/`appsettings.Production.json` are) had live
  Groq, Cohere, and Brevo API keys and a real DB password committed in plain
  text. Moved the real values to a new, gitignored
  `appsettings.Development.json`; scrubbed `appsettings.json` back to
  placeholders. **Rotate all three keys** — treat anything that was ever
  committed as compromised, the same call made for the connection-string leak
  documented in the Sprint 1/2 closure section above.
- **Bug**: `ChannelsController.ToDto` only read the WhatsApp channel's
  `displayPhoneNumber` metadata key, so a connected Email channel always
  rendered with blank display info in `/api/channels` responses. Added the
  `displayEmail` branch.
- **Gap**: `Company.EscalationRulesJson` existed on the entity (and
  `EscalationService` already read it) but was never exposed through
  `CompanyDetailsDto` / `UpdateCompanyRequest` — there was no way to actually
  configure escalation rules short of writing raw SQL. Added the field to both,
  following the exact pattern already used for `BusinessHoursJson`.
- **Gap**: onboarding wizard's Step 4 (Connect a channel) had WhatsApp and Web
  Chat cards but no Email card, despite the Email channel's backend endpoint
  already existing. Added an `EmailCard` matching the existing cards'
  connect/status/copy-webhook-url pattern.
- **Gap**: no onboarding step for escalation rules at all (the deep-dive's
  "Step 6"). Added `Step5EscalationRules` as the wizard's new final step —
  Step 4 now advances to it instead of finishing onboarding directly, and Step
  5 owns the `POST /api/companies/me/onboarding/complete` call.
- **Gap**: `TicketsController.Assign` existed and worked, but the dashboard
  had no UI for it — tickets could only be created and replied to, never
  reassigned. Added an inline assign/reassign panel to the ticket detail
  modal, gated to Owner/Admin (matches the endpoint's `[Authorize]` policy).
- **Gap**: escalation rules were only reachable during onboarding, with no way
  to revisit them afterward. Added the same rules form as a persistent card on
  the Settings → Company tab, sharing one `EscalationRulesForm` component with
  the onboarding step so the two surfaces can't drift out of sync.
- **Gap**: no test coverage for either piece of new business logic. Added
  `Sprint5Tests` — see below.

### Architecture deviations from the deep-dive doc (intentional, pre-existing)

Two Phase 1 doc assumptions don't match the real implementation, and this
pass keeps the real implementation rather than "correcting" working code to
match a planning doc:

1. **Email is inbound-webhook-based (Brevo), not IMAP polling (MailKit).**
   `EmailWebhookController` receives Brevo's inbound-parse POST directly —
   no polling loop, no `BackgroundService`, no IMAP credentials to store or
   rotate. Threading uses the `In-Reply-To`/`References` headers Brevo passes
   through, matched against `Conversation.EmailMessageId`
   (`ConversationService.GetByEmailMessageIdAsync`). This is simpler and more
   reliable than IMAP for a company that doesn't want to hand over an inbox
   password, and Brevo's free tier covers send + inbound parsing in one
   provider — one fewer moving part for a KES-priced SME product.
2. **PostgreSQL/pgvector, not SQL Server**, per the Sprint 1/2 foundation —
   already the case before Sprint 5; noted here only because the roadmap
   slides still say SQL Server.

### New/changed pieces this pass touched

| File | Change |
|---|---|
| `appsettings.json` | Scrubbed live secrets to placeholders |
| `appsettings.Development.json` | New, gitignored — holds the real local dev keys |
| `ChannelsController.cs` | `ToDto` now reads `displayEmail` in addition to `displayPhoneNumber` |
| `CompanyContracts.cs` | `EscalationRulesJson` added to `CompanyDetailsDto` + `UpdateCompanyRequest` |
| `CompaniesController.cs` | Reads/writes `EscalationRulesJson`, same pattern as `BusinessHoursJson` |
| `Sprint5Tests.cs` | New — `EscalationService` rule-evaluation tests + `TicketService` numbering/escalation tests |
| `Step4ConnectChannel.tsx` | Added `EmailCard`; footer button now `Continue` (advances to Step 5) instead of finishing onboarding |
| `Step5EscalationRules.tsx` | New — onboarding wizard's final step, owns the finish-onboarding call |
| `EscalationRulesForm.tsx` | New shared component — used by both the onboarding step and Settings |
| `OnboardingWizardPage.tsx` | 5 steps instead of 4 |
| `TicketsPage.tsx` | Added `AssignPanel` (agent + team reassignment, Owner/Admin only) |
| `CompanySettingsTab.tsx` | Added a persistent Escalation Rules card |
| `types.ts` | `EscalationRules`, `ConnectEmailRequest/Response`, `AssignTicketRequest`, `escalationRulesJson` on `CompanyDetails` |

### Sprint 5 checklist

- [x] `Ticket` entity + migration (`sprint5-tickets-email.sql`)
- [x] `TicketService` — sequential per-company ticket numbers (Serializable
      transaction to avoid races), status transitions, agent replies routed
      back through the originating channel
- [x] `EscalationService` — confidence threshold, explicit agent-request
      keywords, opt-in payment-keyword rule, configurable via
      `Company.EscalationRulesJson`
- [x] Escalation wired into `ChatHub`, `WhatsAppWebhookController`, and
      `EmailWebhookController` — every channel can create a ticket, not just one
- [x] `TicketsController` — full CRUD, reply, assign, status transitions
- [x] Email channel: `ChannelsController.ConnectEmail`,
      `EmailWebhookController` (Brevo inbound parse), `BrevoEmailClient` (send
      + threading headers)
- [x] `TicketsPage` dashboard — filterable list, detail modal, reply,
      status transitions, **assignment** (this pass)
- [x] Onboarding: Email channel card (this pass), escalation rules step
      (this pass)
- [x] Settings: escalation rules persistently editable (this pass)
- [x] Live secrets removed from `appsettings.json` (this pass — rotate the
      keys)
- [x] `Sprint5Tests` — escalation rule evaluation + ticket numbering (this pass)

## Sprint 5 follow-up — web chat continuity + Channels settings

A second pass, prompted by real usage after the first: agent replies weren't
reaching the widget, chat history vanished on refresh, and a live Brevo 401
turned out to be a wrong-key-type mixup rather than a config bug.

### Bug: agent replies never reached the web chat widget

`TicketsController.Reply`'s `WebChat`/`MobileSdk` case did nothing but save
the message — a stale comment claimed the customer "polls via SignalR," but
no code ever pushed anything, and there was no per-conversation SignalR group
to push to in the first place (`ChatHub` only ever used `Clients.Caller`,
never `Clients.Group`). Fixed by:

- `ChatHub.ConversationGroupName(Guid conversationId)` — a shared, public
  static helper (`conversation:{id}`) so hub and controller can't drift on
  the naming scheme.
- `ChatHub.SendMessage` now joins the caller's connection to that group
  immediately after resolving the conversation.
- `TicketsController.Reply`'s `default:` case now broadcasts an `AgentReply`
  SignalR event to that group via an injected `IHubContext<ChatHub>`.
- `useChatHub.ts` listens for `AgentReply` and renders it as a distinct
  "Support agent" bubble (`WidgetMessage.role: 'agent'`).

### Bug: chat history vanished on refresh; escalated chats forked into orphaned new conversations

Two bugs with one root cause. `ConversationService.GetOrCreateAsync` only
reused a conversation when `Status == Open`. `TicketService.CreateAsync` sets
`Status = Escalated` when it raises a ticket — so the *very next* customer
message (including one sent right after a page refresh, since the widget had
no history-rehydration mechanism at all) matched nothing and silently forked
a brand-new, ticket-less conversation. That new conversation is also why
agent replies had "nowhere to land" even after the SignalR fix above — the
agent was replying to conversation A, but the widget had already moved on to
conversation B.

Fixed by:

- `ConversationService.GetOrCreateAsync` now reuses `Open` **or**
  `Escalated` conversations (not just `Open`).
- `ChatHub.SendMessage` short-circuits once `conversation.Status ==
  Escalated`: it appends the message and sends a plain "added to your open
  ticket" acknowledgement instead of re-running the full AI/RAG/escalation
  pipeline (which would otherwise spam a duplicate ticket every follow-up
  message while the customer waits for a human).
- `TicketsController.UpdateStatus` now calls
  `ConversationService.ResolveAsync` when a ticket becomes `Resolved` or
  `Closed`, so the conversation doesn't stay `Escalated` forever — the
  customer's next message correctly starts a clean new conversation the AI
  can help with again, rather than being permanently stuck saying "I've
  added that to your ticket" for a ticket that's long since closed.
- New `ChatHub.GetHistory(companyPublicKey, sessionId)` hub method: called
  once by the widget right after connecting. Finds the customer's most
  recent conversation via `ConversationService.GetMostRecentAsync` (any
  status — history should show even if the thread later got resolved),
  joins its SignalR group, and returns its
  `GetCustomerFacingMessagesAsync` result (User/Ai/Agent messages only —
  `System` messages are internal escalation notes for agents, never
  customer-facing). `useChatHub.ts` uses this to rehydrate `messages` and an
  `isEscalated` banner on connect/reconnect instead of always starting blank.

### Real-world bug: Brevo 401 "Key not found"

Not a code bug — a wrong-credential-type bug, but worth documenting because
it's an easy trap. Brevo issues two distinct credentials under **Settings →
SMTP & API**: an **API key** (`xkeysib-...`, for the REST API this codebase
calls) and a separate **SMTP key** (`xsmtpsib-...`, for raw SMTP relay auth,
which this codebase never uses). The key originally committed in
`appsettings.json` (see the security note earlier in this doc) was in fact an
`xsmtpsib-...` SMTP key — Brevo's REST API correctly returns 401 "Key not
found" for it, no matter how many times it's regenerated, because from the
API's point of view it was never a real API key. See the README's Sprint 5
testing section for the full explanation and the exact Brevo dashboard path.

### New: Settings → Channels tab

Onboarding's Step 4 only requires *one* active channel to finish — so before
this pass, there was no way to connect a second/third channel, rotate a
WhatsApp token, or change the support inbox after onboarding, short of
raw SQL. `ChannelsSettingsTab.tsx` adds a connected-channels list (status
pill, reconnect form, `POST /api/channels/{id}/disconnect` — that endpoint
already existed and worked, it just had no UI) plus a collapsible "How to
connect" guide per channel covering prerequisites, numbered steps, and the
gotchas above (Meta token expiry, the Brevo key-type mixup, sender
verification). Owner/Admin gated, matching the endpoints' `[Authorize]`
policies.

### New/changed pieces this pass touched

| File | Change |
|---|---|
| `ChatHub.cs` | `ConversationGroupName` helper, `GetHistory` hub method, conversation-group join in `SendMessage`, escalated-conversation short-circuit |
| `TicketsController.cs` | Injected `IHubContext<ChatHub>`; `Reply`'s `WebChat` case now broadcasts `AgentReply`; `UpdateStatus` resolves the conversation on ticket close |
| `ConversationService.cs` | `GetOrCreateAsync` reuses Escalated too; new `GetMostRecentAsync` + `GetCustomerFacingMessagesAsync` |
| `useChatHub.ts` | History rehydration on connect/reconnect, `AgentReply` listener, `isEscalated` state |
| `WidgetPage.tsx` | Escalated-ticket banner, distinct "Support agent" message bubble |
| `ChannelsSettingsTab.tsx` | New — connected-channels list, reconnect/disconnect, per-channel setup guides |
| `SettingsPage.tsx` | Registered the Channels tab |
| `Sprint5Tests.cs` | Added `ConversationService` continuity tests (escalated reuse, resolved-starts-fresh, history lookup, system-message exclusion) |

## Sprint 5 follow-up #2 — IMAP/SMTP as a free alternative to Brevo's inbound webhook

Brevo's free tier covers **sending** mail but not **receiving** it — inbound
parsing (what the Webhook mode above depends on) requires a paid plan. Rather
than force that cost, the Email channel now supports two inbound modes,
client's choice — matching the Phase 1 deep-dive doc's original MailKit/IMAP
design intent, which the first Sprint 5 pass had deviated from in favor of
Brevo-only (see the "Architecture deviations" note above, now partially
superseded by this).

### Design

- **`EmailChannelMetadata`** (`Api.Infrastructure.Services`): the single
  source of truth for reading/writing the Email connection's `MetadataJson`
  shape (`{ inboundMode, displayEmail, senderEmail, senderName }`) and
  decrypting the right credentials type. `ChannelsController`,
  `EmailWebhookController`, `EmailPipelineService`, `ImapPollingService`, and
  `TicketsController` all go through this rather than each parsing the JSON
  their own way — the exact kind of drift that caused the display-info bug
  fixed in the first Sprint 5 pass.
- **`EmailPipelineService`**: the inbound-email business logic (thread
  lookup, save, RAG, AI reply, escalation, send) extracted out of
  `EmailWebhookController` so `ImapPollingService` doesn't duplicate it.
  Picks Brevo or SMTP for the outbound send based on
  `EmailChannelMetadata.ReadMode`.
- **`ImapSmtpEmailClient`** (`IImapSmtpEmailClient`): MailKit wrapper.
  `VerifyAsync` connects + authenticates both IMAP and SMTP before
  `ChannelsController.ConnectEmail` saves anything — mirrors
  `WhatsAppClient.VerifyAsync`'s verify-before-save pattern, so a typo'd
  password fails immediately instead of silently breaking the poller later.
  `SendAsync` sends a reply over SMTP with proper `In-Reply-To`/`References`
  threading headers.
- **`ImapMailFetcher`**: fetches unread mail via `SearchQuery.NotSeen` and
  marks messages `\Seen` after processing. Deliberately *not* UID-range
  tracking — simpler, self-correcting, and avoids UIDVALIDITY edge cases —
  at the cost of assuming the mailbox is dedicated to this integration (not
  also read by a human, who could mark something seen before this service
  processes it).

  **Fix (caught in real testing):** `NotSeen` alone pulled in a mailbox's
  entire pre-existing unread backlog — old newsletters, notifications, mail
  from months or years ago that was simply never marked read — as brand-new
  conversations the instant a company connected. Fixed with a
  `processingStartedAtUtc` cutoff, set to "now" at connect time (and reset on
  every reconnect) and stored in `MetadataJson`. `FetchUnseenAsync` combines
  `SearchQuery.NotSeen.And(SearchQuery.DeliveredAfter(...))` as a coarse,
  cheap server-side filter (IMAP's `SINCE` is date-only, no time component —
  MailKit's own docs and FAQ note it isn't perfectly reliable across every
  server implementation either), then enforces the exact cutoff in C# against
  each message's real `Date` header — belt and suspenders. Legacy Imap
  connections made before this fix (no `processingStartedAtUtc` in their
  metadata) get it back-filled to "now" the first time `ImapPollingService`
  polls them post-upgrade, so nobody's backlog floods in on the next
  deploy either.
- **`ImapPollingService`** (`BackgroundService`): polls every company's
  Imap-mode Email connection every `Email:PollingIntervalSeconds` (default
  30s, 10s floor). One loop for every company rather than one
  `BackgroundService` instance per company — cheap at scale, and a bad
  password on one company's mailbox is caught and logged without affecting
  anyone else's polling.
- **`ChannelsController.ConnectEmail`**: now takes a `Mode` field
  (`"Webhook"` | `"Imap"`). Imap mode is verified synchronously before
  saving; Webhook mode has nothing to verify synchronously (Brevo will just
  start POSTing once the agent wires up the returned webhook URL).
- **`ChannelConnectionDto.InboundMode`**: new field so the frontend can tell
  Webhook and Imap connections apart after a page reload, instead of
  inferring it from local component state (which was wrong for a reloaded
  Imap-mode connection in an earlier draft of this change — worth naming
  since it's the kind of bug that's easy to reintroduce).

### Bug caught during this pass: agent email replies were not mode-aware

`TicketsController.SendEmailReplyAsync` (used when an agent replies to an
email-channel ticket from the dashboard) always deserialized the connection's
encrypted credentials as `EmailChannelCredentials` (the Webhook-mode shape)
and always sent via `IBrevoEmailClient`. For an Imap-mode connection, that
credentials blob is actually `ImapChannelCredentials` — a different shape —
so deserializing it as `EmailChannelCredentials` wouldn't throw (System.Text.Json
just leaves unmatched properties at their default), it would silently produce
an outbound email with an empty sender address. Fixed by routing through
`EmailChannelMetadata`/`IImapSmtpEmailClient` the same way
`EmailPipelineService` does.

### Backward compatibility note

Email connections made **before** this pass only ever stored
`{ displayEmail }` in `MetadataJson` — no `inboundMode`, `senderEmail`, or
`senderName`. `EmailChannelMetadata.ReadMode` defaults to `Webhook` for these
(their actual historical behavior), but `ReadSenderEmail`/`ReadSenderName`
return `null`, which makes `TicketsController.SendEmailReplyAsync` bail out
rather than send with an empty sender. **Any company that connected email
before this update needs to disconnect and reconnect it once** to populate
the new metadata fields. No data migration was written for this — it's a
one-time, one-click fix per affected company, and this codebase has no
production traffic yet to migrate around.

### New/changed pieces this pass touched

| File | Change |
|---|---|
| `Api.Infrastructure.csproj` | Added `MailKit` package reference |
| `ImapSmtpEmailClient.cs` | New — `ImapChannelCredentials`, `IImapSmtpEmailClient`/`ImapSmtpEmailClient` (verify + SMTP send), `ImapMailFetcher` (IMAP fetch) |
| `EmailPipelineService.cs` | New — shared inbound pipeline; also defines `EmailChannelMetadata` |
| `ImapPollingService.cs` | New `BackgroundService` — polls Imap-mode connections |
| `EmailWebhookController.cs` | Slimmed to delegate to `EmailPipelineService`; ignores hits for non-Webhook-mode connections |
| `ChannelsController.cs` | `ConnectEmail` supports both modes with Imap verify-before-save; `ToDto` exposes `InboundMode` |
| `TicketsController.cs` | `SendEmailReplyAsync` is now mode-aware (bug fix, see above) |
| `ChannelContracts.cs` | `ConnectEmailRequest`/`Response` extended for both modes; `ChannelConnectionDto.InboundMode` added |
| `DependencyInjection.cs` | Registered `IImapSmtpEmailClient`, `ImapMailFetcher`, `EmailPipelineService`, `ImapPollingService` |
| `appsettings.json` | Added `Email:PollingIntervalSeconds` |
| `EmailChannelCard.tsx` | New shared component (replaces duplicate `EmailCard` in onboarding + Settings) — mode picker, provider presets (Gmail/Outlook/Zoho/Custom), dual guides |
| `Step4ConnectChannel.tsx`, `ChannelsSettingsTab.tsx` | Use the shared `EmailChannelCard` |
| `types.ts` | `ChannelConnection.inboundMode`; `ConnectEmailRequest`/`Response` extended |
| `Sprint5Tests.cs` | Added `EmailChannelMetadata` mode-routing tests (Webhook/Imap detection, legacy-connection fallback, malformed-JSON fallback) |

## Sprint 6 — Messenger, Telegram, Sandbox Mode

**Goal: two more channels, and a safe place to try the AI before real customers do.**

Unlike Sprint 5, almost none of this existed yet — only the `ChannelType.Messenger`/`ChannelType.Telegram` enum values were reserved from Sprint 1. Everything else (clients, webhooks, connect endpoints, sandbox mode, the wizard's final step) is new this sprint.

### Messenger

Same Meta Graph API family as WhatsApp, different payload shape and endpoint
(`/v19.0/me/messages` vs WhatsApp's phone-number-scoped endpoint).
`MessengerClient` mirrors `WhatsAppClient`'s structure closely on purpose —
verify (GET the Page, confirms the token + page id are both real) then send.

**Deviation from the Phase 1 doc (matching an existing precedent):** the doc
describes a "Connect with Facebook" OAuth redirect. WhatsApp — the other Meta
channel already built — already doesn't do that either; it uses a simpler
"paste your access token, we verify it" flow, because OAuth needs a fully
registered Meta App with a public redirect URI and app secret, neither of
which exist in this environment. Messenger follows that same established
pattern rather than introducing a second, inconsistent connection UX for the
one channel that would have real OAuth.

`MessengerWebhookController` has the same GET-verification-challenge /
POST-inbound shape as `WhatsAppWebhookController`, but delegates the actual
AI/RAG/escalation pipeline to `ChatChannelPipelineService` (see below) instead
of duplicating it a second time.

### Telegram

Simplest channel to set up by far — no dashboard to paste a URL into
anywhere. `ConnectTelegram` verifies the bot token (`getMe`) and immediately
calls Telegram's `setWebhook` itself, so the bot works the instant the
connect call succeeds. Pure REST via a plain `HttpClient` — Telegram's Bot
API needs no SDK.

`TelegramWebhookController` has no GET verification step (Telegram doesn't
have one — it just starts POSTing once `setWebhook` succeeds), and also
delegates to `ChatChannelPipelineService`.

**Known gap, consistent with WhatsApp/Messenger:** none of the three
webhook endpoints cryptographically verify inbound requests (Meta's
`X-Hub-Signature-256`, Telegram's `secret_token` header). Real, worth fixing,
but it's a Sprint 8 security-review item, not a Sprint 6 one — matching
WhatsApp's existing, already-shipped state.

### `ChatChannelPipelineService` — shared AI pipeline for Messenger + Telegram

`WhatsAppWebhookController.ProcessMessageAsync` has an inline pipeline
(thread lookup → save → RAG → AI → escalation → ticket → reply). Rather than
copy that a second and third time for Messenger and Telegram,
`ChatChannelPipelineService` factors it into one shared service, parameterized
by a `sendReplyAsync` delegate so it stays completely channel-agnostic — each
webhook controller already needs its own client and decrypted credentials to
process the inbound payload, so it's simplest for it to also own "how do I
reply," rather than adding a third sender-interface hierarchy alongside
`IBrevoEmailClient`/`IImapSmtpEmailClient` from Sprint 5.

**`WhatsAppWebhookController` was deliberately left untouched** rather than
refactored to also use this shared service — it already works, and this pass
prioritized not risking a regression in already-shipped code over saving a
few dozen duplicated lines.

### Sandbox mode

Every company gets a private, freely-shareable test link
(`platform.com/test/{sandboxToken}`) that behaves exactly like their real web
chat widget — literally the same `ChatPanel` component, same `ChatHub` — with
one difference: `ChatHub.ResolveCompanyAsync` tries `PublicApiKey` first,
then falls back to `SandboxToken`, and whichever one matched determines
`Conversation.IsSandbox` for any conversation created through it. From there:

- `ChatHub.SendMessage` skips `TicketService.CreateAsync` for sandbox
  conversations and instead sends back an explanatory message ("this would
  escalate to a ticket in production, reason: X — no ticket was actually
  created") — informative without touching real ticket data.
- `ChatHub.SendMessage` skips `ConversationService.IncrementTokenUsageAsync`
  for sandbox conversations — the Groq call itself still costs real tokens,
  but the company's billed/budgeted usage is never charged for it.
- Because no ticket is ever created, a sandbox conversation's `Status` never
  transitions to `Escalated` — every sandbox message gets the full AI
  treatment, repeatedly, which is exactly the point of a test chat.

`SandboxController` exposes `GET /api/sandbox/info` (dashboard: the
company's own test link) and `POST /api/sandbox/regenerate` (rotates it —
old links stop working immediately), plus a public, unauthenticated
`GET /api/sandbox/{token}/company` the test page itself uses to show which
company it's testing and fail fast with a clear message if the token was
rotated or mistyped.

The sandbox chat appears in three places, all sharing the same `ChatPanel`
component: the public `/test/{token}` page, a permanent dashboard **Sandbox**
page (for testing after onboarding — e.g. after editing the knowledge base),
and inline in the onboarding wizard's new final step.

**Edge case handled:** the sandbox page and the widget page each use their
own `localStorage` key for the session id (`asp_sandbox_session_id` /
`asp_dashboard_sandbox_session_id` vs `asp_widget_session_id`). Without that
separation, a business owner testing their own sandbox in the same browser
they use to view their live site could accidentally share one conversation
between sandbox and production, permanently locking its `IsSandbox` flag to
whichever context created it first.

### Onboarding: Step 6 (Test & Go Live)

The wizard's new final step (the Phase 1 doc's "Step 7" — numbered 6 here,
same reason Step 5 isn't "Step 6": no separate knowledge-base wizard step in
this codebase). Shows the company's own sandbox chat inline plus the
shareable test link, and moves the finish-onboarding call here from Step 5 —
a company can now actually try their AI before committing to going live,
rather than connecting channels blind. Step 5 (escalation rules) now just
"Continue"s to this step instead of finishing directly.

### New/changed pieces this sprint

| File | Change |
|---|---|
| `Company.cs`, `Conversation.cs` | `SandboxToken`, `IsSandbox` |
| `sprint6-messenger-telegram-sandbox.sql` | New migration — additive, includes backfill for existing companies |
| `MessengerClient.cs`, `TelegramClient.cs` | New — verify + send, mirroring `WhatsAppClient`'s shape |
| `ChatChannelPipelineService.cs` | New — shared inbound pipeline for Messenger + Telegram |
| `MessengerWebhookController.cs`, `TelegramWebhookController.cs` | New |
| `ChannelsController.cs` | `ConnectMessenger`, `ConnectTelegram`; `ToDto` handles their metadata keys |
| `SandboxController.cs` | New |
| `ConversationService.GetOrCreateAsync` | New `isSandbox` parameter (defaults `false`, existing callers unaffected) |
| `ChatHub.cs` | `ResolveCompanyAsync` (PublicApiKey or SandboxToken); sandbox-aware ticket/token-usage skip |
| `AuthController.cs` | Sets `SandboxToken` at registration |
| `ChatPanel.tsx` | New shared chat UI (widget + sandbox both use it) |
| `SandboxChatPage.tsx`, `SandboxPage.tsx` | New — public test page, dashboard test page |
| `MessengerChannelCard.tsx`, `TelegramChannelCard.tsx`, `channels/shared.tsx` | New shared channel-card components + extracted `StatusPill`/`GuideDisclosure`/`GuideStep` (now used by 5 channel cards instead of being duplicated in 3 files) |
| `Step5EscalationRules.tsx` | Now "Continue"s to Step 6 instead of finishing |
| `Step6TestAndGoLive.tsx` | New — final wizard step |
| `Sprint6Tests.cs` | Sandbox-flag behavior on `ConversationService.GetOrCreateAsync` |

### What wasn't tested here (and why)

`ChatChannelPipelineService`, `MessengerClient`, and `TelegramClient` have no
direct unit tests — they need either real HTTP calls or a mocking framework
this project doesn't reference (no Moq/NSubstitute; same reasoning
`Sprint4Tests` used for not testing `GroqChatProvider`/`CohereEmbeddingProvider`
directly). They got careful manual review instead. The sandbox-flag logic in
`ConversationService` — the one piece cheap to test with the existing
InMemory-DbContext pattern, and the one most load-bearing for correctness —
is fully covered.

## Sprint 7 — Analytics + Billing (M-Pesa)

**Goal: a company can see how their AI is doing, and pay for it.**

### Analytics

Three new endpoints alongside the existing `summary`:
`conversations-over-time`, `channel-breakdown`, `top-questions`. All three are
read-only SQL aggregation over tables that already existed — no schema
changes needed for analytics at all.

**Deliberate simplification — "top questions" is exact-match, not topic
clustering.** It groups each conversation's first customer message by exact
(lowercased, trimmed) text and ranks by frequency. "What are your hours?" and
"what time do you open" count as two different questions, since there's no
semantic grouping. Real topic clustering would need embeddings-based
similarity (comparing message vectors the way `RagService` already compares
`KnowledgeChunk` vectors) — a meaningfully bigger feature than a Sprint 7
analytics endpoint. Exact-match frequency still surfaces genuinely repeated
questions, which covers the common real case, just not paraphrases.

**CSAT is a placeholder**, per the Phase 1 doc's own framing. There's no
mechanism anywhere in this codebase for a customer to rate a conversation —
`AnalyticsSummaryDto.CsatScore` is always `null`, and the dashboard shows
"Coming soon" rather than a fabricated number.

**No new charting library.** `web/package.json` had no charting dependency
before this sprint, and recharts (the obvious choice, and what Claude's own
artifact environment happens to have available) has had spotty React 19
peer-dependency support in some versions — a real risk of breaking `npm
install` for something a dozen lines of custom SVG renders just as well for
this dashboard's actual needs (a bar chart and some ranked lists, not
interactive data exploration). `SimpleBarChart`/`HorizontalBarList` are
dependency-free.

### Token budget enforcement (90% warning, 100% cutoff)

The Phase 1 doc frames this as "a background job sums usage" — but this
codebase already tracks usage atomically in real time
(`ConversationService.IncrementTokenUsageAsync`, an `ExecuteUpdateAsync` SQL
increment, since Sprint 4/5), so there's nothing to batch-sum. Both checks
are real-time instead:

- **`TokenBudgetService.IsOverBudgetAsync`** — called by *every* channel's
  pipeline (`ChatHub`, `WhatsAppWebhookController`, `EmailPipelineService`,
  `ChatChannelPipelineService`) right after saving the inbound message, before
  RAG/AI runs. If over budget: skip the AI call entirely, create a ticket
  directly (priority Medium, reason "Monthly AI token budget exceeded" — no
  AI confidence to run through `EscalationService`, since no AI reply was
  generated), and send the Phase 1 doc's exact customer-facing copy. **Sandbox
  conversations are exempt** — sandbox never counted against the budget in
  the first place (Sprint 6), so a company maxed out on their real plan can
  still test their AI to help decide whether to upgrade.
- **`TokenBudgetService.CheckAndSendBudgetWarningIfNeededAsync`** — called
  right after `IncrementTokenUsageAsync` in the same four places. Sends one
  email to every Owner/Admin agent the first time usage crosses 90% of
  budget, using an atomic `ExecuteUpdateAsync` compare-and-set on
  `Company.TokenBudgetWarningSentAt` (`WHERE TokenBudgetWarningSentAt IS
  NULL`) so concurrent replies across channels can't send two warnings for
  the same crossing — only the request that actually flips the flag from
  null sends.

### Billing period reset

`BillingPeriodResetService` (a plain `BackgroundService`, same pattern as
`ImapPollingService`) rolls a company's usage period over once 30 days have
passed since `Company.CurrentPeriodStartAt`, resetting
`TokensUsedThisMonth` to 0 and clearing `TokenBudgetWarningSentAt`.

**Rolling 30-day window, not a calendar billing date.** The Phase 1 doc
implies a per-company "billing date" (day-of-month based). A real
implementation of that needs 28-vs-31-day edge cases and a concept of
"which day of the month is this company's billing day" that doesn't exist
anywhere else in this schema. A 30-day window anchored to each company's own
`CurrentPeriodStartAt` (set at signup, and reset again on every successful
M-Pesa payment — see below) is simpler and behaves the same from the
company's perspective either way.

### M-Pesa (Daraja) billing

**No `BillingPlans` database table.** The Phase 1 doc calls for one, but
three fixed tiers that only ever change via a code deploy don't need a
database row each — `BillingPlanCatalog` is a static, strongly-typed
dictionary that's the single source of truth for the pricing page, the
in-dashboard Billing tab, and the M-Pesa amount calculation alike. Token
budgets per tier are this codebase's own invention (the doc only specifies
conversation-count limits, not tokens, since it doesn't know this codebase
enforces via tokens) — Starter's 100,000 matches `Company.MonthlyTokenBudget`'s
existing C# default exactly, so a brand-new signup needs no data migration to
already match the Starter catalog entry (see `Sprint7Tests`'s test for this).

**`MpesaClient`** — pure REST via a plain `HttpClient`, no SDK, per the doc.
OAuth (client-credentials, Basic Auth) then STK Push. Sandbox by default
(`Mpesa:Environment`); the sandbox shortcode (174379) and passkey baked into
`appsettings.json` are Safaricom's own published, public sandbox test
values — safe to commit, not secrets (verified against Safaricom's own Daraja
documentation) — production values are unique per business and must be set
separately.

**`BillingController`** — `GetPlans` (public, powers the pricing page),
`GetInfo` (current plan/usage), `InitiateMpesaPayment` (sends the STK push,
returns immediately — Daraja's HTTP response only confirms *the push was
sent*, not that payment succeeded), `GetTransactionStatus` (the dashboard
polls this after initiating), `MpesaCallback` (public — Safaricom's async
result some seconds later, once the customer enters or ignores their PIN).

**Idempotency**: `MpesaCallback` only acts once per transaction
(`if (transaction.Status != Pending) return Ok();`) — Safaricom can retry a
callback delivery, and without this guard a retried callback would re-apply
the plan change and send a second receipt email.

**On success**: `Company.Plan`, `MonthlyTokenBudget` (from the catalog), and
`CurrentPeriodStartAt`/`TokensUsedThisMonth`/`TokenBudgetWarningSentAt` all
update together — a plan change resets the usage clock, same as
`BillingPeriodResetService`'s natural rollover — and a receipt email goes to
the company's Owner via the same "AI Support Platform / noreply@..." sender
pattern `TicketService` already established for internal notifications.

### New/changed pieces this sprint

| File | Change |
|---|---|
| `Company.cs` | `CurrentPeriodStartAt`, `TokenBudgetWarningSentAt`, `BillingPhoneNumber` |
| `MpesaTransaction.cs` | New entity |
| `sprint7-analytics-billing.sql` | New migration |
| `BillingPlanCatalog.cs` | New — static plan catalog |
| `MpesaClient.cs` | New — Daraja OAuth + STK Push |
| `MpesaWebhookContracts.cs` | New — Daraja's callback payload shape |
| `BillingController.cs` | New — plans, info, initiate, status, callback |
| `TokenBudgetService.cs` | New — over-budget check + 90% warning, shared by all 4 channel pipelines |
| `BillingPeriodResetService.cs` | New background service |
| `ChatHub.cs`, `WhatsAppWebhookController.cs`, `EmailPipelineService.cs`, `ChatChannelPipelineService.cs` | Budget cutoff + warning check added to each |
| `AnalyticsController.cs` | `conversations-over-time`, `channel-breakdown`, `top-questions` |
| `SimpleBarChart.tsx` | New — dependency-free chart components |
| `OverviewPage.tsx` | Volume chart, channel breakdown, top questions, CSAT placeholder |
| `BillingSettingsTab.tsx` | New Settings tab — plan, usage, M-Pesa upgrade flow with status polling |
| `PricingPage.tsx` | New — public pricing page |
| `Sprint7Tests.cs` | `TokenBudgetService` (over-budget, 90% warning incl. once-per-period + owner/admin-only), `BillingPlanCatalog` sanity |

### What wasn't tested here (and why)

`MpesaClient` (real HTTP to Safaricom), `BillingController`'s M-Pesa
initiate/callback flow (needs a real or mocked `IMpesaClient` — no
Moq/NSubstitute in this project), and the analytics endpoints' SQL
aggregation (meaningful coverage would need seeding a much larger dataset
than a unit test reasonably should) all got careful manual review instead,
consistent with Sprint 6's `ChatChannelPipelineService`/channel-client
reasoning. `TokenBudgetService` — the piece most load-bearing for
correctness, and the one cheap to test with the existing InMemory pattern —
is fully covered, including the once-per-period and owner/admin-only
behaviors.


## Sprint 7 follow-up — real CSAT + a genuinely advanced analytics dashboard

The first Sprint 7 pass shipped CSAT as a documented placeholder (there was
no rating mechanism anywhere), and analytics as a handful of endpoints
folded into the Overview page. This pass builds CSAT for real and gives
analytics its own dedicated, much deeper dashboard — going beyond the Phase
1 doc's own spec, which only ever asked for a placeholder.

### Real CSAT

- **`Message.TokensUsed`** (new column): per-AI-reply token cost. Added
  specifically so analytics could show a *daily* token-usage trend instead
  of only `Company.TokensUsedThisMonth`'s single cumulative number —
  `Company.TokensUsedThisMonth` remains the fast, atomic figure budget
  enforcement actually checks against; this is additive detail for
  reporting, not a replacement. Threaded through all four channel
  pipelines' `AppendMessageAsync` calls for the AI's reply.
- **`Conversation.CsatScore` / `CsatSubmittedAt`**: one 1-5 rating per
  conversation. `ConversationService.SubmitCsatRatingAsync` uses an atomic
  `ExecuteUpdateAsync` compare-and-set (`WHERE CsatSubmittedAt IS NULL`) so
  a double-submit or retried request can't silently overwrite an earlier
  rating with a different one — first submission wins, matching how most
  chat products treat a rating as a one-time close-out action rather than
  an editable field.
- **`ChatHub.SubmitCsatRating`**: a new hub method the widget calls
  directly — no REST endpoint, since the widget already has a live
  SignalR connection and the conversation id from `GetHistory`/
  `ReplyComplete`. No auth: the customer isn't a logged-in agent, and a
  conversation id (an unguessable GUID they can only have learned from
  their own session) is enough to identify what to rate.
- **`ChatPanel`'s `CsatBar`**: shown once a conversation has more than one
  exchange, dismissible, never blocking — a rating prompt that gates the
  conversation would be a worse product than no rating at all. Once
  submitted (or already rated in an earlier session), shows a small
  non-interactive star confirmation instead of the interactive row.

### A genuinely advanced Analytics dashboard, not a placeholder-adjacent afterthought

`OverviewPage` stays a fast, focused landing page (four KPI cards, a token
usage bar, a link out) — everything below now lives at a **new, dedicated
`/analytics` page**, so the page every agent lands on at login doesn't pay
for seven charts' worth of queries it may not need.

- **Date range selector** (7/14/30/90 days) — every endpoint takes a `days`
  query parameter now, not a hardcoded 14.
- **Trend deltas** on every KPI — conversations, AI containment rate, avg
  first response time, all compared against the immediately preceding
  window of the same length. `TrendBadge` flips its color semantics per
  metric (`higherIsBetter`) since a rising response time is bad, not good.
- **AI containment rate** — % of conversations the AI resolved without ever
  escalating to a human. A standard support-ops "deflection" metric this
  codebase didn't have any version of before.
- **Avg first response time** — time from conversation creation to the
  first Ai-or-Agent message, computed via a grouped subquery per
  conversation rather than N+1 queries.
- **Escalation reason breakdown** — `Ticket.EscalationReason`'s free-text
  strings (from `EscalationService`) bucketed into stable categories ("Low
  AI confidence", "Customer requested a human", "Payment-related", "Budget
  exceeded", "Other") for a chart — the exact confidence score in a reason
  like "Low AI confidence (0.42)" isn't the useful signal, the pattern is.
- **Token usage trend** — real daily bars from the new `Message.TokensUsed`
  column, not a single cumulative number.
- **CSAT section** — average score, a 1-5 star distribution, and a daily
  trend, all from real submitted ratings.
- **Sandbox conversations are now excluded from every single query in this
  controller.** This wasn't true before this pass — every metric would
  have quietly counted a company's own testing traffic (Sprint 6) as if it
  were real customers, for every company, since every company is expected
  to test their AI at some point. This is arguably the most important fix
  in this pass: "advanced insights" are worthless if the underlying numbers
  are already wrong.

**Still no charting library** — `SimpleBarChart`/`HorizontalBarList`
(Sprint 7's original dependency-free choice) cover every new chart here
too, including the token-usage trend and CSAT distribution. `TrendBadge` is
the one new small shared component (up/down arrow + %, color-aware).

### New/changed pieces this pass touched

| File | Change |
|---|---|
| `Message.cs` | `TokensUsed` |
| `Conversation.cs` | `CsatScore`, `CsatSubmittedAt` |
| `sprint7-csat-token-tracking.sql` | New migration |
| `ConversationConfiguration.cs` | CSAT check constraint + index |
| `ConversationService.cs` | `AppendMessageAsync` takes `tokensUsed`; new `SubmitCsatRatingAsync` |
| `ChatHub.cs`, `WhatsAppWebhookController.cs`, `EmailPipelineService.cs`, `ChatChannelPipelineService.cs` | Pass `tokensUsed` through to `AppendMessageAsync`; `ChatHub` gets `SubmitCsatRating` |
| `AnalyticsController.cs` | Full rewrite — `days` param everywhere, trend deltas, containment rate, response times, `escalation-reasons`, `token-usage-over-time`, `csat`; sandbox exclusion |
| `AnalyticsContracts.cs` | Extended `AnalyticsSummaryDto`; new `EscalationReasonBreakdownDto`, `DailyTokenUsageDto`, `CsatSummaryDto`/`CsatDistributionBucketDto`/`CsatTrendPointDto` |
| `ChatPanel.tsx`, `useChatHub.ts` | CSAT rating bar + submission |
| `AnalyticsPage.tsx` | New — the dedicated deep-dive dashboard |
| `OverviewPage.tsx` | Simplified back to a fast landing page, trend badges added |
| `TrendBadge.tsx` | New shared component |
| `Sidebar.tsx` | Added Analytics nav entry |
| `Sprint7Tests.cs` | Added `SubmitCsatRatingAsync` tests (accept, reject-resubmit, score-range validation, nonexistent conversation) |

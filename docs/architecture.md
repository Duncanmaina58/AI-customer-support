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

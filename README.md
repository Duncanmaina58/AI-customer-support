# AI Customer Support Platform

Monorepo for the AI Customer Support Automation Platform (Kenya/East Africa-focused).

```
.
├── api/        C# .NET 8 Web API (backend)
├── web/        React + TypeScript dashboard & chat widget (frontend)
├── docs/       Architecture notes, ADRs
└── .github/    CI workflows
```

## Quick start

> **⚠️ Security note (read this first if you're picking this repo back up):**
> `appsettings.json` previously had live Groq, Cohere, and Brevo API keys and a
> real DB password committed in plain text — it's not gitignored (only
> `appsettings.Development.json`/`appsettings.Production.json` are). Those have
> now been scrubbed to placeholders and the real dev values moved to a new,
> gitignored `appsettings.Development.json`. **Rotate all three API keys** —
> anything that was ever committed should be treated as compromised, whether or
> not this particular repo copy is public.

### Backend (`/api`)

```bash
cd api
dotnet restore

# One-time: install the EF Core CLI tool if you don't already have it
dotnet tool install --global dotnet-ef

# Create the initial migration (reads your model, no DB connection needed)
dotnet ef migrations add InitialCreate --project src/Api.Infrastructure --startup-project src/Api -o Persistence/Migrations

# Apply it - creates the database + tables if they don't exist yet.
# Uses ConnectionStrings:Default from api/src/Api/appsettings.Development.json.
# If that file isn't picking up correctly for the `dotnet ef` CLI, override
# explicitly: $env:DESIGN_TIME_CONNECTION_STRING = "Host=localhost;Port=5432;Database=ai_support_platform;Username=postgres;Password=YOUR_PASSWORD"
dotnet ef database update --project src/Api.Infrastructure --startup-project src/Api

dotnet run --project src/Api
```

API will be available at `http://localhost:5080` (Swagger UI at `/swagger`).

> **NuGet access required.** `dotnet restore` needs to reach `api.nuget.org`. If you're
> running this inside a network-restricted sandbox, do this step on a machine/CI with
> normal internet access.

> **Postgres required**, with the `vector` extension available (pgvector). Easiest path:
> `docker compose up -d` from the repo root, which uses the `pgvector/pgvector:pg16`
> image — extension included. The `Pgvector.EntityFrameworkCore` package's `UseVector()`
> call (see `DependencyInjection.cs`) should make the first migration include a
> `CREATE EXTENSION IF NOT EXISTS vector` step automatically — if it doesn't, run that
> SQL manually against your database once before `dotnet ef database update`.

> **Already have a migration applied from before?** The model picked up new tables
> (`RefreshTokens`, `ChannelConnections`) and `Company` columns since the last update —
> see `docs/architecture.md`'s "Migration note" for how to bring an existing dev
> database up to date (or just drop and recreate it, simplest if there's no real data
> in it yet).

> **Connecting WhatsApp?** Set `WhatsApp:VerifyToken` in `appsettings.Development.json`
> to a secret you make up, enter the same value in your Meta App's webhook config, and
> see `docs/architecture.md` for the ngrok setup needed to test it locally.

> **Web chat widget (Sprint 3)?** `Widget:BaseUrl` in `appsettings.json` controls what
> origin the generated embed script tag points at — defaults to
> `http://localhost:5173` (the local Vite dev server), so the "Activate" button in the
> onboarding wizard's Web Chat card works out of the box in local dev. Set it to your
> deployed frontend's URL in production.

### Frontend (`/web`)

```bash
cd web
npm install
npm run dev
```

Dashboard will be available at `http://localhost:5173`.

### Testing the web chat widget end-to-end

1. Register a company at `/register`, complete onboarding, and activate **Web Chat**
   in Step 4 — this generates an embed script tag and copies your `publicApiKey`.
2. Open `http://localhost:5173/widget-test.html` directly in a browser — a deliberately
   plain, framework-free HTML page standing in for a client's own website.
3. Edit the `data-key` attribute in that file to your company's `publicApiKey`, reload,
   and click the chat bubble in the bottom-right corner.
4. Send a message — you should see it appear, a typing indicator, then a streamed AI
   reply (from `PlaceholderAiProvider` until Sprint 4 wires up real RAG).
5. Check the dashboard's **Conversations** page — the conversation should appear
   (polls every 8s) and clicking it shows the live transcript (polls every 3s).

## Stack

- **Backend:** C# .NET 8, ASP.NET Core Web API, Entity Framework Core, PostgreSQL + pgvector, SignalR, JWT auth
- **Frontend:** React 18, TypeScript, Vite, React Router, Tailwind CSS, TanStack Query
- **Multi-tenancy:** Row-level isolation via `CompanyId` + global EF Core query filter

See [`docs/architecture.md`](./docs/architecture.md) for more detail.

### Testing the RAG pipeline end-to-end (Sprint 4)

> **Correction:** this section originally said "OpenAI API key" — the actual
> registered providers are `GroqChatProvider` (chat) and
> `CohereEmbeddingProvider` (embeddings), both free-tier. Config keys below
> are the real ones; see `DependencyInjection.cs`'s `// ---- Sprint 4: AI ----`
> block if you want to verify.

1. **Add your Groq and Cohere API keys** to `api/src/Api/appsettings.Development.json`:
   ```json
   {
     "Groq":   { "ApiKey": "gsk_..." },
     "Cohere": { "ApiKey": "..." }
   }
   ```
   Both have free tiers: [console.groq.com](https://console.groq.com) and
   [dashboard.cohere.com/api-keys](https://dashboard.cohere.com/api-keys).
2. Start the API and frontend, log in, and go to **Knowledge Base** in the sidebar.
3. Click **Add entry** and paste some text (e.g., your refund policy, product FAQ).
4. The entry is embedded and saved — you'll see it appear in the list immediately.
5. Open the chat widget (or send a WhatsApp message) and ask a question that the
   text answers.
6. The AI should reply with information grounded in your entry. If you ask
   something not in the knowledge base, it says it doesn't know and offers a human agent.
7. Watch the **Overview** dashboard token bar update within 15 seconds.

**No Groq/Cohere key?** The pipeline still runs — `RagService.RetrieveAsync`
fails gracefully, `ChatHub` and `WhatsAppWebhookController` catch the
exception and proceed with empty context. Chat replies themselves need
`Groq:ApiKey` to be set, though — `GroqChatProvider` throws if it's missing,
same non-fatal-for-RAG-but-fatal-for-chat behavior as before.

### Testing the ticket + email flow end-to-end (Sprint 5)

1. **Add your Brevo API key** to `appsettings.Development.json`:
   ```json
   { "Brevo": { "ApiKey": "xkeysib-..." } }
   ```
   Free tier at [app.brevo.com](https://app.brevo.com). This is needed
   **regardless of which email inbound mode you pick below** — it's also used
   for internal "new ticket" notifications sent to your team, which is a
   separate thing from the customer-facing email channel.

   > **⚠️ The #1 cause of `Brevo API error 401: {"message":"Key not found"}`:**
   > Brevo has **two different kinds of key** under Settings → SMTP & API, and
   > they are not interchangeable:
   > - **API key** (starts `xkeysib-...`) — generated under the **API Keys**
   >   tab. This is what this codebase needs; it calls Brevo's REST API
   >   (`POST /v3/smtp/email`) with an `api-key` header.
   > - **SMTP key** (starts `xsmtpsib-...`) — generated under the **SMTP**
   >   tab, for authenticating an actual SMTP connection. This code never
   >   opens an SMTP connection *to Brevo* (it does open one to your own
   >   mailbox if you pick Imap mode below, but that's unrelated), so an SMTP
   >   key here always fails with 401 "Key not found."
   >
   > Go to Brevo → account name (top right) → **Settings → SMTP & API → API
   > Keys** (not the SMTP tab) → **Generate a new API key**. Also confirm the
   > sender address you connect is a **verified sender** in Brevo (Settings →
   > Senders, Domains & Dedicated IPs) — an unverified sender causes a
   > different error (400, not 401) once the key itself is correct.

2. **Escalate a conversation to create a ticket** — the fastest path is the
   web chat widget: ask something that's not in your knowledge base *and*
   phrase it as wanting a human (e.g. "I want to talk to a real person about
   my order"). `EscalationService` should fire and a ticket appears in the
   dashboard's **Tickets** page within a couple of seconds.
3. **Reply to the ticket** from the dashboard — the reply is delivered back
   over the ticket's originating channel: web chat is pushed live over
   SignalR to the widget (and saved either way, so it's there on refresh via
   `ChatHub.GetHistory`), WhatsApp via the Graph API, email via Brevo or SMTP
   depending on which mode you picked below.
4. **Assign the ticket** — open it, click "Assign", pick an agent and/or team,
   save. (Owner/Admin role required — matches `TicketsController.Assign`'s
   `[Authorize]` policy.)
5. **Connect the Email channel** — Settings → Channels → Email. Two inbound
   modes, pick whichever's simpler for you:

   **Option A — IMAP/SMTP (free, recommended for most people)**
   - Pick your provider (Gmail, Outlook/Office365, Zoho, or Custom) — this
     pre-fills the IMAP/SMTP host and port.
   - Gmail/Workspace needs an **App Password**, not your normal login
     password: Google Account → Security → 2-Step Verification (must be on)
     → App passwords. A regular password is rejected even if it's correct.
   - Enter the mailbox's email + password, click Connect. The backend
     actually connects via IMAP *and* SMTP before saving anything — a wrong
     password fails immediately with a clear error rather than silently
     breaking `ImapPollingService` later.
   - Only mail received **after** you connect is ever processed — a mailbox's
     existing unread backlog (old newsletters, notifications, etc.) won't
     flood in as new conversations. Disconnecting and reconnecting resets
     this cutoff to "now" again.
   - That's it. `ImapPollingService` checks for new mail automatically every
     `Email:PollingIntervalSeconds` (default 30s, floor 10s) — no dashboard
     to configure, no public URL/ngrok needed, works from your local machine.
   - **Note:** if a mailbox already has this channel connected from *before*
     this update (i.e. via the old Brevo-only ConnectEmail), disconnect and
     reconnect it once — the stored metadata format changed and old
     connections default to Webhook mode with no sender info, which will
     silently fail to send agent replies until reconnected.

   **Option B — Brevo webhook (Brevo's paid plan required)**
   - Brevo's **free tier only covers sending mail, not receiving it** —
     inbound parsing is a paid-plan feature. If you don't want to pay for
     that, use Option A instead.
   - Enter sender address + display name, choose "Brevo webhook", connect.
   - Copy the generated webhook URL, then in Brevo: Settings → Transactional
     → Inbound Parsing → add a route pointing at that URL. Locally this needs
     a public URL — use `ngrok http 5080` the same way the WhatsApp setup
     does (see `docs/architecture.md`), and point Brevo at the ngrok URL +
     `/webhook/email/{companyId}`.
6. **Send a real email** to your connected address — it should create (or
   continue) a conversation, get an AI-generated reply sent back with proper
   `In-Reply-To` threading headers, and escalate to a ticket under the same
   rules as any other channel. With IMAP mode, allow up to the polling
   interval (default 30s) for it to be picked up.
7. **Configure escalation rules** — onboarding Step 5, or Settings → Company →
   Escalation rules card. Try raising the confidence threshold or turning on
   the payment-keyword rule, then re-test step 2 with a payment-related
   message ("I was charged twice on M-Pesa") to see the rule fire.
8. **Verify web chat continuity** — after step 2 creates a ticket, refresh
   the widget page. You should see the full prior conversation (not a blank
   slate) plus a banner saying the ticket is open. Reply from the dashboard
   (step 3) and confirm it appears in the widget without a refresh. Resolve
   or close the ticket from the dashboard, then send a new widget message —
   it should start a fresh conversation the AI answers directly, not another
   "added to your ticket" acknowledgement.

### Testing Messenger, Telegram, and Sandbox mode end-to-end (Sprint 6)

1. **Messenger** — Settings → Channels → Messenger:
   - Needs a Facebook Page + a Meta for Developers app with the Messenger
     product added ([developers.facebook.com](https://developers.facebook.com)).
   - Under Messenger → Access Tokens, generate a Page Access Token; copy the
     Page ID from Page Settings → About.
   - Paste both in — verified against the Graph API before saving.
   - Add `Messenger:VerifyToken` to `appsettings.Development.json` (any string
     you choose), then in the Meta app's webhook settings, subscribe to
     `messages` for your Page, pointing at `/webhook/messenger/{companyId}`
     (needs a public URL locally — same `ngrok http 5080` approach as WhatsApp).
   - Message your Page on Messenger to test.
2. **Telegram** — Settings → Channels → Telegram:
   - Message [@BotFather](https://t.me/BotFather) on Telegram, send `/newbot`,
     follow the prompts, copy the token it gives you.
   - Paste it in and click Connect — **that's the entire setup.** Unlike every
     other channel, this one call both verifies the token *and* registers the
     webhook with Telegram automatically. Needs a public URL locally too
     (ngrok), since `setWebhook` needs a real HTTPS endpoint to register.
   - Message your bot on Telegram to test.
3. **Sandbox mode** — Sidebar → Sandbox, or during onboarding's final step:
   - Try asking your AI something from your knowledge base, then something
     that should escalate (e.g. "let me talk to a human"). You'll see an
     explanatory message ("this would create a ticket in production...")
     instead of an actual ticket appearing in the Tickets page — confirm
     nothing shows up there.
   - Check the Overview dashboard's token usage — it should **not** move
     after sandbox messages, even though the AI really did answer them.
   - Copy the sandbox test link and open it in a private/incognito window
     (or send it to a teammate) — same chat, no login needed.
   - Click "Regenerate link" and confirm the old link stops working
     (reloads to "This test link isn't valid").

### Testing Analytics + Billing end-to-end (Sprint 7)

1. **Analytics** — the Overview page is a quick-glance landing page now;
   the deep dive lives at **Analytics** in the sidebar: a date range picker
   (7/14/30/90 days), trend arrows on every KPI, AI containment rate, avg
   first response time, conversation volume, channel breakdown, why
   conversations escalate, token usage over time, CSAT (average, 1-5
   distribution, trend), and top questions. Send a few test messages
   through different channels (widget, WhatsApp, sandbox intentionally
   doesn't count anywhere in Analytics — see below) and refresh to see them
   show up.
2. **CSAT** — open the widget (or `/widget-test.html`), send at least two
   messages back and forth, then a small star-rating bar appears above the
   input. Rate it, confirm it shows "Thanks for your feedback!" with the
   stars filled in, then refresh the page and confirm the rating persists
   (via `ChatHub.GetHistory`) instead of showing the prompt again. Check
   Analytics → Customer satisfaction for the average score and
   distribution to update.
3. **Sandbox exclusion** — rate a few conversations and send several
   messages in **Sandbox**, then confirm none of it shows up anywhere in
   Analytics (conversation count, CSAT, token usage) — sandbox traffic is
   deliberately excluded from every analytics query so real-customer
   metrics aren't skewed by your own testing.
2. **M-Pesa billing** — needs free Daraja sandbox credentials:
   - Register at [developer.safaricom.co.ke](https://developer.safaricom.co.ke),
     create an app, copy its Consumer Key + Consumer Secret.
   - Add to `appsettings.Development.json`:
     ```json
     { "Mpesa": { "ConsumerKey": "...", "ConsumerSecret": "...", "CallbackUrl": "https://your-ngrok-url/api/billing/mpesa/callback" } }
     ```
     Shortcode and Passkey are already set to Safaricom's own published
     sandbox test values in `appsettings.json` — no need to change those for
     sandbox testing. `CallbackUrl` needs a public URL (`ngrok http 5080`,
     same as the other webhooks).
   - Go to Settings → Billing, enter Safaricom's official sandbox test phone
     number **254708374149** (a real phone number won't receive anything in
     sandbox mode — this specific number is what triggers Daraja's simulated
     PIN-entry flow), and click a plan.
   - You'll see "Check your phone…" — the sandbox environment auto-completes
     the payment after a few seconds without needing an actual PIN entry.
     Confirm the page updates to "Payment received" and your plan changes.
   - Check the Owner's configured email for the receipt (needs a working
     Brevo API key — see the Sprint 5 section above).
3. **Token budget cutoff** — lower a company's `MonthlyTokenBudget` directly
   in the database to something tiny (e.g. `50`) and send a message on any
   channel. You should see "Your AI conversation limit has been reached…"
   instead of a normal AI reply, and a ticket should appear in the Tickets
   page with reason "Monthly AI token budget exceeded" — but sending a
   message in Sandbox should still work normally regardless.
4. **90% warning email** — set `TokensUsedThisMonth` to 90% of
   `MonthlyTokenBudget` directly in the database, then send one message.
   Check the Owner/Admin's email for the warning — then send another message
   and confirm a second warning is **not** sent (it only fires once per
   billing period).
5. **Pricing page** — visit `/pricing` while logged out. Plans should match
   exactly what's shown in Settings → Billing (both read the same catalog).

### Production performance note (IVFFlat / HNSW index)

Once a company has > ~1000 knowledge chunks, add a vector index so similarity
search stays fast:
```sql
CREATE INDEX CONCURRENTLY ON "KnowledgeChunks"
    USING hnsw ("Embedding" vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);
```

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

1. **Add your OpenAI API key** to `api/src/Api/appsettings.Development.json`:
   ```json
   { "OpenAI": { "ApiKey": "sk-..." } }
   ```
2. Start the API and frontend, log in, and go to **Knowledge Base** in the sidebar.
3. Click **Add entry** and paste some text (e.g., your refund policy, product FAQ).
4. The entry is embedded and saved — you'll see it appear in the list immediately.
5. Open the chat widget (or send a WhatsApp message) and ask a question that the
   text answers.
6. The AI should reply with information grounded in your entry. If you ask
   something not in the knowledge base, it says it doesn't know and offers a human agent.
7. Watch the **Overview** dashboard token bar update within 15 seconds.

**No OpenAI key?** The pipeline still runs — `RagService.RetrieveAsync` fails
gracefully, `ChatHub` catches the error and proceeds with empty context.
The AI still replies (via `PlaceholderAiProvider` fallback logic... actually
no: `OpenAiProvider.CreateHttpClient` throws `InvalidOperationException` at
runtime if `OpenAI:ApiKey` is empty. The hub's outer `catch (Exception)` returns
an "Error" event to the widget. For dev without a key, keep
`PlaceholderAiProvider` registered in DI by reverting the one line in
`DependencyInjection.cs`.

### Production performance note (IVFFlat / HNSW index)

Once a company has > ~1000 knowledge chunks, add a vector index so similarity
search stays fast:
```sql
CREATE INDEX CONCURRENTLY ON "KnowledgeChunks"
    USING hnsw ("Embedding" vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);
```

# Load Testing — Sprint 8

**Checklist item:** "Load test with 50 concurrent conversations — p95 latency
< 5 seconds."

## What it tests

`api/tools/LoadTest` is a small console tool that drives real conversations
through the exact same path a customer's website visitor uses: it opens a
SignalR connection to `/hubs/chat` (the same hub the embeddable widget uses),
joins a company's group, sends a message, and measures wall-clock time until
the AI's `ReplyComplete` event arrives. That means the numbers it reports
include everything a real user waits on — SignalR connect, RAG retrieval
(Cohere embeddings), Groq generation, escalation-rule evaluation, and the
database writes for the conversation/message rows — not just raw HTTP latency.

## Running it

```bash
cd api
dotnet run --project tools/LoadTest -- --url http://localhost:5080 --key pub_SYz6Qf53Gbz50RWNWrp0bxIqgInzf6U
```

Required:
- `--url` — your API's base URL (no trailing `/hubs/chat`, the tool appends it)
- `--key` — a real company's public widget key or sandbox token. **Use a
  dedicated test/pilot company**, not a live paying client's key — 50
  concurrent AI generations will consume real token budget.

Common options:

| Flag | Default | Purpose |
|---|---|---|
| `--conversations` | 50 | Concurrent simulated conversations |
| `--message` | "What are your opening hours?" | What every simulated visitor asks |
| `--ramp-up-seconds` | 0 | Spread starts over N seconds instead of all-at-once |
| `--timeout-seconds` | 30 | Per-conversation timeout waiting for a reply |
| `--p95-threshold-seconds` | 5 | Pass/fail threshold (matches the Sprint 8 target) |

## Reading the output

The tool prints per-percentile latency (p50/p95/p99/min/max/mean) across every
*successful* conversation, plus a final `PASS`/`FAIL` verdict against the
threshold. A run only passes if **both** every conversation succeeded *and*
p95 is under the threshold — a fast-but-flaky backend isn't launch-ready
either.

## If it fails

Rough triage order, cheapest-to-check first:
1. **Check the structured logs** (`docs/security-audit.md` covers what's
   logged) for the slow requests — Serilog's request logging includes elapsed
   time per HTTP call, and `RagService`/`CohereEmbeddingProvider`/
   `GroqChatProvider` calls are the most likely bottleneck, not the API layer
   itself.
2. **Database connection pool exhaustion** — 50 concurrent conversations means
   up to 50 concurrent EF Core contexts; confirm Npgsql's pool size isn't the
   limiting factor.
3. **Groq/Cohere rate limits** — free-tier keys have per-minute request caps;
   50 concurrent generations could hit them. Check for 429s in the structured
   logs from `GroqChatProvider`/`CohereEmbeddingProvider`.
4. **Scale the hosting plan** — if the above are clear and it's still slow,
   it's likely just compute — Render's free/starter tiers are not sized for
   this load.

## What this tool does *not* test

- It does not test the REST API surface (dashboard endpoints, webhooks) under
  load — only the chat pipeline, which is the one with a stated SLA.
- It's not a substitute for the security audit's recommendation to also run a
  proper dependency/vulnerability scan and (eventually) a third-party pentest
  — this tool is purely about latency under concurrency.

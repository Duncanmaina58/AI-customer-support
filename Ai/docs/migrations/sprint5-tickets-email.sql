-- =============================================================================
-- Migration: Sprint 5 — Tickets (TicketNumber, AssignedTeam, EscalationReason)
--                     + Conversations (EmailMessageId, EmailSubject)
-- =============================================================================
--
-- Run this against your existing Sprint 4 database before starting the Sprint 5
-- API. If you are starting fresh, EF Core EnsureCreated will create the columns
-- correctly from the updated entity configurations — skip this script.
--
-- All changes are additive (new columns with nullable or default values) so
-- no data is lost and no down-time is required.
-- =============================================================================

BEGIN;

-- ---- Tickets ----------------------------------------------------------------

-- Per-company human-readable ticket number (e.g. #1, #2, #42).
-- Default 0 means "not yet assigned" — TicketService generates the real value.
ALTER TABLE "Tickets"
    ADD COLUMN IF NOT EXISTS "TicketNumber"     integer     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "AssignedTeam"     varchar(100),
    ADD COLUMN IF NOT EXISTS "EscalationReason" varchar(500);

-- Unique per-company ticket number. The constraint is named so it can be
-- dropped/replaced if you need to re-run this script.
ALTER TABLE "Tickets"
    DROP CONSTRAINT IF EXISTS "uq_tickets_company_number";

ALTER TABLE "Tickets"
    ADD CONSTRAINT "uq_tickets_company_number"
    UNIQUE ("CompanyId", "TicketNumber");

-- ---- Conversations ----------------------------------------------------------

-- Email threading: the Message-ID of the most recent inbound email,
-- used as In-Reply-To on the next outbound reply.
ALTER TABLE "Conversations"
    ADD COLUMN IF NOT EXISTS "EmailMessageId" varchar(512),
    ADD COLUMN IF NOT EXISTS "EmailSubject"   varchar(500);

-- Partial index (only non-null rows) for fast lookup when Brevo delivers
-- a reply email and we need to find the existing thread by In-Reply-To.
CREATE INDEX IF NOT EXISTS "IX_Conversations_EmailMessageId"
    ON "Conversations" ("EmailMessageId")
    WHERE "EmailMessageId" IS NOT NULL;

COMMIT;

-- Verify
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name IN ('Tickets', 'Conversations')
  AND column_name IN ('TicketNumber','AssignedTeam','EscalationReason',
                      'EmailMessageId','EmailSubject')
ORDER BY table_name, column_name;

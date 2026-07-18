-- =============================================================================
-- Migration: Sprint 7 follow-up — real CSAT + per-message token tracking
-- =============================================================================
--
-- Run this against your existing Sprint 7 database (after
-- sprint7-analytics-billing.sql) before starting this pass's API. If you are
-- starting fresh, EF Core EnsureCreated will create everything correctly from
-- the updated entity configurations — skip this script.
--
-- All changes are additive (new nullable columns) — no data is lost and no
-- downtime is required. Existing rows simply have NULL for these new columns,
-- which every query in this pass already treats as "not yet known"/"not rated".
-- =============================================================================

BEGIN;

-- ---- Messages: per-AI-reply token cost -------------------------------------
-- Enables a real daily/weekly token-usage trend in analytics instead of only
-- a single cumulative monthly number. Company.TokensUsedThisMonth remains the
-- fast, atomic running total used for budget enforcement — this is additive
-- detail, not a replacement.

ALTER TABLE "Messages"
    ADD COLUMN IF NOT EXISTS "TokensUsed" integer NULL;

-- ---- Conversations: CSAT rating ---------------------------------------------

ALTER TABLE "Conversations"
    ADD COLUMN IF NOT EXISTS "CsatScore" integer NULL,
    ADD COLUMN IF NOT EXISTS "CsatSubmittedAt" timestamptz NULL;

-- PostgreSQL has no "ADD CONSTRAINT IF NOT EXISTS" syntax — guard manually so
-- this script stays safely re-runnable like every ADD COLUMN above it.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_Conversations_CsatScore_Range'
    ) THEN
        ALTER TABLE "Conversations"
            ADD CONSTRAINT "CK_Conversations_CsatScore_Range"
            CHECK ("CsatScore" IS NULL OR ("CsatScore" >= 1 AND "CsatScore" <= 5));
    END IF;
END $$;

COMMIT;

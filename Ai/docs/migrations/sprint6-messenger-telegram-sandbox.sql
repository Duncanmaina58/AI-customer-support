-- =============================================================================
-- Migration: Sprint 6 — Messenger, Telegram, Sandbox Mode
-- =============================================================================
--
-- Run this against your existing Sprint 5 database before starting the Sprint 6
-- API. If you are starting fresh, EF Core EnsureCreated will create the columns
-- correctly from the updated entity configurations — skip this script.
--
-- Messenger and Telegram need NO new columns at all: ChannelType already had
-- Messenger/Telegram enum values reserved since Sprint 1, and both channels
-- reuse ChannelConnections.CredentialsEncrypted / MetadataJson exactly like
-- WhatsApp and Email do (see EmailChannelMetadata's pattern from Sprint 5).
--
-- All changes are additive (new columns with defaults) so no data is lost and
-- no downtime is required.
-- =============================================================================

BEGIN;

-- ---- Companies: sandbox test link ------------------------------------------

ALTER TABLE "Companies"
    ADD COLUMN IF NOT EXISTS "SandboxToken" varchar(64) NOT NULL DEFAULT '';

-- Backfill every existing company with a real token — an empty string would
-- collide with every other empty string once the unique index below is added.
UPDATE "Companies"
    SET "SandboxToken" = 'sbx_' || replace(gen_random_uuid()::text, '-', '')
    WHERE "SandboxToken" = '';

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Companies_SandboxToken" ON "Companies" ("SandboxToken");

-- ---- Conversations: sandbox flag --------------------------------------------

ALTER TABLE "Conversations"
    ADD COLUMN IF NOT EXISTS "IsSandbox" boolean NOT NULL DEFAULT false;

COMMIT;

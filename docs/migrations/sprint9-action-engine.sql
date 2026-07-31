-- =============================================================================
-- Migration: Sprint 9 — AI Action Engine
-- =============================================================================
--
-- Run this against an existing database before starting the updated Sprint 9
-- API, OR just let `dotnet ef database update` apply
-- api/src/Api.Infrastructure/Migrations/20260713100000_ActionEngine.cs, which is
-- the source of truth — this file is a human-readable summary of that migration,
-- kept for the same documentation reasons as the other docs/migrations/*.sql
-- files in this folder.
--
-- All three tables are brand new — no existing data is touched.
-- =============================================================================

BEGIN;

-- ---- ActionDefinitions: a company's registered webhook actions ------------

CREATE TABLE IF NOT EXISTS "ActionDefinitions" (
    "Id"                     uuid PRIMARY KEY,
    "CompanyId"              uuid NOT NULL REFERENCES "Companies"("Id") ON DELETE CASCADE,
    "ActionType"             varchar(100) NOT NULL,   -- e.g. "cancel_order" — the AI's <intent> block refers to this
    "DisplayName"            varchar(200) NOT NULL,
    "WebhookUrl"             varchar(2000) NOT NULL,  -- https:// only, enforced in ActionDefinitionsController
    "WebhookSecretEncrypted" text NOT NULL,            -- REVERSIBLY encrypted (not a one-way hash) — HMAC signing needs it back
    "RequiresVerification"   boolean NOT NULL DEFAULT false,
    "IsReadOnly"             boolean NOT NULL DEFAULT false,  -- true skips verification even if RequiresVerification is set
    "ParameterSchema"        text NULL,
    "TimeoutSeconds"         integer NOT NULL DEFAULT 10,
    "IsActive"               boolean NOT NULL DEFAULT true,
    "CreatedAt"              timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt"              timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ActionDefinitions_CompanyId_ActionType"
    ON "ActionDefinitions" ("CompanyId", "ActionType");

-- ---- ActionLogs: IMMUTABLE audit trail --------------------------------------
-- Application-level enforcement lives in AppDbContext.SaveChangesAsync, which
-- throws if any ActionLog row is ever found in a Modified/Deleted state before
-- a single row is written — see that method's doc comment for why this, and
-- not just "nobody calls .Update()", is what "verified" actually means here.

CREATE TABLE IF NOT EXISTS "ActionLogs" (
    "Id"                  uuid PRIMARY KEY,
    "CompanyId"           uuid NOT NULL REFERENCES "Companies"("Id") ON DELETE RESTRICT,
    "ConversationId"      uuid NOT NULL REFERENCES "Conversations"("Id") ON DELETE RESTRICT,
    "ActionDefinitionId"  uuid NULL REFERENCES "ActionDefinitions"("Id") ON DELETE SET NULL,
    "ActionType"          varchar(100) NOT NULL,   -- denormalized so the log means something even if ActionDefinitionId is null
    "CustomerId"          varchar(200) NOT NULL,
    "ParametersEncrypted" text NOT NULL,            -- may be PII (order/account numbers) — encrypted at rest
    "IdentityVerified"    boolean NOT NULL DEFAULT false,
    "VerificationMethod"  varchar(20) NOT NULL DEFAULT 'None',  -- None | OtpSms | OtpWhatsapp
    "WebhookStatusCode"   integer NULL,
    "WebhookResponseJson" text NULL,
    "Success"             boolean NOT NULL,
    "ErrorMessage"        text NULL,
    "ExecutedAt"          timestamptz NOT NULL DEFAULT now(),
    "DurationMs"          integer NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS "IX_ActionLogs_CompanyId_ExecutedAt" ON "ActionLogs" ("CompanyId", "ExecutedAt");
CREATE INDEX IF NOT EXISTS "IX_ActionLogs_ConversationId"       ON "ActionLogs" ("ConversationId");
CREATE INDEX IF NOT EXISTS "IX_ActionLogs_ActionDefinitionId"   ON "ActionLogs" ("ActionDefinitionId");

-- ---- OtpVerifications: identity-verification state -------------------------
-- Sprint 9 scaffolding for Sprint 10's real SMS delivery (Africa's Talking) —
-- see Api.Infrastructure.Actions.ISmsSender's doc comment.

CREATE TABLE IF NOT EXISTS "OtpVerifications" (
    "Id"                    uuid PRIMARY KEY,
    "CompanyId"             uuid NOT NULL REFERENCES "Companies"("Id") ON DELETE CASCADE,
    "ConversationId"        uuid NOT NULL REFERENCES "Conversations"("Id") ON DELETE RESTRICT,
    "CustomerId"            varchar(200) NOT NULL,
    "OtpHash"               varchar(200) NOT NULL,   -- PasswordHasher-hashed 6-digit code, never plaintext
    "Purpose"               varchar(100) NOT NULL DEFAULT 'action_verification',
    "ActionType"            varchar(100) NOT NULL,
    "PendingParametersJson" text NOT NULL,            -- the action's params, captured at send-time so it can run once verified
    "AttemptCount"          integer NOT NULL DEFAULT 0,
    "ExpiresAt"             timestamptz NOT NULL,
    "VerifiedAt"            timestamptz NULL,
    "CreatedAt"             timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS "IX_OtpVerifications_ConversationId_VerifiedAt_ExpiresAt"
    ON "OtpVerifications" ("ConversationId", "VerifiedAt", "ExpiresAt");

COMMIT;

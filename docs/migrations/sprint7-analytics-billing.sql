-- =============================================================================
-- Migration: Sprint 7 — Analytics + Billing (M-Pesa)
-- =============================================================================
--
-- Run this against your existing Sprint 6 database before starting the Sprint 7
-- API. If you are starting fresh, EF Core EnsureCreated will create everything
-- correctly from the updated entity configurations — skip this script.
--
-- Analytics needed NO schema changes at all — every endpoint added this sprint
-- (conversations-over-time, channel-breakdown, top-questions) is read-only
-- aggregation over tables that already existed (Conversations, Messages).
--
-- All changes here are additive (new columns/table with defaults) — no data
-- is lost and no downtime is required.
-- =============================================================================

BEGIN;

-- ---- Companies: billing period tracking ------------------------------------

ALTER TABLE "Companies"
    ADD COLUMN IF NOT EXISTS "CurrentPeriodStartAt" timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS "TokenBudgetWarningSentAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "BillingPhoneNumber" varchar(20) NULL;

-- ---- MpesaTransactions: one row per STK Push attempt -----------------------

CREATE TABLE IF NOT EXISTS "MpesaTransactions" (
    "Id"                 uuid PRIMARY KEY,
    "CompanyId"          uuid NOT NULL REFERENCES "Companies"("Id") ON DELETE CASCADE,
    "RequestedPlan"      varchar(16) NOT NULL,
    "PhoneNumber"        varchar(20) NOT NULL,
    "AmountKes"          decimal(12,2) NOT NULL,
    "CheckoutRequestId"  varchar(100) NOT NULL,
    "MerchantRequestId"  varchar(100) NOT NULL,
    "Status"             varchar(16) NOT NULL DEFAULT 'Pending',
    "ResultCode"         varchar(16) NULL,
    "ResultDescription"  varchar(500) NULL,
    "MpesaReceiptNumber" varchar(50) NULL,
    "CompletedAt"        timestamptz NULL,
    "CreatedAt"          timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt"          timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_MpesaTransactions_CheckoutRequestId"
    ON "MpesaTransactions" ("CheckoutRequestId");

CREATE INDEX IF NOT EXISTS "IX_MpesaTransactions_CompanyId_CreatedAt"
    ON "MpesaTransactions" ("CompanyId", "CreatedAt");

COMMIT;

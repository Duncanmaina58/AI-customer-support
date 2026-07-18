-- =============================================================================
-- Migration: Sprint 4 update — Web Crawling
-- =============================================================================
--
-- Run this against an existing database before starting the updated Sprint 4
-- API, OR just let `dotnet ef database update` apply
-- api/src/Api.Infrastructure/Migrations/20260711120000_WebCrawling.cs, which is
-- the source of truth — this file is a human-readable summary of that migration,
-- kept for the same documentation reasons as the other docs/migrations/*.sql
-- files in this folder.
--
-- All changes are additive (new columns with defaults / new tables) — no data
-- is lost and no downtime is required. Existing KnowledgeChunks rows default
-- to SourceType = 'Document', so nothing already in the knowledge base changes
-- behaviour.
-- =============================================================================

BEGIN;

-- ---- KnowledgeChunks: distinguish manual entries from crawled web pages ----

ALTER TABLE "KnowledgeChunks"
    ADD COLUMN IF NOT EXISTS "SourceType"   varchar(20)  NOT NULL DEFAULT 'Document',
    ADD COLUMN IF NOT EXISTS "SourceUrl"    varchar(2000) NULL,
    ADD COLUMN IF NOT EXISTS "WebSourceId"  uuid NULL;

-- ---- WebSources: a company's connected website(s) --------------------------

CREATE TABLE IF NOT EXISTS "WebSources" (
    "Id"                        uuid PRIMARY KEY,
    "CompanyId"                 uuid NOT NULL REFERENCES "Companies"("Id") ON DELETE CASCADE,
    "Url"                       varchar(2000) NOT NULL,
    "CrawlMode"                 varchar(20) NOT NULL,   -- FullSite | SinglePage | Sitemap
    "CrawlDepth"                integer NOT NULL DEFAULT 3,
    "IncludePattern"            varchar(500) NULL,
    "ExcludePattern"            varchar(500) NULL,
    "MaxPages"                  integer NOT NULL DEFAULT 200,
    "Status"                    varchar(20) NOT NULL DEFAULT 'Pending',  -- Pending | Crawling | Indexed | Error | Paused
    "PagesCrawled"              integer NOT NULL DEFAULT 0,
    "ChunksCreated"             integer NOT NULL DEFAULT 0,
    "MonitoringMode"            varchar(20) NOT NULL DEFAULT 'Adaptive', -- Adaptive | Fixed | Manual
    "FixedIntervalHours"        integer NULL,
    "NotifyOnChange"            boolean NOT NULL DEFAULT true,
    "LastCrawledAt"             timestamptz NULL,
    "ErrorMessage"              text NULL,
    "HasJsRenderedPagesWarning" boolean NOT NULL DEFAULT false,
    "MaxPagesReached"           boolean NOT NULL DEFAULT false,
    "CurrentCrawlUrl"           varchar(2000) NULL,
    "EstimatedTotalPages"       integer NULL,
    "CreatedAt"                 timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt"                 timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_WebSources_CompanyId_Url" ON "WebSources" ("CompanyId", "Url");

-- ---- WebPages: one row per crawled page, tracked for change detection ------

CREATE TABLE IF NOT EXISTS "WebPages" (
    "Id"                uuid PRIMARY KEY,
    "WebSourceId"       uuid NOT NULL REFERENCES "WebSources"("Id") ON DELETE CASCADE,
    "CompanyId"         uuid NOT NULL REFERENCES "Companies"("Id") ON DELETE RESTRICT,
    "Url"               varchar(2000) NOT NULL,
    "Title"             varchar(500) NULL,
    "ContentHash"       varchar(64) NOT NULL,   -- SHA-256 hex of extracted text
    "ContentLength"     integer NOT NULL DEFAULT 0,
    "HttpETag"          varchar(200) NULL,
    "HttpLastModified"  timestamptz NULL,
    "CheckCount"        integer NOT NULL DEFAULT 0,
    "ChangeCount"       integer NOT NULL DEFAULT 0,
    "LastCheckedAt"     timestamptz NULL,
    "LastChangedAt"     timestamptz NULL,
    "NextCheckAt"       timestamptz NOT NULL DEFAULT now(),
    "Status"            varchar(20) NOT NULL DEFAULT 'Active',  -- Active | Removed | Error
    "CreatedAt"         timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt"         timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_WebPages_WebSourceId_Url"       ON "WebPages" ("WebSourceId", "Url");
CREATE INDEX        IF NOT EXISTS "IX_WebPages_Status_NextCheckAt"    ON "WebPages" ("Status", "NextCheckAt");
CREATE INDEX        IF NOT EXISTS "IX_WebPages_CompanyId_LastChangedAt" ON "WebPages" ("CompanyId", "LastChangedAt");

-- ---- KnowledgeChunks: FK + indexes now that WebSources exists --------------

ALTER TABLE "KnowledgeChunks"
    ADD CONSTRAINT "FK_KnowledgeChunks_WebSources_WebSourceId"
        FOREIGN KEY ("WebSourceId") REFERENCES "WebSources"("Id") ON DELETE CASCADE;

CREATE INDEX IF NOT EXISTS "IX_KnowledgeChunks_WebSourceId" ON "KnowledgeChunks" ("WebSourceId");
CREATE INDEX IF NOT EXISTS "IX_KnowledgeChunks_CompanyId_SourceUrl"
    ON "KnowledgeChunks" ("CompanyId", "SourceUrl") WHERE "SourceUrl" IS NOT NULL;

COMMIT;

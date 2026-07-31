-- =============================================================================
-- Migration: OpenAI text-embedding-3-small (1536-dim)
--            → Cohere embed-multilingual-v3.0 (1024-dim)
-- =============================================================================
--
-- Run this ONCE against your existing database if you previously ran Sprint 4
-- with OpenAI embeddings and are now switching to Cohere. If you are starting
-- fresh (no existing KnowledgeChunks rows), just run the application — EF Core
-- EnsureCreated / your initial migration will create the column at vector(1024).
--
-- IMPORTANT: Existing vectors stored at 1536-dim are INCOMPATIBLE with the new
-- 1024-dim column. All rows must be deleted and re-added via the Knowledge Base
-- UI so that CohereEmbeddingProvider can generate fresh 1024-dim vectors.
-- There is no way to convert between the two embedding spaces.
--
-- Steps:
--   1. Back up your database (pg_dump) before running this.
--   2. Run this script as a PostgreSQL superuser or the application DB user.
--   3. Re-start the application.
--   4. Re-add all knowledge base entries via the dashboard Knowledge Base page.
--
-- =============================================================================

BEGIN;

-- Step 1: Drop all existing knowledge chunks — they were embedded at 1536-dim
-- and cannot be used with the new 1024-dim column.
TRUNCATE TABLE "KnowledgeChunks";

-- Step 2: Alter the Embedding column from vector(1536) to vector(1024).
-- pgvector requires explicit dimension in the column type; ALTER TYPE is the
-- right way to change it on an existing column.
ALTER TABLE "KnowledgeChunks"
    ALTER COLUMN "Embedding" TYPE vector(1024)
    USING "Embedding"::text::vector(1024);

-- The USING clause above reinterprets the stored text representation at the new
-- dimension. Since we TRUNCATED in Step 1, the USING expression is never
-- actually evaluated (no rows to convert), but it is required syntactically for
-- ALTER COLUMN TYPE in PostgreSQL.

COMMIT;

-- After running this script:
--   * The column is now vector(1024) — matching KnowledgeChunkConfiguration.cs.
--   * Re-add entries in the dashboard → CohereEmbeddingProvider will embed them
--     at 1024-dim and store them correctly.
--   * If you had an HNSW or IVFFlat index on Embedding, drop and recreate it:
--
--     DROP INDEX IF EXISTS "IX_KnowledgeChunks_Embedding";
--     CREATE INDEX CONCURRENTLY ON "KnowledgeChunks"
--         USING hnsw ("Embedding" vector_cosine_ops)
--         WITH (m = 16, ef_construction = 64);

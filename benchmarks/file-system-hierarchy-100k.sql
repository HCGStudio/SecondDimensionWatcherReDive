-- Run only against a disposable, fully migrated PostgreSQL database:
--   psql "$SDW_BENCHMARK_CONNECTION" -v ON_ERROR_STOP=1 \
--     -f benchmarks/file-system-hierarchy-100k.sql
--
-- The transaction is rolled back, so rerunning the benchmark is recoverable.
\timing on
BEGIN;

INSERT INTO "FileMappings"
    ("Id", "AnimationInfoId", "VirtualPath", "PhysicalPath", "FileStore")
SELECT
    md5(i::text)::uuid,
    md5((i + 100000)::text)::uuid,
    '/library/show-' || lpad((i % 1000)::text, 4, '0')
        || '/episode-' || lpad(i::text, 6, '0') || '.mkv',
    '/media/episode-' || i::text || '.mkv',
    'benchmark-100k'
FROM generate_series(1, 100000) AS i;

ANALYZE "FileMappings";
ANALYZE "FileSystemEntries";

SELECT count(*) AS mappings FROM "FileMappings";
SELECT count(*) AS hierarchy_entries FROM "FileSystemEntries";

-- Root listing: one direct child regardless of total subtree size.
EXPLAIN (ANALYZE, BUFFERS)
SELECT entry."Path", entry."ParentPath", entry."Name", entry."IsDirectory",
       mapping."Id", mapping."AnimationInfoId", mapping."VirtualPath",
       mapping."PhysicalPath", mapping."FileStore"
FROM "FileSystemEntries" AS entry
LEFT JOIN "FileMappings" AS mapping ON entry."FileMappingId" = mapping."Id"
WHERE entry."ParentPath" = '/'
ORDER BY entry."IsDirectory" DESC, entry."Name";

-- A leaf directory has 100 files; mapping data is obtained by the same query.
EXPLAIN (ANALYZE, BUFFERS)
SELECT entry."Path", entry."ParentPath", entry."Name", entry."IsDirectory",
       mapping."Id", mapping."AnimationInfoId", mapping."VirtualPath",
       mapping."PhysicalPath", mapping."FileStore"
FROM "FileSystemEntries" AS entry
LEFT JOIN "FileMappings" AS mapping ON entry."FileMappingId" = mapping."Id"
WHERE entry."ParentPath" = '/library/show-0001'
ORDER BY entry."IsDirectory" DESC, entry."Name";

-- Directory/file existence is an exact index-only EXISTS probe.
EXPLAIN (ANALYZE, BUFFERS)
SELECT EXISTS (
    SELECT 1 FROM "FileSystemEntries"
    WHERE "Path" = '/library/show-0001'
);

-- Baseline for the removed subtree materialization pattern.
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM "FileMappings"
WHERE "VirtualPath" LIKE '/library/%';

ROLLBACK;

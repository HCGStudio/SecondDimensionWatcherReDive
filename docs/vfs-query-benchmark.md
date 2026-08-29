# VFS hierarchy query benchmark

This benchmark compares the materialized direct-child read model with the old
`FileMappings.VirtualPath LIKE '/prefix/%'` subtree scan. The reproducible SQL is
in [`benchmarks/file-system-hierarchy-100k.sql`](../benchmarks/file-system-hierarchy-100k.sql).
Run it only on a disposable, fully migrated PostgreSQL database; the script wraps
all generated rows in a transaction and rolls it back.

## Measured run

- Date: 2026-08-29
- Database: PostgreSQL 17 (`postgres:17-alpine`), local Podman container
- Dataset: 100,000 file mappings, 1,001 materialized directories, 101,001 total
  hierarchy entries
- Cache: warm (the load and `ANALYZE` immediately preceded the queries)
- Read model maintenance during the synthetic one-statement insert: 68.893 s.
  This is the worst-case row-trigger import path; migration backfill uses one
  set-based statement inside the migration transaction instead.

| Query | Rows returned | Execution time | Relevant plan |
| --- | ---: | ---: | --- |
| Root direct children | 1 | 0.142 ms | bitmap index scan on `IX_FileSystemEntries_ParentPath_IsDirectory_Name` |
| `/library/show-0001` direct children + mappings | 100 | 1.197 ms | parent index scan + 100 primary-key mapping lookups |
| Exact entry existence | 1 boolean | 0.040 ms | index-only scan on `PK_FileSystemEntries`, 0 heap fetches |
| Old `/library/%` subtree load | 100,000 | 8.951 ms | sequential scan of all 100,000 mappings |

The old query's 8.951 ms is only database execution time; it also transfers and
materializes all 100,000 rows in the application. The new root query returns one
row and its work remains proportional to the number of immediate children. File
metadata is fetched once per storage backend through `GetFileInfosAsync`, rather
than by serial mapping lookup followed by serial `stat` calls.

Representative `EXPLAIN (ANALYZE, BUFFERS)` excerpts:

```text
Bitmap Index Scan on "IX_FileSystemEntries_ParentPath_IsDirectory_Name"
  Index Cond: ("ParentPath" = '/'::text)
Execution Time: 0.142 ms

Index Only Scan using "PK_FileSystemEntries" on "FileSystemEntries"
  Index Cond: ("Path" = '/library/show-0001'::text)
  Heap Fetches: 0
Execution Time: 0.040 ms

Seq Scan on "FileMappings"
  Filter: ("VirtualPath" ~~ '/library/%'::text)
  rows=100000
Execution Time: 8.951 ms
```

## Backfill and consistency

The schema migration derives file and directory nodes with set-based SQL before
installing the maintenance triggers. EF applies that migration transactionally,
so an error rolls back both schema and backfill and a normal migration retry is
safe. Insert/delete triggers maintain descendant reference counts in the same
transaction as every `FileMappings` mutation. Direct `VirtualPath` updates are
not used by the application; remaps are delete-and-insert operations under the
existing mapping transaction lock. A database trigger rejects direct
`VirtualPath` updates so an out-of-band writer cannot silently desynchronize the
read model.

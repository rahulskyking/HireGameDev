# M2.3a — public catalog query layer

Owner approved public search/detail and a separate future engagement module. This
slice adds backend queries only. No new screens/endpoints, migration, salary filter,
candidate blocklist extension, registration wall or engagement persistence.

## Run locally

```powershell
git pull --rebase origin arena/e7fc2ca3-hiregamedev
dotnet build
dotnet test
```

For real SQL translation/execution, configure a disposable PostgreSQL 16 maintenance
connection per POSTGRES_TESTS.md, then run:

```powershell
dotnet test --filter "Category=PostgresIntegration"
```

There are now eight opt-in PostgreSQL tests. Without HGD_TEST_POSTGRES they skip;
skips are not passes. The new test executes combined search filters, literal %/_,
detail/skill queries and immediate verification revocation hiding using actual
Npgsql + existing migrations. No new migration should be applied manually.

## Contract

`IPublicJobQueryService` is registered in Infrastructure:

- `SearchAsync(PublicJobSearch)` → public cards, total count and bounded pagination.
- `GetBySlugAsync(slug)` → public allowlisted detail or neutral unavailable error.
- Both queries share one SQL visibility gate: Active AND expiry strictly after
  current UTC AND Studio verified, not suspended and not deleted. No scheduler or
  cached visibility flag is required. Read-only; no history/status/quota writes.
- No job entity, creator/owner ID, xmin/version, fingerprint, application counters,
  internal notes or moderation metadata is returned in the public DTOs.
- Stable job/Studio IDs and slugs enable future authorized engagement actions; these
  are not secret tokens and grant no mutation authority.

## Filter semantics

- Up to 100 keyword characters / eight terms, case-insensitive literal substring
  matching on title OR description. All terms must match somewhere; %, _ and C++
  are literal terms, not caller-supplied SQL wildcard syntax. No relevance ranking.
- Discipline, job type, mode and exact city combine with AND.
- City + Remote intentionally returns no matches; selecting city doesn't silently
  include remote jobs or expand metro aliases.
- Experience contains the supplied 0–99.9 years, inclusive on both ends. A null
  maximum is unbounded. Fresher-friendly checks only the explicit listing flag.
- Up to 12 skill IDs; all distinct selected IDs must appear (required or optional).
  Duplicate filter IDs collapse; absent skill/city IDs simply produce no matches.
- 20 items by default, 1–50 allowed. Pages clamp to valid bounds (empty result page
  is 1 of 1). Sort PublishedAt descending, then ID ascending; never UpdatedAt.
- Case-insensitive canonical slug input; hidden/missing/malformed slug errors are
  deliberately identical. Public routes and neutral unavailable page come in b.
- Invalid enums/ranges/unbounded inputs return PublicJobs.InvalidSearch.
- Published reference labels remain readable even if a city/skill is later made
  inactive; active reference checks still run on future publishing. This does not
  silently close an existing listing or expose reference-administration flags.

InMemory tests cover visibility/status/expiry boundaries, Studio restrictions,
filters/skills, ordering/pagination, invalid inputs, public DTO fields and no tracked
writes. They cannot prove SQL behavior; execute the Npgsql regression locally.

Sandbox: C# syntax parsing only; .NET SDK unavailable. Local build/tests remain
required before M2.3b. No browser check yet because this slice has no UI changes.

## Next slices / registration strategy

M2.3b builds public pages with an honest existing-profile registration CTA, not
nonfunctional Save/Follow/Alert buttons. Browse without signup remains unchanged.
Candidate Engagement & Registration Conversion is a separate planned module
(bookmarks, alerts, Studio follows and contextual signup-return flow); placement
and implementation need their own scope agreement. Profiles/discoverability already
exist; application tracking remains Phase 3. Salary filters and extending candidate
blocklists to browsing remain explicit decisions, not assumed behavior in this API.

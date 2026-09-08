# Article search

`/search` implements the reference layout using the site's existing design tokens,
light/dark themes and public content client. The interface remains in English,
consistent with the rest of the site; English and Russian article languages are
selectable independently.

## Behavior

- Submit text with Enter or Search. Topic, tags, scope, publication period and
  sort changes apply immediately and reset the page.
- Multiple tags match **any** selected tag. Text, topic, tag selection and period
  are combined with AND. An empty text query lists published articles.
- Scopes: `all`, `title`, `content` (summary or article body), `topics`, `tags`.
  Full-text terms must match within one of the selected fields. This is word
  search with language stemming, not typo correction or arbitrary substrings.
- Title matches rank above taxonomy matches, then summary and body matches.
  PostgreSQL text rank breaks ties within these groups; publication date and
  localization ID provide deterministic final ordering.
- Only published localizations of nondeleted articles appear, including in the
  total count. Periods use the original publication date, in UTC.
- Ten items per page in the UI. URL parameters preserve filters, language, sort
  and page through reload, direct links and browser history. Out-of-range pages
  normalize to the last available page (or page 1 for an empty result).
- Loading, empty and retry states are implemented. Superseded requests are
  cancelled and their responses cannot replace newer results.

## API and existing models

```text
GET /api/public/search?languageCode=en&q=python&scope=all&topic=programming&tag=C%23&tag=.NET&period=year&sort=relevance&page=2&pageSize=10
```

Returns `items`, `totalCount`, `page`, `pageSize`, `totalPages`. The existing
`PublicArticleListItemDto` adds optional cover, reading-time and search-headline
fields. `/api/public/articles` keeps its existing list response and parameters.

No new domain entities or search tables are introduced. `ArticleLocalization`
stores optional `CoverMediaAssetId`, derived `SearchText` and `ReadingMinutes`.
The current media upload endpoint and localization save/version workflow handle
cover selection and removal. Each translation may have its own cover.

`AppDbContext.SaveChanges` prepares visible text from Markdown (including code and
image alt text, excluding HTML markup, script/style blocks and link destinations)
and estimates reading time at 200 words per minute. These derived fields do not
increment the author's edit version separately. Direct SQL writers must update
the derived text too, or set it to NULL and run the backfill.

The PostgreSQL implementation lives behind `IArticleSearch` in Infrastructure and
reuses `PublicArticleTaxonomyLoader` and author lookup. Topic/tag names and links
are queried live, so renaming or reassignment needs no synchronization worker.

Search uses EF Core LINQ and Npgsql's built-in full-text translations, without
raw search SQL or custom `DbFunction` mappings. PostgreSQL executes filtering,
ranking, counting, pagination and headlines. A separate count allows an invalid
page number to be clamped; existing batched metadata loaders run after pagination.

Stored generated vectors on the existing tables have GIN indexes for article
title, summary, body, topic names and English/Russian/simple tag searches. They
are shadow properties in the EF model, keeping provider types out of the domain.
Schema expressions use built-in PostgreSQL functions to preserve `C#`, `C++` and
`.NET`, matching query normalization in C#. Request values are parameterized by EF.

Search snippets use PostgreSQL headlines. Private-use characters U+E000/U+E001
mark matches in the response; `SearchHighlight` renders encoded text and `<mark>`
elements, never trusts a returned string as HTML.

## Migration and verification

Apply migrations through `20260908130000_UseEntityFrameworkArticleSearch` before
starting the updated API, using the existing EF migration/deployment workflow.
The new migration populates generated vectors and replaces the earlier expression
indexes and `gl_search_*` functions; its rollback restores them. API startup fills existing
NULL search-text values in batches with the same Markdown extraction used for
new saves. The backfill is repeatable and checks edit versions before updating.
It does not republish content or modify its edit version.

The E2E project verifies search on real PostgreSQL, live content/taxonomy changes,
language stemming, technical tags, ranking, totals, page boundaries, index use,
and a timing sample with 1,000 additional 300-word articles (rolled back).
Browser scenarios cover URL/history, filters, cancellation races, error recovery,
keyboard dismissal, both themes, mobile overflow, and cover upload/removal.
Screenshots are written under the E2E output's `TestResults/search` directory.

```powershell
dotnet test tests/GaifulinLab.E2E.Tests --filter FullyQualifiedName~ArticleSearchTests
```

The existing E2E fixture uses the local development database and retains browser
fixtures. It requires the existing PostgreSQL connection, WasmAppHost and
Playwright Chromium; no external search service is required.

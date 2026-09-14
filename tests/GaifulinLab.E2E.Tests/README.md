# Browser E2E tests

This project verifies registration and access boundaries, article creation and
publication, and the complete article-image flow: upload in the editor,
HTML insertion and preview, public rendering, and PDF download.

It is included in `GaifulinLab.slnx` for discovery in Visual Studio Test
Explorer. By default, the fixture starts Web and API locally, applies pending
migrations, and seeds its Identity administrator. The image/PDF scenario also
starts the local PDF worker.

## Prerequisites

- Chromium for Playwright.
- PostgreSQL at the connection string in `e2e.runsettings`.
- Docker Desktop only for the real image/PDF workflow. Start it first and check
  that `docker info` succeeds.

E2E intentionally uses the normal `gaifulinlab` development database, configured through
`GAIFULINLAB_E2E_CONNECTION_STRING` in `e2e.runsettings`. The fixture creates
it when necessary, applies pending migrations, and then creates its Identity
administrator. Chromium runs in the separate `pdf-worker-e2e` container, using
the same development database and `runtime/media` folder as the API.

The fixture starts the Web client with its `E2E` configuration. That config
points the browser to the local API and enables public PDF download only for the
test process; it does not alter the normal application settings. The fixture
also enables the matching API feature only in its child API process.

Install Chromium after building the test project:

```powershell
dotnet build tests/GaifulinLab.E2E.Tests/GaifulinLab.E2E.Tests.csproj
pwsh tests/GaifulinLab.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

The tests intentionally keep registered users, articles, uploaded images and
PDF jobs in the development database so the generated data is immediately
visible in the normal local application. Repeated runs therefore accumulate
E2E fixtures. `e2e.runsettings` contains a local-only deterministic Identity
administrator; the fixture creates it when it starts the API.

The registration/article workflow may seed uniquely named taxonomy directly
when a test needs controlled public-content fixtures. The application also
provides authenticated workspace UI and endpoints for creating and managing
topics and series; browser tests cover creating taxonomy from the article
editor.

Run the E2E suite explicitly:

```powershell
dotnet test tests/GaifulinLab.E2E.Tests/GaifulinLab.E2E.Tests.csproj
```

Test Explorer receives the local URL, database connection string, and E2E
Identity credentials from `e2e.runsettings`, which is attached to this test
project. The fixture starts and stops its own Web, API, and PDF worker as
needed; it refuses to reuse already running services, because they could be
connected to a different database.

To run against an already deployed or manually started site, set
`GAIFULINLAB_E2E_BASE_URL` to its absolute HTTP(S) URL. This explicit external
mode does not create a database, apply migrations, seed data, or start local
Web/API/PDF-worker processes.

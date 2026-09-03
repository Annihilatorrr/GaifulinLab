# Browser E2E tests

This project verifies registration and access boundaries, article creation and
publication, and the complete article-image flow: upload in the editor,
Markdown insertion and preview, public rendering, and PDF download.

It is included in `GaifulinLab.slnx` for discovery in Visual Studio Test
Explorer. With the default URL, the fixture starts Web and API automatically.
The image/PDF scenario also starts the local PDF worker when Docker is
available.

## Prerequisites

- Chromium for Playwright.

The local PostgreSQL instance must still be available. E2E intentionally uses
the normal `gaifulinlab` development database, configured through
`GAIFULINLAB_E2E_CONNECTION_STRING` in `e2e.runsettings`. The fixture creates
it when necessary, applies pending migrations, and then creates its Identity
administrator. Chromium runs in the separate `pdf-worker-e2e` container, using
the same development database and `runtime/media` folder as the API.

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

The registration/article workflow also seeds a uniquely named topic directly
into the development database, because the application currently has no UI or
admin endpoint for creating taxonomy. The browser still performs the actual
topic assignment and verifies the public topic and filtered article pages.

Run the E2E suite explicitly:

```powershell
dotnet test tests/GaifulinLab.E2E.Tests/GaifulinLab.E2E.Tests.csproj
```

Test Explorer receives the local URL, database connection string, and E2E
Identity credentials from `e2e.runsettings`, which is attached to this test
project. The fixture starts and stops its own Web, API, and PDF worker as
needed; it refuses to reuse already running services, because they could be
connected to a different database.

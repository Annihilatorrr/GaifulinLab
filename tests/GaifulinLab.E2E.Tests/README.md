# Browser E2E tests

This project verifies the complete article-image flow on the primary running
site: upload in the editor, Markdown insertion and preview, save, publish, and
public rendering.

It is intentionally excluded from `GaifulinLab.slnx`, so the normal unit and
API test command never builds Docker images or starts a browser.

## Prerequisites

- API and Web projects are running;
- Chromium for Playwright.

Install Chromium after building the test project:

```powershell
dotnet build tests/GaifulinLab.E2E.Tests/GaifulinLab.E2E.Tests.csproj
pwsh tests/GaifulinLab.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

The test writes an E2E article and its uploaded image into the configured main
database and intentionally does not delete them. Provide the main site URL and
the administrator password at the command line; the password is never stored in
the repository.

Run the E2E suite explicitly:

```powershell
$env:GAIFULINLAB_E2E_BASE_URL = 'https://localhost:7069'
$env:GAIFULINLAB_E2E_ADMIN_LOGIN = 'admin'
$env:GAIFULINLAB_E2E_ADMIN_PASSWORD = 'your-admin-password'
dotnet test tests/GaifulinLab.E2E.Tests/GaifulinLab.E2E.Tests.csproj
```

The default URL is `https://localhost:7069` when
`GAIFULINLAB_E2E_BASE_URL` is not set.

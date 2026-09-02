# Browser E2E tests

This project verifies the complete article-image flow on the primary running
site: upload in the editor, Markdown insertion and preview, save, publish,
public rendering, and PDF download.

It is included in `GaifulinLab.slnx` for discovery in Visual Studio Test
Explorer. It still requires the local services, Chromium, and administrator
credentials described below, and should be run explicitly.

## Prerequisites

- API and Web projects are running from Visual Studio;
- the local PDF worker is running;
- Chromium for Playwright.

Start the worker before the API and Web projects. Chromium runs only inside this
worker container; it uses the same local PostgreSQL database and `runtime/media`
folder as the API.

```powershell
.\scripts\start-local.ps1
```

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
$env:GAIFULINLAB_E2E_BASE_URL = 'http://localhost:5172'
$env:GAIFULINLAB_E2E_ADMIN_LOGIN = 'admin'
$env:GAIFULINLAB_E2E_ADMIN_PASSWORD = 'your-admin-password'
dotnet test tests/GaifulinLab.E2E.Tests/GaifulinLab.E2E.Tests.csproj
```

The default URL is `http://localhost:5172` when
`GAIFULINLAB_E2E_BASE_URL` is not set.

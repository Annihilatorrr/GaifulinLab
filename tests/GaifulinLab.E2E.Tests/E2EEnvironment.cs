using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace GaifulinLab.E2E.Tests;

public sealed class E2EEnvironment : IAsyncLifetime
{
    private const string DefaultBaseUrl = "http://localhost:5172";
    private const string DefaultApiHealthUrl = "http://localhost:5180/health/live";
    private const string E2EPdfWorkerService = "pdf-worker-e2e";
    private readonly List<Process> _startedProcesses = [];
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private string _repositoryRoot = null!;
    private string _connectionString = null!;
    private string _dockerConnectionString = null!;
    private bool _usesExternalSite;
    private bool _startedPdfWorker;

    public Uri BaseUri { get; private set; } = null!;
    public Uri ApiBaseUri => new(new Uri(DefaultApiHealthUrl).GetLeftPart(UriPartial.Authority));
    private string AdminLogin { get; set; } = null!;
    private string AdminPassword { get; set; } = null!;

    public async Task InitializeAsync()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_BASE_URL");
        var baseUrl = configuredBaseUrl ?? DefaultBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp
                && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "GAIFULINLAB_E2E_BASE_URL must be an absolute HTTP or HTTPS URL.");
        }

        AdminLogin = Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_ADMIN_LOGIN") ?? "admin";
        AdminPassword = Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_ADMIN_PASSWORD")
            ?? throw new InvalidOperationException(
                "Set GAIFULINLAB_E2E_ADMIN_PASSWORD before running E2E tests. "
                + "It is intentionally not stored in the repository.");
        if (string.IsNullOrWhiteSpace(AdminPassword))
        {
            throw new InvalidOperationException("GAIFULINLAB_E2E_ADMIN_PASSWORD cannot be empty.");
        }

        BaseUri = baseUri;
        _usesExternalSite = baseUri != new Uri(DefaultBaseUrl);
        _repositoryRoot = FindRepositoryRoot();

        if (!_usesExternalSite)
        {
            ConfigureDevelopmentDatabase();
            await EnsureDevelopmentDatabaseExistsAsync();
            await ApplyMigrationsAsync();
            await SeedAdminAsync();
        }

        try
        {
            if (!_usesExternalSite)
            {
                await StartLocalApplicationsAsync();
            }

            await WaitForPortAsync(BaseUri, "Web application");
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public (string Login, string Password) GetAdminCredentials() => (AdminLogin, AdminPassword);

    public async Task<SeededTopic> SeedTopicAsync(
        string name,
        string slug,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var topic = new SeededTopic(Guid.NewGuid(), name, slug, description);
        var now = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var topicCommand = new NpgsqlCommand(
            """
            INSERT INTO topics ("Id", "CreatedAt", "UpdatedAt")
            VALUES (@topicId, @createdAt, @updatedAt)
            """,
            connection,
            transaction))
        {
            topicCommand.Parameters.AddWithValue("topicId", topic.Id);
            topicCommand.Parameters.AddWithValue("createdAt", now);
            topicCommand.Parameters.AddWithValue("updatedAt", now);
            await topicCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var localizationCommand = new NpgsqlCommand(
            """
            INSERT INTO topic_localizations ("Id", "TopicId", "LanguageCode", "Name", "Slug", "Description")
            VALUES (@localizationId, @topicId, 'en', @name, @slug, @description)
            """,
            connection,
            transaction))
        {
            localizationCommand.Parameters.AddWithValue("localizationId", Guid.NewGuid());
            localizationCommand.Parameters.AddWithValue("topicId", topic.Id);
            localizationCommand.Parameters.AddWithValue("name", topic.Name);
            localizationCommand.Parameters.AddWithValue("slug", topic.Slug);
            localizationCommand.Parameters.AddWithValue(
                "description",
                topic.Description is null ? DBNull.Value : topic.Description);
            await localizationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return topic;
    }

    public async Task<SeededSeries> SeedSeriesAsync(
        string title,
        string slug,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var series = new SeededSeries(Guid.NewGuid(), title, slug, description);
        var now = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var seriesCommand = new NpgsqlCommand(
            """
            INSERT INTO series ("Id", "CreatedAt", "UpdatedAt")
            VALUES (@seriesId, @createdAt, @updatedAt)
            """,
            connection,
            transaction))
        {
            seriesCommand.Parameters.AddWithValue("seriesId", series.Id);
            seriesCommand.Parameters.AddWithValue("createdAt", now);
            seriesCommand.Parameters.AddWithValue("updatedAt", now);
            await seriesCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var localizationCommand = new NpgsqlCommand(
            """
            INSERT INTO series_localizations ("Id", "SeriesId", "LanguageCode", "Title", "Slug", "Description")
            VALUES (@localizationId, @seriesId, 'en', @title, @slug, @description)
            """,
            connection,
            transaction))
        {
            localizationCommand.Parameters.AddWithValue("localizationId", Guid.NewGuid());
            localizationCommand.Parameters.AddWithValue("seriesId", series.Id);
            localizationCommand.Parameters.AddWithValue("title", series.Title);
            localizationCommand.Parameters.AddWithValue("slug", series.Slug);
            localizationCommand.Parameters.AddWithValue(
                "description",
                series.Description is null ? DBNull.Value : series.Description);
            await localizationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return series;
    }

    public async Task<SeededArticle> SeedPublishedArticleAsync(
        string title,
        string slug,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var article = new SeededArticle(Guid.NewGuid(), title, slug);
        var now = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var ownerUserId = await GetUserIdAsync(connection, transaction, AdminLogin, cancellationToken);

        await using (var articleCommand = new NpgsqlCommand(
            """
            INSERT INTO articles ("Id", "OwnerUserId", "CreatedAt", "UpdatedAt")
            VALUES (@articleId, @ownerUserId, @createdAt, @updatedAt)
            """,
            connection,
            transaction))
        {
            articleCommand.Parameters.AddWithValue("articleId", article.Id);
            articleCommand.Parameters.AddWithValue("ownerUserId", ownerUserId);
            articleCommand.Parameters.AddWithValue("createdAt", now);
            articleCommand.Parameters.AddWithValue("updatedAt", now);
            await articleCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var localizationCommand = new NpgsqlCommand(
            """
            INSERT INTO article_localizations ("Id", "ArticleId", "LanguageCode", "Slug", "Title", "Summary", "Markdown", "Status", "PublishedAt", "UpdatedAt", "LastEditedAt")
            VALUES (@localizationId, @articleId, 'en', @slug, @title, NULL, @markdown, 'Published', @publishedAt, @updatedAt, @lastEditedAt)
            """,
            connection,
            transaction))
        {
            localizationCommand.Parameters.AddWithValue("localizationId", Guid.NewGuid());
            localizationCommand.Parameters.AddWithValue("articleId", article.Id);
            localizationCommand.Parameters.AddWithValue("slug", article.Slug);
            localizationCommand.Parameters.AddWithValue("title", article.Title);
            localizationCommand.Parameters.AddWithValue("markdown", markdown);
            localizationCommand.Parameters.AddWithValue("publishedAt", now);
            localizationCommand.Parameters.AddWithValue("updatedAt", now);
            localizationCommand.Parameters.AddWithValue("lastEditedAt", now);
            await localizationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return article;
    }

    private static async Task<string> GetUserIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string userName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"UserName\" = @userName",
            connection,
            transaction);
        command.Parameters.AddWithValue("userName", userName);
        return (string?)await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException($"The E2E administrator '{userName}' was not found.");
    }

    public async Task<long> CountArticleViewsAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM article_views WHERE \"ArticleId\" = @articleId",
            connection);
        command.Parameters.AddWithValue("articleId", articleId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Article view count query returned no value."));
    }

    public async Task ChangeArticleLocalizationAsync(
        Guid articleId,
        string languageCode,
        string title,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE article_localizations
            SET "Title" = @title,
                "Markdown" = @markdown,
                "UpdatedAt" = @updatedAt,
                "LastEditedAt" = @lastEditedAt,
                "Version" = "Version" + 1
            WHERE "ArticleId" = @articleId
              AND "LanguageCode" = @languageCode
            """,
            connection);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("markdown", markdown);
        command.Parameters.AddWithValue("updatedAt", now);
        command.Parameters.AddWithValue("lastEditedAt", now);
        command.Parameters.AddWithValue("articleId", articleId);
        command.Parameters.AddWithValue("languageCode", languageCode);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed != 1)
        {
            throw new InvalidOperationException($"Article localization '{languageCode}' was not found.");
        }
    }

    public async Task EnsurePdfWorkerAsync()
    {
        if (_usesExternalSite || _startedPdfWorker)
        {
            return;
        }

        if (await IsPdfWorkerRunningAsync(E2EPdfWorkerService))
        {
            throw new InvalidOperationException(
                "The dedicated E2E PDF worker is already running. Stop 'pdf-worker-e2e' before starting E2E tests.");
        }

        var composeFile = Path.Combine(_repositoryRoot, "docker-compose.local.yml");
        await RunCommandAsync(
            "docker",
            _repositoryRoot,
            ["compose", "-f", composeFile, "up", "--detach", "--build", E2EPdfWorkerService],
            environment: new Dictionary<string, string>
            {
                ["GAIFULINLAB_E2E_DOCKER_CONNECTION_STRING"] = _dockerConnectionString
            });
        _startedPdfWorker = true;
    }

    public async Task DisposeAsync()
    {
        if (_startedPdfWorker)
        {
            try
            {
                var composeFile = Path.Combine(_repositoryRoot, "docker-compose.local.yml");
                await RunCommandAsync(
                    "docker",
                    _repositoryRoot,
                    ["compose", "-f", composeFile, "stop", E2EPdfWorkerService]);
            }
            catch
            {
                // Test cleanup must not hide a test failure.
            }
        }

        foreach (var process in _startedProcesses.AsEnumerable().Reverse())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch
            {
                // The process may have already exited after a startup failure.
            }
            finally
            {
                process.Dispose();
            }
        }

        _httpClient.Dispose();

    }

    private async Task StartLocalApplicationsAsync()
    {
        var apiUri = new Uri(DefaultApiHealthUrl);
        if (await IsPortOpenAsync(apiUri))
        {
            throw new InvalidOperationException(
                $"API port {apiUri.Port} is already in use. Stop the existing API before starting E2E tests so they cannot use its database.");
        }

        if (await IsPortOpenAsync(BaseUri))
        {
            throw new InvalidOperationException(
                $"Web port {BaseUri.Port} is already in use. Stop the existing Web application before starting E2E tests.");
        }

        _startedProcesses.Add(StartApi());
        await WaitForReadyAsync(apiUri, "API");
        _startedProcesses.Add(StartWeb());
    }

    private Process StartApi()
    {
        var apiAssembly = GetBuildOutputPath(
            "src/GaifulinLab.Api",
            "GaifulinLab.Api.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(apiAssembly)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = "http://localhost:5180";
        startInfo.Environment["ConnectionStrings__Postgres"] = _connectionString;
        startInfo.Environment["Logging__LogLevel__Microsoft.AspNetCore.DataProtection"] = "None";
        startInfo.Environment["Logging__EventLog__LogLevel__Default"] = "None";
        startInfo.ArgumentList.Add(apiAssembly);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the API.");
    }

    private async Task ApplyMigrationsAsync()
    {
        await RunCommandAsync("dotnet", _repositoryRoot, ["tool", "restore"]);
        await RunCommandAsync(
            "dotnet",
            _repositoryRoot,
            [
                "tool", "run", "dotnet-ef", "database", "update", "--no-build",
                "--project", "src/GaifulinLab.Infrastructure/GaifulinLab.Infrastructure.csproj",
                "--startup-project", "src/GaifulinLab.Api/GaifulinLab.Api.csproj"
            ],
            environment: new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ConnectionStrings__Postgres"] = _connectionString
            });
    }

    private async Task SeedAdminAsync()
    {
        const string roleId = "e2e-admin-role";
        var passwordHash = new PasswordHasher<object>().HashPassword(new object(), AdminPassword);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var roleCommand = new NpgsqlCommand(
            """
            INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
            VALUES (@roleId, 'Admin', 'ADMIN', @concurrencyStamp)
            ON CONFLICT ("NormalizedName") DO NOTHING
            """,
            connection,
            transaction))
        {
            roleCommand.Parameters.AddWithValue("roleId", roleId);
            roleCommand.Parameters.AddWithValue("concurrencyStamp", Guid.NewGuid().ToString("N"));
            await roleCommand.ExecuteNonQueryAsync();
        }

        await using (var userCommand = new NpgsqlCommand(
            """
            INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnd", "LockoutEnabled", "AccessFailedCount")
            VALUES (@userId, @login, @normalizedLogin, NULL, NULL, false, @passwordHash, @securityStamp, @concurrencyStamp, NULL, false, false, NULL, true, 0)
            ON CONFLICT ("NormalizedUserName") DO UPDATE
            SET "PasswordHash" = EXCLUDED."PasswordHash", "SecurityStamp" = EXCLUDED."SecurityStamp"
            """,
            connection,
            transaction))
        {
            userCommand.Parameters.AddWithValue("userId", Guid.NewGuid().ToString("N"));
            userCommand.Parameters.AddWithValue("login", AdminLogin);
            userCommand.Parameters.AddWithValue("normalizedLogin", AdminLogin.ToUpperInvariant());
            userCommand.Parameters.AddWithValue("passwordHash", passwordHash);
            userCommand.Parameters.AddWithValue("securityStamp", Guid.NewGuid().ToString("N"));
            userCommand.Parameters.AddWithValue("concurrencyStamp", Guid.NewGuid().ToString("N"));
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (var assignmentCommand = new NpgsqlCommand(
            """
            INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
            SELECT user_account."Id", role."Id"
            FROM "AspNetUsers" AS user_account
            JOIN "AspNetRoles" AS role ON role."NormalizedName" = 'ADMIN'
            WHERE user_account."NormalizedUserName" = @normalizedLogin
            ON CONFLICT ("UserId", "RoleId") DO NOTHING
            """,
            connection,
            transaction))
        {
            assignmentCommand.Parameters.AddWithValue("normalizedLogin", AdminLogin.ToUpperInvariant());
            await assignmentCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private Process StartWeb()
    {
        var runtimeConfig = GetBuildOutputPath(
            "src/GaifulinLab.Web",
            "GaifulinLab.Web.runtimeconfig.json");
        var wasmAppHost = FindWasmAppHost();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = DefaultBaseUrl;
        startInfo.ArgumentList.Add(wasmAppHost);
        startInfo.ArgumentList.Add("--use-staticwebassets");
        startInfo.ArgumentList.Add($"--runtime-config={runtimeConfig}");

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Web application.");
    }

    private string GetBuildOutputPath(string relativeProjectDirectory, string fileName)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        return Path.Combine(
            _repositoryRoot,
            relativeProjectDirectory,
            "bin",
            configuration,
            "net10.0",
            fileName);
    }

    private static string FindWasmAppHost()
    {
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        var packageRoot = Path.Combine(packagesRoot, "microsoft.net.sdk.webassembly.pack");
        var wasmAppHost = Directory.Exists(packageRoot)
            ? Directory.EnumerateFiles(packageRoot, "WasmAppHost.dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        return wasmAppHost
            ?? throw new InvalidOperationException(
                "WasmAppHost was not found. Build GaifulinLab.Web before running E2E tests.");
    }

    private async Task WaitForPortAsync(Uri uri, string serviceName)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsPortOpenAsync(uri))
            {
                return;
            }

            var stoppedProcess = _startedProcesses.FirstOrDefault(process => process.HasExited);
            if (stoppedProcess is not null)
            {
                throw new InvalidOperationException(
                    $"{serviceName} stopped during startup with exit code {stoppedProcess.ExitCode}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        var process = _startedProcesses.LastOrDefault();
        if (process is not null && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        throw new TimeoutException(
            $"{serviceName} did not open '{uri.Host}:{uri.Port}' within 60 seconds.");
    }

    private async Task WaitForReadyAsync(Uri uri, string serviceName)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await _httpClient.GetAsync(uri);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The process is still starting.
            }
            catch (TaskCanceledException)
            {
                // The port accepted a connection but the service is not ready yet.
            }

            var stoppedProcess = _startedProcesses.FirstOrDefault(process => process.HasExited);
            if (stoppedProcess is not null)
            {
                throw new InvalidOperationException(
                    $"{serviceName} stopped during startup with exit code {stoppedProcess.ExitCode}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"{serviceName} did not become ready at '{uri}' within 60 seconds.");
    }

    private async Task<bool> IsPdfWorkerRunningAsync(string serviceName)
    {
        var composeFile = Path.Combine(_repositoryRoot, "docker-compose.local.yml");
        var output = await RunCommandAsync(
            "docker",
            _repositoryRoot,
            ["compose", "-f", composeFile, "ps", "--status", "running", "--quiet", serviceName],
            throwOnFailure: false);
        return !string.IsNullOrWhiteSpace(output);
    }

    private static async Task<bool> IsPortOpenAsync(Uri uri)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(uri.Host, uri.Port).WaitAsync(TimeSpan.FromMilliseconds(250));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<string> RunCommandAsync(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        bool throwOnFailure = true)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await standardOutput;
        var error = await standardError;
        if (throwOnFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{fileName} {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }

    private void ConfigureDevelopmentDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("GAIFULINLAB_E2E_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set GAIFULINLAB_E2E_CONNECTION_STRING before running local E2E tests.");
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connectionStringBuilder.Database)
            || !string.Equals(
                connectionStringBuilder.Database,
                "gaifulinlab",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Local E2E tests must target the 'gaifulinlab' development database.");
        }

        _connectionString = connectionStringBuilder.ConnectionString;
        connectionStringBuilder.Host = "host.docker.internal";
        _dockerConnectionString = connectionStringBuilder.ConnectionString;
    }

    private async Task EnsureDevelopmentDatabaseExistsAsync()
    {
        var developmentConnectionString = new NpgsqlConnectionStringBuilder(_connectionString);
        var databaseName = developmentConnectionString.Database;
        if (string.IsNullOrWhiteSpace(databaseName)
            || databaseName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException(
                "The local development database name may contain only letters, digits, and underscores.");
        }

        var maintenanceConnectionString = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Database = "postgres"
        };

        try
        {
            await using var connection = new NpgsqlConnection(maintenanceConnectionString.ConnectionString);
            await connection.OpenAsync();

            await using var existsCommand = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @databaseName",
                connection);
            existsCommand.Parameters.AddWithValue("databaseName", databaseName);
            if (await existsCommand.ExecuteScalarAsync() is not null)
            {
                return;
            }

            await using var createCommand = new NpgsqlCommand(
                $"CREATE DATABASE \"{databaseName}\"",
                connection);
            await createCommand.ExecuteNonQueryAsync();
        }
        catch (PostgresException exception) when (exception.SqlState is "42501" or "3D000")
        {
            throw new InvalidOperationException(
                $"Could not create the development database '{databaseName}'. Create it once with a PostgreSQL account that has CREATEDB permission, then rerun the tests.",
                exception);
        }
        catch (PostgresException exception) when (exception.SqlState == "42P04")
        {
            // Another E2E process created the database after the existence check.
        }
        catch (NpgsqlException exception) when (exception.InnerException is SocketException)
        {
            throw new InvalidOperationException(
                $"Could not connect to the local development database '{databaseName}'. Start PostgreSQL at {developmentConnectionString.Host}:{developmentConnectionString.Port} or update GAIFULINLAB_E2E_CONNECTION_STRING in e2e.runsettings.",
                exception);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaifulinLab.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root for E2E startup.");
    }
}

public sealed record SeededTopic(Guid Id, string Name, string Slug, string? Description);

public sealed record SeededSeries(Guid Id, string Title, string Slug, string? Description);

public sealed record SeededArticle(Guid Id, string Title, string Slug);

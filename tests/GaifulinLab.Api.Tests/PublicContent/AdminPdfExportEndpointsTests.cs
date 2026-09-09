using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Api.Tests.Authentication;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Application.Media;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GaifulinLab.Api.Tests.PublicContent;

public sealed class AdminPdfExportEndpointsTests
{
    [Fact]
    public async Task ArticlePdfExport_ForPublishedLocalization_QueuesExport()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var anonymousClient = factory.CreateClient();
        using var userClient = await CreateUserClientAsync(factory);
        using var adminClient = await CreateAdminClientAsync(factory);
        using var ownerClient = await CreateOwnerClientAsync(factory);

        var unauthorized = await anonymousClient.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            content: null);
        var foreign = await userClient.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var adminResponse = await adminClient.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, adminResponse.StatusCode);

        var response = await ownerClient.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var export = await response.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        Assert.NotNull(export);
        Assert.Equal("queued", export.Status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}/download")).StatusCode);

        var status = await ownerClient.GetFromJsonAsync<PdfExportStatusDto>(
            $"/api/admin/pdf-exports/{export.Id}");
        Assert.Equal("queued", status?.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}/download")).StatusCode);
    }

    [Fact]
    public async Task ArticlePdfExport_ForSavedDraftAndUnpublishedLocalizations_QueuesExport()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = await CreateOwnerClientAsync(factory);

        var draft = await client.PostAsync(
            "/api/admin/articles/en/future-draft/pdf-exports", null);
        Assert.Equal(HttpStatusCode.Accepted, draft.StatusCode);
        var draftExport = await draft.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        Assert.NotNull(draftExport);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var article = dbContext.Articles.Include(item => item.Localizations).Single(item => item.Localizations.Any(
                localization => localization.Slug == "understanding-fft"));
            article.UnpublishLocalization("en", DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        var unpublished = await client.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports", null);
        Assert.Equal(HttpStatusCode.Accepted, unpublished.StatusCode);
        var unpublishedExport = await unpublished.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        Assert.NotNull(unpublishedExport);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/admin/pdf-exports/{draftExport.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/admin/pdf-exports/{unpublishedExport.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(
            $"/api/admin/pdf-exports/{draftExport.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(
            $"/api/admin/pdf-exports/{unpublishedExport.Id}/download")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(
            "/api/admin/articles/en/deleted-published/pdf-exports", null)).StatusCode);
    }

    [Fact]
    public async Task ArticlePdfExport_WhenNotReady_ReturnsConflictOnDownload()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = await CreateOwnerClientAsync(factory);

        var created = await client.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports",
            content: null);
        var export = await created.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        var response = await client.GetAsync($"/api/admin/pdf-exports/{export!.Id}/download");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("pdf_export_not_ready", error?.Code);
    }

    [Fact]
    public async Task ArticlePdfExport_ReusesOneJobAndRefreshesItsSnapshot()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = await CreateOwnerClientAsync(factory);

        var first = await client.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4", null);
        var second = await client.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.8&blockSpacing=0.6", null);

        var firstJob = await first.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        var secondJob = await second.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(firstJob?.Id, secondJob?.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await dbContext.PdfExportJobs.CountAsync());
        var job = await dbContext.PdfExportJobs.SingleAsync();
        Assert.Equal(1.8m, job.LineHeight);
        Assert.Equal(0.6m, job.BlockSpacing);
        Assert.Equal(1, job.GenerationVersion);
    }

    [Fact]
    public async Task ReaderPdfDownload_IsDisabledUntilTheFeatureIsEnabled()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = await CreateUserClientAsync(factory);

        var response = await client.GetAsync("/api/public/articles/en/understanding-fft/pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReaderPdfDownload_ServesReadyPublishedFileAndDoesNotChargeTwice()
    {
        await using var factory = new AuthWebApplicationFactory(publicPdfDownloadEnabled: true);
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = await CreateUserClientAsync(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
            var localization = await dbContext.ArticleLocalizations.SingleAsync(item => item.Slug == "understanding-fft");
            var job = PdfExportJob.Create(localization, 1.5m, 0.4m, DateTimeOffset.UtcNow);
            job.Complete("pdf-exports/reader-test.pdf", 9, DateTimeOffset.UtcNow);
            dbContext.PdfExportJobs.Add(job);
            await using var stream = new MemoryStream("%PDF-test"u8.ToArray());
            await storage.SaveAsync("pdf-exports/reader-test.pdf", stream, CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        using var first = await client.GetAsync("/api/public/articles/en/understanding-fft/pdf");
        using var second = await client.GetAsync("/api/public/articles/en/understanding-fft/pdf");

        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        _ = await first.Content.ReadAsByteArrayAsync();
        _ = await second.Content.ReadAsByteArrayAsync();
        first.Dispose();
        second.Dispose();
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await verificationDbContext.PdfDownloadUsages.CountAsync());
        var verificationStorage = verificationScope.ServiceProvider.GetRequiredService<IMediaStorage>();
        await verificationStorage.DeleteAsync("pdf-exports/reader-test.pdf", CancellationToken.None);
    }

    [Fact]
    public async Task ExistingPdfExport_BecomesUnavailableWhenItsArticleIsDeleted()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = await CreateOwnerClientAsync(factory);

        var created = await client.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports",
            content: null);
        var export = await created.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        Assert.NotNull(export);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var articleId = dbContext.ArticleLocalizations.Single(
                localization => localization.Slug == "understanding-fft").ArticleId;
            dbContext.Articles.Single(item => item.Id == articleId).Delete(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/admin/pdf-exports/{export!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}/download")).StatusCode);
    }

    private static Task<HttpClient> CreateAdminClientAsync(AuthWebApplicationFactory factory) =>
        CreateAuthenticatedClientAsync(factory, AuthWebApplicationFactory.AdminLogin, AuthWebApplicationFactory.AdminPassword);

    private static async Task<HttpClient> CreateOwnerClientAsync(AuthWebApplicationFactory factory)
    {
        const string password = "Strong-password-1!";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var owner = await userManager.FindByIdAsync("test-owner");
            Assert.NotNull(owner);
            var result = await userManager.AddPasswordAsync(owner, password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        return await CreateAuthenticatedClientAsync(factory, "private-owner@example.com", password);
    }

    private static async Task<HttpClient> CreateUserClientAsync(AuthWebApplicationFactory factory)
    {
        var login = $"pdf-user-{Guid.NewGuid():N}@example.com";
        const string password = "Strong-password-1!";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await userManager.CreateAsync(
                new ApplicationUser { UserName = login, DisplayName = "PDF User" },
                password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        return await CreateAuthenticatedClientAsync(factory, login, password);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        AuthWebApplicationFactory factory,
        string login,
        string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authenticationService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var token = await authenticationService.AuthenticateAsync(login, password);
        Assert.NotNull(token);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Api.Tests.Authentication;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
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

        var unauthorized = await anonymousClient.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            content: null);
        var forbidden = await userClient.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var response = await adminClient.PostAsync(
            "/api/admin/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var export = await response.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        Assert.NotNull(export);
        Assert.Equal("queued", export.Status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}/download")).StatusCode);

        var status = await adminClient.GetFromJsonAsync<PdfExportStatusDto>(
            $"/api/admin/pdf-exports/{export.Id}");
        Assert.Equal("queued", status?.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.GetAsync(
            $"/api/admin/pdf-exports/{export.Id}/download")).StatusCode);
    }

    [Fact]
    public async Task ArticlePdfExport_ForSavedDraftAndUnpublishedLocalizations_QueuesExport()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = await CreateAdminClientAsync(factory);

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
        using var client = await CreateAdminClientAsync(factory);

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
    public async Task ExistingPdfExport_BecomesUnavailableWhenItsArticleIsDeleted()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = await CreateAdminClientAsync(factory);

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

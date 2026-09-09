using System.Data;
using System.Security.Claims;
using GaifulinLab.Application.Common;
using GaifulinLab.Application.Media;
using GaifulinLab.Domain.Common;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/public/articles")]
public sealed class PublicPdfDownloadController(
    AppDbContext dbContext,
    IMediaStorage mediaStorage,
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory) : ControllerBase
{
    [HttpGet("{languageCode}/{slug}/pdf")]
    public async Task<IActionResult> Download(string languageCode, string slug, CancellationToken cancellationToken)
    {
        // The API guard is independent of the WASM setting: hidden UI must not
        // turn into an accidentally public capability.
        if (!configuration.GetValue<bool>("Features:PublicPdfDownloadEnabled"))
        {
            throw new ResourceNotFoundException("PDF download", $"{languageCode}/{slug}");
        }

        var normalizedLanguage = DomainRules.NormalizeLanguageCode(languageCode);
        var normalizedSlug = DomainRules.NormalizeSlug(slug);
        var job = await dbContext.PdfExportJobs
            .AsNoTracking()
            .Where(candidate => candidate.LanguageCode == normalizedLanguage
                && candidate.Slug == normalizedSlug
                && candidate.RelativePath != null
                && dbContext.ArticleLocalizations.Any(localization =>
                    localization.Id == candidate.ArticleLocalizationId
                    && localization.PublishedAt != null
                    && localization.Article.DeletedAt == null))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("PDF download", $"{normalizedLanguage}/{normalizedSlug}");

        var content = await mediaStorage.OpenReadAsync(job.RelativePath!, cancellationToken);
        if (content is null)
        {
            throw new ResourceNotFoundException("PDF export file", job.Id.ToString());
        }

        if (!await ReserveQuotaAsync(GetCurrentUserId(), job, cancellationToken))
        {
            await content.DisposeAsync();
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new Contracts.Common.ApiErrorResponse(
                    "pdf_download_quota_exceeded",
                    "The monthly PDF download limit has been reached."));
        }

        await using (content)
        await using (var buffer = new MemoryStream())
        {
            await content.CopyToAsync(buffer, cancellationToken);
            Response.Headers.CacheControl = "private, no-store";
            return File(buffer.ToArray(), "application/pdf", $"{job.Slug}.pdf");
        }
    }

    private async Task<bool> ReserveQuotaAsync(string userId, PdfExportJob job, CancellationToken cancellationToken)
    {
        var period = DateOnly.FromDateTime(DateTime.UtcNow);
        var month = new DateOnly(period.Year, period.Month, 1);
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var reservationDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try
            {
                await using var transaction = reservationDb.Database.IsRelational()
                    ? await reservationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                    : null;
                var alreadyDownloaded = await reservationDb.PdfDownloadUsages.AnyAsync(usage =>
                    usage.UserId == userId && usage.PdfExportJobId == job.Id
                    && usage.RelativePath == job.RelativePath && usage.Period == month, cancellationToken);
                if (alreadyDownloaded)
                {
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return true;
                }

                var tier = await reservationDb.Users
                    .Where(user => user.Id == userId)
                    .Select(user => user.PdfSubscriptionTier)
                    .SingleAsync(cancellationToken);
                var limit = tier == PdfSubscriptionTier.Paid ? 10 : 1;
                var usage = await reservationDb.PdfDownloadUsages.CountAsync(item =>
                    item.UserId == userId && item.Period == month, cancellationToken);
                if (usage >= limit)
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }

                    return false;
                }

                reservationDb.PdfDownloadUsages.Add(new PdfDownloadUsage(userId, job.Id, job.RelativePath!, month));
                await reservationDb.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return true;
            }
            catch (Exception exception) when (IsRetryableReservationConflict(exception) && attempt < maxAttempts)
            {
                // A serializable PostgreSQL transaction or the per-file unique
                // index can lose a race. A fresh scope sees the committed usage.
            }
        }

        // The loop only reaches this point after retryable contention. Treating
        // it as unavailable prevents issuing an uncharged download.
        return false;
    }

    private static bool IsRetryableReservationConflict(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException postgres }
            && (postgres.SqlState == PostgresErrorCodes.SerializationFailure
                || postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        || exception is PostgresException directPostgres
            && (directPostgres.SqlState == PostgresErrorCodes.SerializationFailure
                || directPostgres.SqlState == PostgresErrorCodes.UniqueViolation);

    private string GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated user does not have an identifier.");
}

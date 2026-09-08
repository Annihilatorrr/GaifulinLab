using GaifulinLab.Application.Common;
using GaifulinLab.Application.Media;
using GaifulinLab.Application.Pdf;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GaifulinLab.Infrastructure.Pdf;

internal sealed class PdfExportWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    PdfExportWorkerSettings settings,
    ILogger<PdfExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("The PDF export worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await ClaimNextAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(settings.PollInterval, stoppingToken);
                    continue;
                }

                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The PDF export worker loop failed.");
                await Task.Delay(settings.PollInterval, stoppingToken);
            }
        }
    }

    private async Task<PdfExportSnapshot?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var job = await dbContext.PdfExportJobs
                .FromSqlRaw("""
                    SELECT * FROM "pdf_export_jobs"
                    WHERE "Status" = 'Queued'
                       OR ("Status" = 'Processing' AND "LeaseExpiresAt" < NOW())
                    ORDER BY "CreatedAt"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (job is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            job.Start(now, settings.LeaseDuration);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PdfExportSnapshot(
                job.Id,
                new ArticlePdfDocument(
                    job.LanguageCode,
                    job.Slug,
                    job.Title,
                    job.Summary,
                    job.Markdown,
                    job.PublishedAt),
                new ArticleTypography(job.LineHeight, job.BlockSpacing),
                job.AttemptCount);
        });
    }

    private async Task ProcessAsync(PdfExportSnapshot job, CancellationToken cancellationToken)
    {
        string? relativePath = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var renderer = scope.ServiceProvider.GetRequiredService<IArticlePdfRenderer>();
            var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
            var pdf = await renderer.RenderAsync(job.Document, job.Typography, cancellationToken);
            relativePath = $"pdf-exports/{job.Id:N}-{job.AttemptCount}.pdf";
            await using var content = new MemoryStream(pdf, writable: false);
            await storage.SaveAsync(relativePath, content, cancellationToken);

            if (!await CompleteAsync(job, relativePath, pdf.LongLength, cancellationToken))
            {
                await DeleteOutputAsync(relativePath);
                logger.LogInformation(
                    "Discarded PDF export {PdfExportId} from superseded attempt {AttemptCount}.",
                    job.Id,
                    job.AttemptCount);
                return;
            }

            logger.LogInformation("Generated PDF export {PdfExportId} ({Size} bytes).", job.Id, pdf.LongLength);
        }
        catch (PdfRenderingException exception)
        {
            await FailAsync(job.Id, job.AttemptCount, exception.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "PDF export {PdfExportId} failed.", job.Id);
            await FailAsync(job.Id, job.AttemptCount, "The PDF export failed.", cancellationToken);
        }
    }

    private async Task<bool> CompleteAsync(
        PdfExportSnapshot job,
        string relativePath,
        long outputSize,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var completedAt = timeProvider.GetUtcNow();
        // A lease may have been reclaimed while rendering, so only this active attempt may complete.
        var changed = await dbContext.PdfExportJobs
            .Where(candidate =>
                candidate.Id == job.Id
                && candidate.AttemptCount == job.AttemptCount
                && candidate.Status == PdfExportStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.Status, PdfExportStatus.Completed)
                .SetProperty(candidate => candidate.RelativePath, relativePath)
                .SetProperty(candidate => candidate.OutputSize, outputSize)
                .SetProperty(candidate => candidate.CompletedAt, completedAt)
                .SetProperty(candidate => candidate.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.ErrorMessage, (string?)null), cancellationToken);

        return changed == 1;
    }

    private async Task FailAsync(
        Guid jobId,
        int attemptCount,
        string message,
        CancellationToken cancellationToken)
    {
        var errorMessage = string.IsNullOrWhiteSpace(message)
            ? "The PDF export failed."
            : message[..Math.Min(message.Length, 1_000)];

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Do not let an expired attempt mark the worker that reclaimed its lease as failed.
        await dbContext.PdfExportJobs
            .Where(candidate =>
                candidate.Id == jobId
                && candidate.AttemptCount == attemptCount
                && candidate.Status == PdfExportStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.Status, PdfExportStatus.Failed)
                .SetProperty(candidate => candidate.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.ErrorMessage, errorMessage), cancellationToken);
    }

    private async Task DeleteOutputAsync(string? relativePath)
    {
        if (relativePath is null)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
            await storage.DeleteAsync(relativePath, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not delete discarded PDF export at {RelativePath}.", relativePath);
        }
    }

    private sealed record PdfExportSnapshot(
        Guid Id,
        ArticlePdfDocument Document,
        ArticleTypography Typography,
        int AttemptCount);
}

internal sealed record PdfExportWorkerSettings(TimeSpan PollInterval, TimeSpan LeaseDuration);

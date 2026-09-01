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
        try
        {
            using var scope = scopeFactory.CreateScope();
            var renderer = scope.ServiceProvider.GetRequiredService<IArticlePdfRenderer>();
            var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
            var pdf = await renderer.RenderAsync(job.Document, job.Typography, cancellationToken);
            var relativePath = $"pdf-exports/{job.Id:N}-{job.AttemptCount}.pdf";
            await using var content = new MemoryStream(pdf, writable: false);
            await storage.SaveAsync(relativePath, content, cancellationToken);

            using var completionScope = scopeFactory.CreateScope();
            var dbContext = completionScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persisted = await dbContext.PdfExportJobs.SingleAsync(candidate => candidate.Id == job.Id, cancellationToken);
            persisted.Complete(relativePath, pdf.LongLength, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Generated PDF export {PdfExportId} ({Size} bytes).", job.Id, pdf.LongLength);
        }
        catch (PdfRenderingException exception)
        {
            await FailAsync(job.Id, exception.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "PDF export {PdfExportId} failed.", job.Id);
            await FailAsync(job.Id, "The PDF export failed.", cancellationToken);
        }
    }

    private async Task FailAsync(Guid jobId, string message, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await dbContext.PdfExportJobs.SingleOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
        if (job is null || job.Status != PdfExportStatus.Processing)
        {
            return;
        }

        job.Fail(message);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record PdfExportSnapshot(
        Guid Id,
        ArticlePdfDocument Document,
        ArticleTypography Typography,
        int AttemptCount);
}

internal sealed record PdfExportWorkerSettings(TimeSpan PollInterval, TimeSpan LeaseDuration);

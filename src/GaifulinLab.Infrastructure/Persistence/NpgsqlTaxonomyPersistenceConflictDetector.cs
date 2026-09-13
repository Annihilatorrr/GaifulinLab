using GaifulinLab.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GaifulinLab.Infrastructure.Persistence;

internal sealed class NpgsqlTaxonomyPersistenceConflictDetector : ITaxonomyPersistenceConflictDetector
{
    public TaxonomyPersistenceConflict Detect(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.InnerException is not PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: var constraintName
            })
        {
            return TaxonomyPersistenceConflict.None;
        }

        return constraintName switch
        {
            "ux_topic_localizations_language_slug" or "ux_series_localizations_language_slug" =>
                TaxonomyPersistenceConflict.SlugAlreadyUsed,
            "ux_topic_localizations_topic_language" or "ux_series_localizations_series_language" =>
                TaxonomyPersistenceConflict.LocalizationAlreadyExists,
            _ => TaxonomyPersistenceConflict.None
        };
    }
}

using GaifulinLab.Application.Persistence;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GaifulinLab.Infrastructure.Tests.Persistence;

public sealed class NpgsqlTaxonomyPersistenceConflictDetectorTests
{
    private readonly NpgsqlTaxonomyPersistenceConflictDetector _detector = new();

    [Theory]
    [InlineData("ux_topic_localizations_language_slug")]
    [InlineData("ux_series_localizations_language_slug")]
    public void Detect_WhenLanguageSlugConstraintIsViolated_ReturnsSlugAlreadyUsed(string constraintName)
    {
        var result = _detector.Detect(UniqueViolation(constraintName));

        Assert.Equal(TaxonomyPersistenceConflict.SlugAlreadyUsed, result);
    }

    [Theory]
    [InlineData("ux_topic_localizations_topic_language")]
    [InlineData("ux_series_localizations_series_language")]
    public void Detect_WhenEntityLanguageConstraintIsViolated_ReturnsLocalizationAlreadyExists(string constraintName)
    {
        var result = _detector.Detect(UniqueViolation(constraintName));

        Assert.Equal(TaxonomyPersistenceConflict.LocalizationAlreadyExists, result);
    }

    [Fact]
    public void Detect_WhenTheExceptionIsNotAKnownTaxonomyConstraint_ReturnsNone()
    {
        var result = _detector.Detect(UniqueViolation("unrelated_unique_constraint"));

        Assert.Equal(TaxonomyPersistenceConflict.None, result);
    }

    private static DbUpdateException UniqueViolation(string constraintName) => new(
        "Persistence failure.",
        new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            null,
            null,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            constraintName,
            null,
            null,
            null));
}

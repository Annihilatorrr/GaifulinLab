using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Persistence;

public interface ITaxonomyPersistenceConflictDetector
{
    TaxonomyPersistenceConflict Detect(DbUpdateException exception);
}

public enum TaxonomyPersistenceConflict
{
    None = 0,
    SlugAlreadyUsed,
    LocalizationAlreadyExists
}

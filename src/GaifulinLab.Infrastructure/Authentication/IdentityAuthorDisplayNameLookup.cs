using GaifulinLab.Application.Authors;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Infrastructure.Authentication;

internal sealed class IdentityAuthorDisplayNameLookup(AppDbContext dbContext) : IAuthorDisplayNameLookup
{
    public async Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        return await dbContext.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
    }
}

namespace GaifulinLab.Application.Authors;

public interface IAuthorDisplayNameLookup
{
    Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);
}

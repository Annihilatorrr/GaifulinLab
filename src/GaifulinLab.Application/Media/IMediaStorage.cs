namespace GaifulinLab.Application.Media;

public interface IMediaStorage
{
    Task SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

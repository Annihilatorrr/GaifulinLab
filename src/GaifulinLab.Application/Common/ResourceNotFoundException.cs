namespace GaifulinLab.Application.Common;

public sealed class ResourceNotFoundException(string resourceName, object resourceId)
    : Exception($"{resourceName} '{resourceId}' was not found.")
{
}

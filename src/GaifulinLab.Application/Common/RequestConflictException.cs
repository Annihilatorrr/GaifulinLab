namespace GaifulinLab.Application.Common;

public sealed class RequestConflictException(string message, string code = "conflict") : Exception(message)
{
    public string Code { get; } = code;
}

namespace GaifulinLab.Application.Common;

public sealed class RequestConflictException(string message) : Exception(message);

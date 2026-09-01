namespace GaifulinLab.Application.Common;

public sealed class PdfRenderingException(string message, Exception? innerException = null)
    : Exception(message, innerException);

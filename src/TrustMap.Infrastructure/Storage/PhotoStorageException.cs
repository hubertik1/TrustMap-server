namespace TrustMap.Infrastructure.Storage;

public sealed class PhotoStorageException : Exception
{
    public PhotoStorageException(string message, string errorCode, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

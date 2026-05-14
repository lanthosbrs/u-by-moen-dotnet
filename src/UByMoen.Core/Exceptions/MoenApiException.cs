namespace UByMoen.Core.Exceptions;

public class MoenApiException : Exception
{
    public MoenApiException(string message) : base(message) { }
    public MoenApiException(string message, Exception innerException) : base(message, innerException) { }
}

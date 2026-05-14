namespace UByMoen.Core.Exceptions;

public class MoenAuthException : MoenApiException
{
    public MoenAuthException(string message) : base(message) { }
    public MoenAuthException(string message, Exception innerException) : base(message, innerException) { }
}

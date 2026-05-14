namespace GNSSR.Host.Infrastructure.Serial.Protocol;

public sealed class FrontendProtocolException : Exception
{
    public FrontendProtocolException(string message)
        : base(message)
    {
    }

    public FrontendProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

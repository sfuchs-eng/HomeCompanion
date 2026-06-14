namespace HomeCompanion.Logics.Shutters;

public class ShutterTargetProcessingException : Exception
{
    public ShutterKey ShutterKey { get; }

    public ShutterTargetProcessingException(ShutterKey shutterKey, string message) : base(message)
    {
        ShutterKey = shutterKey;
    }

    public ShutterTargetProcessingException(ShutterKey shutterKey, string message, Exception innerException) : base(message, innerException)
    {
        ShutterKey = shutterKey;
    }
}

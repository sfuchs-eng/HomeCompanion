namespace HomeCompanion.Values;

public class ValueException : Exception
{
    public DateTimeOffset TimeStamp { get; init; } = System.TimeProvider.System.GetLocalNow();
    
    public ValueException(string message) : base(message)
    {
    }

    public ValueException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class ValueReceptionException : ValueException
{
    public object SourceBusIdentifier { get; }
    public IValueBusEndpointMapping EndpointMapping { get; }

    public ValueReceptionException(object sourceBusIdentifier, IValueBusEndpointMapping endpointMapping, string message) : base(message)
    {
        SourceBusIdentifier = sourceBusIdentifier;
        EndpointMapping = endpointMapping;
    }

    public ValueReceptionException(object sourceBusIdentifier, IValueBusEndpointMapping endpointMapping, string message, Exception innerException) : base(message, innerException)
    {
        SourceBusIdentifier = sourceBusIdentifier;
        EndpointMapping = endpointMapping;
    }
}
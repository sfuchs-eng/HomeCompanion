namespace HomeCompanion;

/// <summary>
/// Marks connectivity providers that must be registered explicitly by an extension
/// instead of being discovered automatically by <c>AddConnectivityProviders</c>.
/// Use this for providers whose registration must be gated by configuration or other extension-specific logic.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ManualConnectivityProviderRegistrationAttribute : Attribute
{
}

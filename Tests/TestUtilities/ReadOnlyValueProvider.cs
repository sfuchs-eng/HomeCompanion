using HomeCompanion.Values;

namespace HomeCompanion.Tests.TestUtilities;

/// <summary>
/// A read-only value provider that resolves IValue instances by their reference.
/// </summary>
/// <typeparam name="string">The type of the reference key.</typeparam>
/// <typeparam name="IValue">The type of the value.</typeparam>
internal sealed class ReadOnlyValueProvider(IReadOnlyDictionary<string, IValue> byReference) : IValueProvider
{
    public void Add(string reference, IValue value) => throw new NotSupportedException("ReadOnlyValueProvider does not support adding new values.");

    public IValue Resolve(string reference) => byReference[reference];

    public bool TryResolve(string reference, out IValue? value)
        => byReference.TryGetValue(reference, out value);

    public bool TryResolve<T>(string reference, out IValue<T>? value)
    {
        if (byReference.TryGetValue(reference, out var untyped) && untyped is IValue<T> typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }
}

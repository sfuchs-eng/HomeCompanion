using HomeCompanion.Diagnostics;

namespace HomeCompanion.Diagnostics;

public class DiagnosticEntityKey : IDiagnosticEntityKey
{
    public string Key { get; }

    public DiagnosticEntityKey(string key)
    {
        Key = key;
    }
    
    public DiagnosticEntityKey(IEnumerable<string> pathSegments)
    {
        Key = string.Join('/', pathSegments);
    }
}

public static class DiagnosticEntityKeyExtensions
{
    public static IDiagnosticEntityKey ToDiagnosticEntityKey(this IEnumerable<string> pathSegments)
    {
        return new DiagnosticEntityKey(pathSegments);
    }

    public static string[] GetPathSegments(this IDiagnosticEntityKey key)
    {
        return key.Key.Split('/');
    }

}
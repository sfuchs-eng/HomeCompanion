namespace HomeCompanion.Diagnostics;

public interface IDiagnosticBrowser
{
    IEnumerable<IDiagnosable> GetDiagnosables();

    Task<IDiagnosticResultNode> GetDiagnosisAsync(IDiagnosable diagnosable, CancellationToken cancellationToken);

    Task<IDiagnosticResultNode> GetDiagnosisAsync(string diagnosableName, CancellationToken cancellationToken);

    IDiagnosticResultNode? ResolveNode(IDiagnosticResultNode root, IDiagnosticEntityKey key);

    IDiagnosticEntityKey? GetKey(IDiagnosticResultNode root, IDiagnosticResultNode node);
}

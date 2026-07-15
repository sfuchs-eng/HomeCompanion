namespace HomeCompanion.Diagnostics;

public class DiagnosticResultNode : IDiagnosticResultNode
{
    public virtual required string Name { get; init; }

    public List<IDiagnosticResultNode> Children { get; set; } = [];

    public List<IDiagnosticRecord> Records { get; set; } = [];

    IEnumerable<IDiagnosticRecord> IDiagnosticResultNode.Records => Records;

    IEnumerable<IDiagnosticResultNode> IDiagnosticResultNode.Children => Children;

    public static DiagnosticResultNode Create(string name, string singleMessage)
    {
        return new DiagnosticResultNode
        {
            Name = name,
            Records = [ new DiagnosticRecord(name, singleMessage) ],
            Children = []
        };
    }
}

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
            Records = [new DiagnosticRecord(name, singleMessage)],
            Children = []
        };
    }

    public static DiagnosticResultNode Create(string name)
    {
        return new DiagnosticResultNode
        {
            Name = name,
            Records = [],
            Children = []
        };
    }

    /// <summary>
    /// Craetes and adds a child node to the current node and returns it.
    /// </summary>
    /// <param name="name"></param>
    /// <returns>The new child node.</returns>
    public DiagnosticResultNode AddChild(string name)
    {
        var child = new DiagnosticResultNode { Name = name };
        Children.Add(child);
        return child;
    }
    
    public DiagnosticResultNode AddChilds(string name, IEnumerable<IDiagnosticResultNode> childNodes)
    {
        var child = new DiagnosticResultNode { Name = name };
        child.Children.AddRange(childNodes);
        Children.Add(child);
        return child;
    }

    /// <summary>
    /// Creates and adds a diagnostic record to the current node and returns the node (not the new record).
    /// </summary>
    /// <param name="name"></param>
    /// <param name="explanation"></param>
    /// <param name="value"></param>
    /// <returns>The current node (not the new record).</returns>
    public DiagnosticResultNode AddRecord(string name, IDiagnosticValue? value = null, string? explanation = null)
    {
        Records.Add(new DiagnosticRecord(name, value, explanation));
        return this;
    }

    public DiagnosticResultNode AddRecord(string name, string? message, string? explanation = null)
    {
        Records.Add(new DiagnosticRecord(name, message, explanation));
        return this;
    }
}

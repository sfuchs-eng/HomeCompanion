using HomeCompanion.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Core.Diagnostics;

public class DiagnosticBrowser(IServiceProvider serviceProvider, ILogger<DiagnosticBrowser> logger) : IDiagnosticBrowser
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly ILogger<DiagnosticBrowser> logger = logger;

    /// <summary>
    /// Return all diagnosable entities registered in the service collection.
    /// </summary>
    public IEnumerable<IDiagnosable> GetDiagnosables()
    {
        var diagnosables = serviceProvider.GetServices<IDiagnosable>();
        return diagnosables;
    }

    /// <summary>
    /// Gets the diagnosis for the specified diagnosable entity.
    /// </summary>
    /// <param name="diagnosable">The diagnosable entity to get the diagnosis for.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the root node of the diagnostic result tree, or null if no diagnosis is available.</returns>
    public Task<IDiagnosticResultNode> GetDiagnosisAsync(IDiagnosable diagnosable, CancellationToken cancellationToken)
    {
        return diagnosable.GetDiagnosisAsync(cancellationToken);
    }

    public Task<IDiagnosticResultNode> GetDiagnosisAsync(string diagnosableName, CancellationToken cancellationToken)
    {
        var diagnosable = GetDiagnosables().FirstOrDefault(d => d.Name == diagnosableName);
        if (diagnosable == null)
        {
            return Task.FromResult<IDiagnosticResultNode>(DiagnosticResultNode.Create(nameof(DiagnosticBrowser), $"No IDiagnosable named '{diagnosableName}' was found."));
        }

        return GetDiagnosisAsync(diagnosable, cancellationToken);
    }

    public IDiagnosticEntityKey? GetKey(IDiagnosticResultNode root, IDiagnosticResultNode node)
    {
        // recursively search the tree for the node and build the key based on the path from root to node
        var path = new List<string>();
        if (BuildPath(root, node, path))
        {
            return new DiagnosticEntityKey(path);
        }

        return null;
    }

    private static bool BuildPath(IDiagnosticResultNode root, IDiagnosticResultNode node, List<string> path)
    {
        if (root == node)
        {
            path.Insert(0, root.Name);
            return true;
        }

        foreach (var child in root.Children)
        {
            if (BuildPath(child, node, path))
            {
                path.Insert(0, root.Name);
                return true;
            }
        }

        return false;
    }

    public IDiagnosticResultNode? ResolveNode(IDiagnosticResultNode root, IDiagnosticEntityKey key)
    {
        var pathSegments = key.GetPathSegments();
        return ResolveNodeRecursive(root, pathSegments, 0);
    }

    private static IDiagnosticResultNode? ResolveNodeRecursive(IDiagnosticResultNode root, string[] pathSegments, int v)
    {
        if (v >= pathSegments.Length)
        {
            return root;
        }

        var segment = pathSegments[v];
        var child = root.Children.FirstOrDefault(c => c.Name == segment);
        if (child == null)
        {
            return null;
        }

        return ResolveNodeRecursive(child, pathSegments, v + 1);
    }
}

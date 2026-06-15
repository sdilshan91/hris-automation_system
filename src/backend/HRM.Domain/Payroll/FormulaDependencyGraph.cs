namespace HRM.Domain.Payroll;

/// <summary>
/// Detects circular references among formula-based salary components (US-PAY-001 BR-6). Each formula
/// component depends on the component codes its expression references; a cycle (e.g. A references B
/// which references A, or A references itself) is rejected before save.
/// </summary>
public static class FormulaDependencyGraph
{
    /// <summary>
    /// Given a map of component code → the codes its formula references (case-insensitive), returns the
    /// first cycle found as an ordered list of codes (e.g. [A, B, A]), or null when the graph is acyclic.
    /// Edges to codes not present in <paramref name="dependencies"/> (non-formula or external) are ignored
    /// for cycle purposes — they are leaves.
    /// </summary>
    public static IReadOnlyList<string>? FindCycle(IReadOnlyDictionary<string, IReadOnlyCollection<string>> dependencies)
    {
        var graph = new Dictionary<string, IReadOnlyCollection<string>>(dependencies, StringComparer.OrdinalIgnoreCase);

        var state = new Dictionary<string, Mark>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        foreach (var node in graph.Keys)
        {
            var cycle = Visit(node, graph, state, stack);
            if (cycle is not null)
                return cycle;
        }

        return null;
    }

    private enum Mark { Visiting, Done }

    private static IReadOnlyList<string>? Visit(
        string node,
        Dictionary<string, IReadOnlyCollection<string>> graph,
        Dictionary<string, Mark> state,
        List<string> stack)
    {
        if (state.TryGetValue(node, out var mark))
        {
            if (mark == Mark.Visiting)
            {
                // Found a back-edge: extract the cycle from the stack.
                var startIdx = stack.FindIndex(n => string.Equals(n, node, StringComparison.OrdinalIgnoreCase));
                var cycle = stack.Skip(startIdx).ToList();
                cycle.Add(node); // close the loop
                return cycle;
            }
            return null; // Done — already fully explored, no cycle through here.
        }

        state[node] = Mark.Visiting;
        stack.Add(node);

        if (graph.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps)
            {
                // Only traverse edges into nodes that are themselves formula components (in the graph).
                if (!graph.ContainsKey(dep))
                    continue;
                var cycle = Visit(dep, graph, state, stack);
                if (cycle is not null)
                    return cycle;
            }
        }

        stack.RemoveAt(stack.Count - 1);
        state[node] = Mark.Done;
        return null;
    }
}

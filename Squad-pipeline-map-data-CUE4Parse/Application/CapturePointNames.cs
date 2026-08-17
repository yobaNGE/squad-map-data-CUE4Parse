using Squad_pipeline_map_data_CUE4Parse.Domain;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal static class CapturePointNames
{
    public const string Team1Main = "00-Team1 Main";
    public const string Team2Main = "Z-Team2 Main";

    public static IReadOnlyList<CaptureLink> NormalizeMains(IReadOnlyList<CaptureLink> links)
    {
        var mainPaths = links
            .SelectMany(link => new[]
            {
                (link.NodeAPath, link.NodeAIsMain),
                (link.NodeBPath, link.NodeBIsMain)
            })
            .Where(node => node.Item2)
            .Select(node => node.Item1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mainPaths.Length != 2) return links;

        var incoming = links.Select(link => link.NodeBPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outgoing = links.Select(link => link.NodeAPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = mainPaths.Where(path => !incoming.Contains(path)).ToArray();
        var targets = mainPaths.Where(path => !outgoing.Contains(path)).ToArray();
        if (sources.Length != 1 || targets.Length != 1) return links;

        return links.Select(link => link with
        {
            NodeA = NameFor(link.NodeAPath, link.NodeA),
            NodeB = NameFor(link.NodeBPath, link.NodeB)
        }).ToArray();

        string NameFor(string path, string fallback) =>
            path.Equals(sources[0], StringComparison.OrdinalIgnoreCase) ? Team1Main :
            path.Equals(targets[0], StringComparison.OrdinalIgnoreCase) ? Team2Main : fallback;
    }

    public static IReadOnlyDictionary<string, string> ByPath(IEnumerable<CaptureLink>? links)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links ?? [])
        {
            names.TryAdd(link.NodeAPath, link.NodeA);
            names.TryAdd(link.NodeBPath, link.NodeB);
        }
        return names;
    }
}

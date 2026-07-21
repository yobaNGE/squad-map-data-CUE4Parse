using System.IO;

namespace Squad_pipeline_map_data_CUE4Parse.Configuration;

public enum ContentLayoutKind
{
    Cooked,
    EditorSdk
}

public sealed record ContentLayout(ContentLayoutKind Kind, DirectoryInfo Root, DirectoryInfo ContentDirectory)
{
    public bool IsEditorSdk => Kind == ContentLayoutKind.EditorSdk;
}

public static class ContentLayoutDetector
{
    public static ContentLayout Detect(string path)
    {
        var selected = new DirectoryInfo(string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : path);
        var sdkRoot = FindSdkRoot(selected);
        if (sdkRoot is not null)
            return new ContentLayout(ContentLayoutKind.EditorSdk, sdkRoot, new DirectoryInfo(Path.Combine(sdkRoot.FullName, "Content")));

        var paks = ResolvePaksDirectory(selected);
        return new ContentLayout(ContentLayoutKind.Cooked, selected, paks);
    }

    private static DirectoryInfo? FindSdkRoot(DirectoryInfo selected)
    {
        var candidates = selected.Name.Equals("Content", StringComparison.OrdinalIgnoreCase) && selected.Parent is not null
            ? new[] { selected.Parent, selected }
            : new[] { selected };

        return candidates.FirstOrDefault(candidate =>
            File.Exists(Path.Combine(candidate.FullName, "SquadGame.uproject"))
            && Directory.Exists(Path.Combine(candidate.FullName, "Content")));
    }

    private static DirectoryInfo ResolvePaksDirectory(DirectoryInfo selected)
    {
        if (selected.Name.Equals("Paks", StringComparison.OrdinalIgnoreCase))
            return selected;

        var candidates = new[]
        {
            Path.Combine(selected.FullName, "SquadGame", "Content", "Paks"),
            Path.Combine(selected.FullName, "Content", "Paks")
        };
        return new DirectoryInfo(candidates.FirstOrDefault(Directory.Exists) ?? selected.FullName);
    }
}

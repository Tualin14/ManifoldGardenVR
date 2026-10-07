using System.Text.RegularExpressions;

namespace ManifoldProbe;

internal sealed record TestLevel(string Scene, string Label);

internal static class LevelCatalog
{
    internal const int PageSize = 18;
    // Consume the shipped gameplay catalog, never synthesize scene names from numbers.
    internal static TestLevel[] Build(IEnumerable<string> scenes, IEnumerable<string> removed)
    {
        var excluded = new HashSet<string>(removed, StringComparer.Ordinal);
        return scenes.Where(s => !string.IsNullOrWhiteSpace(s) && !excluded.Contains(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .Select(s => new TestLevel(s, Label(s))).ToArray();
    }

    static string Label(string scene) => Regex.Replace(scene, "^World_", "")
        .Replace("_Optimized", "").Replace('_', ' ');

    internal static int PageCount(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    internal static int ChangePage(int page, int direction, int count)
    {
        int total = PageCount(count);
        return ((page + direction) % total + total) % total;
    }
}

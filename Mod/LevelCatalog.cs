using System.Text.RegularExpressions;

namespace ManifoldProbe;

internal sealed record TestLevel(string Scene, string Label);

internal static class LevelCatalog
{
    internal const int PageSize = 18;
    static readonly Dictionary<string, int> playOrder = CreatePlayOrder();

    // Order the real catalog; loading must always use the exact native scene name.
    internal static TestLevel[] Build(IEnumerable<string> scenes, IEnumerable<string> removed)
    {
        var excluded = new HashSet<string>(removed, StringComparer.Ordinal);
        return scenes.Where(s => !string.IsNullOrWhiteSpace(s) && !excluded.Contains(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Rank)
            .ThenBy(s => s, StringComparer.Ordinal)
            .Select(s => new TestLevel(s, Label(s))).ToArray();
    }

    static int Rank(string scene)
    {
        string key = scene.EndsWith("_Optimized", StringComparison.Ordinal) ? scene[..^10] : scene;
        return playOrder.TryGetValue(key, out int index) ? index : int.MaxValue;
    }

    static Dictionary<string, int> CreatePlayOrder()
    {
        // Reviewed against the shipped portal graph and hub grow targets.
        // Revisited locations appear once, at their first place on the main route.
        // Branches have no unique total order and follow the main route.
        string[] scenes =
        {
            // Opening and blue.
            "World_000",
            "Hallway_W000_W001",
            "World_001",
            "AudioVisual_001",
            "Mandala_World_001",
            "Hallway_W044_W001A",
            "World_044_CubicSpaceDivision",
            "World_044B_CubicSpaceDivision",
            // Red.
            "Hallway_W044_W015",
            "World_015_Stepwell",
            "Hallway_W015_W041",
            "World_045_FirstHub",
            "Hallway_W045_W018",
            "Hallway_W041_W018",
            "World_018_PastaTile",
            "Hallway_W018_W003B",
            "World_003_SpherePipe",
            "AudioVisual_003",
            "Mandala_World_003",
            "Hallway_W044_W003",
            "World_044C_CubicSpaceDivision",
            // Green.
            "Hallway_W044_W045",
            "Hallway_W045_W053",
            "World_053_WaterTechingPuzzle",
            "AudioVisual_053",
            "Mandala_World_053",
            "Hallway_W044_W053",
            "World_044E_CubicSpaceDivision",
            // Yellow.
            "Hallway_W044_W057",
            "World_057_WaterAndPortals",
            "Hallway_W057_W045",
            "Hallway_W045_W073",
            "World_073_WaterThruStairs",
            "Hallway_W073_W062",
            "World_062_LargeGap",
            "Hallway_W062_W026",
            "World_026_Library",
            "Hallway_W026_W002",
            "Hallway_W026_W002_PuzzleRoom",
            "World_002",
            "AudioVisual_002",
            "Mandala_World_002",
            "Hallway_W044_W002",
            "World_044D_CubicSpaceDivision",
            // Purple.
            "Hallway_W044_W026",
            "Hallway_W026_W051",
            "World_051_CeilingSuspendPuzzle",
            "AudioVisual_051",
            "Mandala_World_051",
            "Hallway_W044_W051A",
            "World_044G_CubicSpaceDivision",
            // Orange.
            "Hallway_W033_W063",
            "World_063_TowerX",
            "AudioVisual_063",
            "Mandala_World_063",
            "Hallway_W044_W063",
            "World_044F_CubicSpaceDivision",
            // Final approach.
            "Hallway_W044_W501",
            "World_801",
            "Hallway_W801_W808",
            "World_808_WaterThruPortals",
            "World_802",
            "World_803",
            "World_804",
            "Hallway_W804_W807",
            "World_807_TetrominoWaterAndGears",
            "Hallway_W807_W806",
            "World_806",
            "Hallway_W806_W071",
            "World_805",
            "Hallway_W805_W071",
            "World_071_AkshardhamTemple",
            "AudioVisual_071",
            "Mandala_World_071",
            // Ending.
            "Hallway_W071_W072",
            "Hallway_W071_W809",
            "World_809",
            "Hallway_W809_W810",
            "World_810",
            "Hallway_W810_W072",
            "World_072_FinalLevel",
            "World_502_ColorfulArchitecture",
            "World_902_EndingFallOne",
            "World_903_EndingFallTwo",
            "World_904_EndingFallThree",
            "World_905_EndingCollapseCutscene",
            "World_906_EndingDarkCollapseCutscene",
            "World_907_EndingZeroCollapseCutscene",
            // Optional routes, secrets and alternate ending.
            "Hallway_W000_W052",
            "World_052_JerryMcGuirePuzzle",
            "Hallway_W052_W062",
            "World_613_LongBridgeLongTower",
            "Hallway_W026_W015",
            "World_612_BlindSpherePuzzle",
            "Hallway_W612_W057",
            "Hallway_W073_W018",
            "Hallway_W073_W026",
            "Hallway_W018_W063",
            "Hallway_W062_W063",
            "Hallway_W604_W062",
            "World_611_SecretRoom1",
            "Hallway_W053_W930",
            "World_930_QueenSecret",
            "World_016_Staircase",
            "World_019_FractalPiano",
            "World_614_Memorial",
            "Hallway_W045_W804",
            "Hallway_W062_W900",
            "World_900_Garnier",
            "Hallway_W805_W922",
            "World_922_AlternateEndingFallOne",
            "World_923_AlternateEndingFallTwo",
            "World_924_AlternateEndingFallThree",
        };
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < scenes.Length; i++) order.Add(scenes[i], i);
        return order;
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

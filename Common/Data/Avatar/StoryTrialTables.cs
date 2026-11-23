using Newtonsoft.Json;

namespace March7thHoney.Data.Avatar;

/// <summary>
///     Skill-tree limits and story-trial world-level flags are not on the MemoryPack excel rows
///     (appending them would invalidate Resource.bin). They are read from the raw tables after either
///     boot path. When the tables are absent the callers keep the previous behavior.
/// </summary>
public static class StoryTrialTables
{
    private readonly record struct SkillNode(int PointId, int Level, int PromotionLimit, int LevelLimit, int EnhancedId);

    private static readonly Dictionary<int, List<SkillNode>> SkillNodes = [];
    private static readonly Dictionary<int, bool> UseWorldLevel = [];

    public static bool HasSkillLimits { get; private set; }
    public static bool HasSpecialAvatarFlags { get; private set; }

    public static void Load()
    {
        SkillNodes.Clear();
        UseWorldLevel.Clear();
        HasSkillLimits = false;
        HasSpecialAvatarFlags = false;

        if (LoadSkillTree("AvatarSkillTreeConfig.json") | LoadSkillTree("AvatarSkillTreeConfigLD.json"))
            HasSkillLimits = SkillNodes.Count > 0;
        if (LoadSpecialAvatars("SpecialAvatar.json"))
            HasSpecialAvatarFlags = UseWorldLevel.Count > 0;
    }

    /// <summary>
    ///     Highest skill-tree level each point may have at this promotion and level
    ///     (<c>CustomSkillTreeKey MaxWithInLevel</c>). Empty when the tables were not loaded.
    /// </summary>
    public static List<(int PointId, int Level)> MaxedSkillTree(int avatarId, int promotion, int level)
    {
        if (!HasSkillLimits || !SkillNodes.TryGetValue(avatarId, out var rows)) return [];

        var best = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            if (row.EnhancedId != 0) continue;
            if (row.PromotionLimit > promotion || row.LevelLimit > level) continue;
            if (!best.TryGetValue(row.PointId, out var current) || row.Level > current)
                best[row.PointId] = row.Level;
        }

        return best.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)).ToList();
    }

    /// <summary>
    ///     The world-level 0 row of this special avatar opts into the player's own world level.
    ///     Missing rows are not world-level scaled.
    /// </summary>
    public static bool SpecialUsesWorldLevel(int specialAvatarId)
    {
        return UseWorldLevel.TryGetValue(specialAvatarId * 10, out var uses) && uses;
    }

    private static bool LoadSkillTree(string fileName)
    {
        var file = ResourcePaths.FindExcel(fileName);
        if (file == null) return false;

        var loaded = false;
        foreach (var row in ReadObjects(file))
        {
            var avatarId = row.GetValueOrDefault("AvatarID");
            var pointId = row.GetValueOrDefault("PointID");
            if (avatarId == 0 || pointId == 0) continue;
            if (!SkillNodes.TryGetValue(avatarId, out var list))
            {
                list = [];
                SkillNodes[avatarId] = list;
            }

            list.Add(new SkillNode(pointId, row.GetValueOrDefault("Level"),
                row.GetValueOrDefault("AvatarPromotionLimit"), row.GetValueOrDefault("AvatarLevelLimit"),
                row.GetValueOrDefault("EnhancedID")));
            loaded = true;
        }

        return loaded;
    }

    private static bool LoadSpecialAvatars(string fileName)
    {
        var file = ResourcePaths.FindExcel(fileName);
        if (file == null) return false;

        var loaded = false;
        foreach (var row in ReadObjects(file))
        {
            var specialId = row.GetValueOrDefault("SpecialAvatarID");
            if (specialId == 0) continue;
            var key = specialId * 10 + row.GetValueOrDefault("WorldLevel");
            UseWorldLevel[key] = row.GetValueOrDefault("IsUseWorldLevel") != 0;
            loaded = true;
        }

        return loaded;
    }

    private static IEnumerable<Dictionary<string, int>> ReadObjects(FileInfo file)
    {
        using var reader = new JsonTextReader(file.OpenText());
        var depth = 0;
        string? property = null;
        Dictionary<string, int>? row = null;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    depth++;
                    if (depth == 1) row = [];
                    break;
                case JsonToken.EndObject:
                    if (depth == 1 && row != null) yield return row;
                    depth--;
                    property = null;
                    break;
                case JsonToken.PropertyName when depth == 1:
                    property = reader.Value as string;
                    break;
                case JsonToken.Integer when depth == 1 && property != null && row != null:
                    row[property] = Convert.ToInt32(reader.Value);
                    break;
                case JsonToken.Boolean when depth == 1 && property != null && row != null:
                    row[property] = reader.Value is true ? 1 : 0;
                    break;
            }
        }
    }
}

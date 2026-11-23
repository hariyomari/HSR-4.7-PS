using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data.Config.AdventureAbility;

/// <summary>Resolves shared NPC abilities using the character's declared names, not its model filename.</summary>
public sealed class AdventureAbilityResourceCatalog
{
    private readonly Dictionary<string, (JObject Ability, JObject Container)> _abilities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);
    private readonly string _resourceRoot;

    public AdventureAbilityResourceCatalog(string resourceRoot)
    {
        _resourceRoot = resourceRoot;
        var directory = Path.Combine(resourceRoot, "Config", "ConfigAdventureAbility");
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (path.EndsWith(".layout.json", StringComparison.Ordinal)) continue;
            var container = JObject.Parse(File.ReadAllText(path).Replace("$type", "Type"));
            foreach (var ability in container["AbilityList"]?.OfType<JObject>() ?? [])
            {
                var name = ability.Value<string>("Name");
                if (string.IsNullOrEmpty(name)) continue;
                if (_abilities.TryGetValue(name, out var existing))
                {
                    if (!JToken.DeepEquals(existing.Ability, ability) ||
                        !JToken.DeepEquals(existing.Container["GlobalModifiers"], container["GlobalModifiers"]))
                        _ambiguous.Add(name);
                }
                else _abilities.Add(name, (ability, container));
            }
        }
    }

    public bool TryResolve(string characterPath, [NotNullWhen(true)] out AdventureAbilityConfigListInfo? info)
    {
        info = null;
        if (string.IsNullOrEmpty(characterPath)) return false;
        var path = Path.Combine(_resourceRoot, characterPath);
        if (!File.Exists(path)) return false;
        var character = JObject.Parse(File.ReadAllText(path));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in character["AbilityList"] ?? new JArray())
            if (token.Type == JTokenType.String && token.Value<string>() is { Length: > 0 } name)
                names.Add(name);
        foreach (var skill in character["SkillList"]?.OfType<JObject>() ?? [])
            if (skill.Value<string>("EntryAbility") is { Length: > 0 } name) names.Add(name);
        if (names.Any(name => _ambiguous.Contains(name) || !_abilities.ContainsKey(name))) return false;

        var abilities = new JArray();
        var modifiers = new JObject();
        foreach (var name in names.Order(StringComparer.Ordinal))
        {
            var entry = _abilities[name];
            abilities.Add(entry.Ability.DeepClone());
            if (entry.Container["GlobalModifiers"] is not JObject globals) continue;
            foreach (var property in globals.Properties())
            {
                if (modifiers.TryGetValue(property.Name, out var previous) && !JToken.DeepEquals(previous, property.Value))
                    return false;
                modifiers[property.Name] = property.Value.DeepClone();
            }
        }
        info = AdventureAbilityConfigListInfo.LoadFromJsonObject(new JObject
        {
            ["AbilityList"] = abilities,
            ["GlobalModifiers"] = modifiers
        });
        return true;
    }
}

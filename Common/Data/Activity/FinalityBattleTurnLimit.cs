using March7thHoney.Internationalization;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data.Activity;

/// <summary>
///     The lose-side round limit of a Finality Battle stage: the LimitCount of the SetLevelChallengeTurnLimit task in
///     the FinalityBattle_TurnLimit ability. Settle records derive the remaining rounds from it.
/// </summary>
public static class FinalityBattleTurnLimit
{
    private const string AbilityFile =
        "Config/ConfigAbility/FinalityBattle/Avatar_FinalityBattle_BattleTarget_Ability.json";

    private const string AbilityName = "FinalityBattle_TurnLimit";

    /// <summary>Sets <see cref="GameData.FinalityBattleTurnLimit" />; leaves it unchanged when the ability is absent.</summary>
    public static void Load()
    {
        var path = ResourcePaths.Resource(AbilityFile);
        if (!File.Exists(path)) return;
        try
        {
            var obj = JObject.Parse(File.ReadAllText(path));
            var ability = obj["AbilityList"]?.FirstOrDefault(x => x["Name"]?.ToString() == AbilityName);
            var task = ability?["OnAdd"]?.FirstOrDefault(x =>
                x["$type"]?.ToString().EndsWith("SetLevelChallengeTurnLimit") == true);
            var value = task?["LimitCount"]?["FixedValue"]?["Value"]?.Value<double>();
            if (value != null) GameData.FinalityBattleTurnLimit = (int)value.Value;
        }
        catch (Exception ex)
        {
            ResourceManager.Logger.Error(I18NManager.Translate("Server.ServerInfo.FailedToReadItem", path, ""), ex);
        }
    }
}

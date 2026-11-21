using System.Net.Http.Json;
using System.Text.Json;
using March7thHoney.Configuration;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Util;

// The "rewards" settings group edits ServerOption.ProgressionRewards through the admin settings API.
internal static class ProgressionRewardSettingsChecks
{
    public static async Task Run(HttpClient client, Action<string, bool> check)
    {
        var option = ConfigManager.Config.ServerOption;
        var saved = option.ProgressionRewards;
        var addedItems = new HashSet<int>();
        try
        {
            foreach (var id in new[] { 1, 2, 101 })
                if (GameData.ItemConfigData.TryAdd(id, new ItemConfigExcel { ID = id }))
                    addedItems.Add(id);
            option.ProgressionRewards = new ProgressionRewardsOption();
            var rewards = option.ProgressionRewards;
            var defaults = rewards.MainMission;

            using (var rejected = await Put(client, new Dictionary<string, string>
                   {
                       ["ProgressionRewards.MainMission"] = "1:180\n999999991:5",
                       ["ProgressionRewards.SubMission"] = "1",
                       ["ProgressionRewards.BattleWin"] = "2:-5"
                   }))
            {
                var data = rejected.RootElement.GetProperty("data");
                check("reward settings reject unknown items, missing counts and non-positive counts",
                    data.GetProperty("applied").GetInt32() == 0 && data.GetProperty("errors").GetArrayLength() == 3 &&
                    ReferenceEquals(rewards.MainMission, defaults));
                var fields = data.GetProperty("fields").EnumerateArray()
                    .Where(f => f.GetProperty("group").GetString() == "rewards").ToList();
                check("settings list the rewards group with the default payouts and the feature off",
                    fields.Count == 16 &&
                    Value(fields, "ProgressionRewards.Enabled") == "false" &&
                    Value(fields, "ProgressionRewards.MainMission") == "1:180\n2:10000\n101:1" &&
                    Value(fields, "ProgressionRewards.BattleKinds.Story") == "true" &&
                    Value(fields, "ProgressionRewards.BattleKinds.Domain") == "false");
            }

            using (var accepted = await Put(client, new Dictionary<string, string>
                   {
                       ["ProgressionRewards.Enabled"] = "true",
                       ["ProgressionRewards.BattleWin"] = "1:5\r\n101 1\n",
                       ["ProgressionRewards.BattleKinds.Domain"] = "true"
                   }))
                check("reward settings apply the switch, an edited list and a battle kind",
                    accepted.RootElement.GetProperty("data").GetProperty("applied").GetInt32() == 3 &&
                    rewards.Enabled && rewards.BattleKinds.Domain &&
                    rewards.BattleWin.Select(i => (i.ItemId, i.Count)).SequenceEqual([(1, 5), (101, 1)]));
        }
        finally
        {
            option.ProgressionRewards = saved;
            foreach (var id in addedItems) GameData.ItemConfigData.Remove(id);
        }
    }

    private static string? Value(List<JsonElement> fields, string key)
    {
        return fields.FirstOrDefault(f => f.GetProperty("key").GetString() == key) is { ValueKind: JsonValueKind.Object } field
            ? field.GetProperty("value").GetString()
            : null;
    }

    private static async Task<JsonDocument> Put(HttpClient client, Dictionary<string, string> values)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/admin/api/settings")
            { Content = JsonContent.Create(new { values }) };
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"PUT /admin/api/settings -> {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }
}

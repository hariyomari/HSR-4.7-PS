using March7thHoney.Data;
using March7thHoney.Data.Activity;
using March7thHoney.Database;
using March7thHoney.Database.Activity;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Activity.Activities;

/// <summary>
///     MaterialSubmission activity ("Lab Assistants In Position"): each slot takes a set of materials while its mission
///     runs, then pays a one-time reward. A slot counts as submitted once saved or once its mission is finished.
/// </summary>
public class MaterialSubmitActivity(PlayerInstance player)
{
    private MaterialSubmitData Data
    {
        get
        {
            var activity = player.ActivityManager!.Data;
            activity.MaterialSubmitData ??= new MaterialSubmitData();
            return activity.MaterialSubmitData;
        }
    }

    public List<KPMAKCHCDMH> BuildList()
    {
        return MaterialSubmitterTable.Rows.Values.OrderBy(row => row.Id).Select(BuildEntry).ToList();
    }

    public KPMAKCHCDMH BuildEntry(MaterialSubmitterRow row)
    {
        return new KPMAKCHCDMH
        {
            LNOCOOCMNCI = (uint)row.Id,
            BAONIPOKCPL = IsSubmitted(row),
            IsTakenReward = Data.Taken.Contains(row.Id),
            PanelId = (uint)(row.ActivityModuleId / 100)
        };
    }

    private bool IsSubmitted(MaterialSubmitterRow row)
    {
        return Data.Submitted.Contains(row.Id) ||
               (row.MissionId != 0 && player.MissionManager?.GetMainMissionStatus(row.MissionId) ==
                   MissionPhaseEnum.Finish);
    }

    public async ValueTask<(Retcode Retcode, KPMAKCHCDMH? Entry)> Submit(int id)
    {
        if (!MaterialSubmitterTable.Rows.TryGetValue(id, out var row) || row.Materials.Count == 0)
            return (Retcode.RetReqParaInvalid, null);
        if (IsSubmitted(row)) return (Retcode.RetFail, null);
        if (row.MissionId != 0 &&
            player.MissionManager?.GetMainMissionStatus(row.MissionId) != MissionPhaseEnum.Accept)
            return (Retcode.RetFail, null);

        // Check every stack before taking any, so a short material cannot leave a partial submission.
        foreach (var (itemId, count) in row.Materials)
            if ((player.InventoryManager!.GetItem(itemId)?.Count ?? 0) < count)
                return (Retcode.RetItemNotEnough, null);

        Data.Submitted.Add(id);
        DatabaseHelper.MarkDirty(player.Uid);
        foreach (var (itemId, count) in row.Materials)
            await player.InventoryManager!.RemoveItem(itemId, count);

        return (Retcode.RetSucc, BuildEntry(row));
    }

    public async ValueTask<(Retcode Retcode, List<ItemData> Items)> TakeReward(int id)
    {
        if (!MaterialSubmitterTable.Rows.TryGetValue(id, out var row) || !IsSubmitted(row))
            return (Retcode.RetFail, []);
        // Resolve the payout before marking, so a missing reward row cannot burn the one-time claim.
        if (!GameData.RewardDataData.TryGetValue(row.RewardId, out var reward)) return (Retcode.RetFail, []);
        if (Data.Taken.Contains(id)) return (Retcode.RetFail, []);

        Data.Taken.Add(id);
        DatabaseHelper.MarkDirty(player.Uid);

        List<ItemData> granted = [];
        if (reward.Hcoin > 0)
        {
            player.Data.Hcoin += reward.Hcoin;
            granted.Add(new ItemData { ItemId = (int)VirtualItemEnum.Hcoin, Count = reward.Hcoin });
        }

        foreach (var (itemId, count) in reward.GetItems())
        {
            var item = await player.InventoryManager!.AddItem(itemId, count, false);
            if (item != null) granted.Add(item);
        }

        return (Retcode.RetSucc, granted);
    }
}

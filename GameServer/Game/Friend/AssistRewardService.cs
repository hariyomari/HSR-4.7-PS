using March7thHoney.Database;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Player;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.DailyActive;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.GameServer.Server.Packet.Send.Friend;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Friend;

/// <summary>
///     Support-character rewards (yuuki d876812). A won Calyx, Stagnant Shadow or raid stage fought with someone's
///     support earns that owner one unit (10 a day, 60 pending at most); claiming pays 2000 credits a unit.
/// </summary>
public static class AssistRewardService
{
    private const uint CreditsPerUnit = 2000;
    private const uint DailyUnitCap = 10;
    private const uint PendingUnitCap = 60;

    // Owners are written from other players' sessions, so every read-modify-write takes this lock.
    private static readonly object Gate = new();

    public static AssistRewardData Get(int uid)
    {
        return DatabaseHelper.Instance!.GetInstanceOrCreateNew<AssistRewardData>(uid);
    }

    /// <summary>Credits each distinct support owner in a won, eligible battle once.</summary>
    public static async ValueTask CreditBattle(PlayerInstance borrower, PVEBattleResultCsReq req,
        IEnumerable<int> lineupAssistOwners)
    {
        var owners = req.Stt.BattleAvatarList
            .Where(avatar => avatar.AvatarType is AvatarType.AvatarAssistType or AvatarType.AvatarReturnAssistType)
            .Select(avatar => (int)avatar.AssistUid)
            .Where(uid => uid != 0)
            .ToHashSet();
        if (owners.Count == 0 && req.Stt.BattleAvatarList.Any(avatar =>
                avatar.AvatarType is AvatarType.AvatarAssistType or AvatarType.AvatarReturnAssistType))
            owners.UnionWith(lineupAssistOwners);
        owners.Remove(borrower.Uid);

        foreach (var ownerUid in owners)
        {
            var ownerPlayer = PlayerData.GetPlayerByUid(ownerUid);
            if (ownerPlayer == null) continue;
            var today = DailyResetClock.Today(ownerPlayer);

            uint pending;
            lock (Gate)
            {
                var data = Get(ownerUid);
                data.TotalAssists++;
                if (data.EarnDate != today)
                {
                    data.EarnDate = today;
                    data.EarnedToday = 0;
                }

                if (data.EarnedToday < DailyUnitCap && data.PendingUnits < PendingUnitCap)
                {
                    data.EarnedToday++;
                    var entry = data.Pending.Find(row => row.Uid == borrower.Uid);
                    if (entry == null) data.Pending.Add(new AssistRewardEntry { Uid = borrower.Uid, Count = 1 });
                    else entry.Count++;
                }

                pending = data.PendingUnits;
                DatabaseHelper.MarkDirty(ownerUid);
            }

            var connection = Listener.GetActiveConnection(ownerUid);
            if (connection?.Player != null)
                await connection.Player.SendPacket(new PacketNewAssistHistoryNotify(pending));
        }
    }

    /// <summary>Claims the whole bucket. Returns the rows it paid for and the granted credits.</summary>
    public static async ValueTask<(List<KAODLPJMEFE> Rows, List<ItemData> Items)> Claim(PlayerInstance owner)
    {
        List<AssistRewardEntry> claimed;
        lock (Gate)
        {
            var data = Get(owner.Uid);
            claimed = [.. data.Pending.Where(entry => entry.Count > 0)];
            data.Pending.Clear();
            DatabaseHelper.MarkDirty(owner.Uid);
        }

        var units = claimed.Sum(entry => (long)entry.Count);
        List<ItemData> items = [];
        if (units > 0)
        {
            var credits = (int)Math.Min(units * CreditsPerUnit, int.MaxValue);
            await owner.InventoryManager!.AddItem((int)VirtualItemEnum.Scoin, credits, false);
            items.Add(new ItemData { ItemId = (int)VirtualItemEnum.Scoin, Count = credits });
        }

        var remarks = owner.FriendManager?.FriendData.FriendDetailList;
        List<KAODLPJMEFE> rows = [];
        foreach (var entry in claimed)
        {
            var borrower = PlayerData.GetPlayerByUid(entry.Uid);
            if (borrower == null) continue;
            rows.Add(new KAODLPJMEFE
            {
                Uid = (uint)borrower.Uid,
                Nickname = borrower.Name,
                Level = (uint)borrower.Level,
                HeadIcon = (uint)borrower.HeadIcon,
                Platform = PlatformType.Pc,
                MIDBIPHKLJO = (uint)borrower.PersonalCard,
                RemarkName = remarks?.GetValueOrDefault(entry.Uid)?.RemarkName ?? "",
                DCOFPHDKNIJ = entry.Count
            });
        }

        return (rows, items);
    }
}

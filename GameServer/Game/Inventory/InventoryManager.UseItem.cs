using March7thHoney.Data;
using March7thHoney.Database.Inventory;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Inventory;

public partial class InventoryManager
{
    public async ValueTask<(Retcode, List<ItemData>? returnItems)> UseItem(int itemId, int count = 1,
        int baseAvatarId = 0)
    {
        GameData.ItemConfigData.TryGetValue(itemId, out var itemConfig);
        if (itemConfig == null) return (Retcode.RetItemNotExist, null);
        var dataId = itemConfig.UseDataID != 0 ? itemConfig.UseDataID : itemConfig.ID;

        GameData.ItemUseBuffDataData.TryGetValue(dataId, out var useConfig);
        if (useConfig != null && baseAvatarId > 0 && Player.AvatarManager?.GetFormalAvatar(baseAvatarId) == null)
            return (Retcode.RetAvatarNotExist, null);

        // The used items are paid before any effect or reward; this also bounds count by what the player holds.
        if (!await TryRemoveItems([(itemId, count)])) return (Retcode.RetItemNotEnough, null);

        List<ItemData> resItemDatas = [];
        if (useConfig != null)
        {
            for (var i = 0; i < count; i++) // do count times
            {
                if (useConfig.PreviewSkillPoint != 0)
                    await Player.LineupManager!.GainMp((int)useConfig.PreviewSkillPoint);

                if (baseAvatarId > 0)
                {
                    // single use
                    var avatar = Player.AvatarManager!.GetFormalAvatar(baseAvatarId);
                    if (avatar == null) return (Retcode.RetAvatarNotExist, null);

                    var extraLineup = Player.LineupManager!.GetCurLineup()?.IsExtraLineup() == true;

                    if (useConfig.PreviewHPRecoveryPercent != 0)
                    {
                        avatar.SetCurHp(
                            Math.Min(Math.Max(avatar.GetCurHp(extraLineup) + (int)(useConfig.PreviewHPRecoveryPercent * GameConstants.FULL_HP), 0),
                                GameConstants.FULL_HP), extraLineup);

                        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager.GetCurLineup()!));
                    }

                    if (useConfig.PreviewHPRecoveryValue != 0)
                    {
                        avatar.SetCurHp(
                            Math.Min(Math.Max(avatar.GetCurHp(extraLineup) + (int)useConfig.PreviewHPRecoveryValue, 0), GameConstants.FULL_HP),
                            extraLineup);

                        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager.GetCurLineup()!));
                    }

                    if (useConfig.PreviewPowerPercent != 0)
                    {
                        avatar.SetCurSp(
                            Math.Min(Math.Max(avatar.GetCurSp(extraLineup) + (int)(useConfig.PreviewPowerPercent * 10000), 0),
                                10000),
                            extraLineup);

                        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager.GetCurLineup()!));
                    }
                }
                else
                {
                    // team use
                    if (useConfig.PreviewHPRecoveryPercent != 0)
                    {
                        Player.LineupManager!.GetCurLineup()!.Heal((int)(useConfig.PreviewHPRecoveryPercent * GameConstants.FULL_HP),
                            true);

                        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager.GetCurLineup()!));
                    }

                    if (useConfig.PreviewHPRecoveryValue != 0)
                    {
                        Player.LineupManager!.GetCurLineup()!.Heal((int)useConfig.PreviewHPRecoveryValue, true);

                        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager.GetCurLineup()!));
                    }

                    if (useConfig.PreviewPowerPercent != 0)
                    {
                        Player.LineupManager!.GetCurLineup()!.AddPercentSp((int)(useConfig.PreviewPowerPercent *
                            10000));

                        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager.GetCurLineup()!));
                    }
                }
            }

            //maze buff
            if (useConfig.MazeBuffID > 0)
                foreach (var info in Player.SceneInstance?.AvatarInfo.Values.ToList() ?? [])
                    if (baseAvatarId == 0 || info.AvatarInfo.BaseAvatarId == baseAvatarId)
                        await info.AddBuff(new SceneBuff(useConfig.MazeBuffID, 1, info.AvatarInfo.AvatarId));

            if (useConfig.MazeBuffID2 > 0)
                foreach (var info in Player.SceneInstance?.AvatarInfo.Values.ToList() ?? [])
                    if (baseAvatarId == 0 || info.AvatarInfo.BaseAvatarId == baseAvatarId)
                        await info.AddBuff(new SceneBuff(useConfig.MazeBuffID2, 1, info.AvatarInfo.AvatarId));
        }

        if (GameData.ItemUseDataData.TryGetValue(dataId, out var useData))
            for (var i = 0; i < count; i++)
                foreach (var rewardId in useData.UseParam)
                    resItemDatas.AddRange(await HandleReward(rewardId, true));

        return (Retcode.RetSucc, resItemDatas);
    }
}

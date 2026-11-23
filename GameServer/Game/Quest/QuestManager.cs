using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Quests;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Quest;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Quest;

public class QuestManager(PlayerInstance player) : BasePlayerManager<QuestData>(player)
{
    /// <summary>QuestData.QuestType of the daily training (每日实训) quests.</summary>
    public const int DailyQuestType = 5;

    public FuncUnlockChecker FuncUnlockChecker { get; } = new(player);
    public List<QuestInfo> WaitToSync { get; } = [];

    private QuestInfo? GetActivityQuest(int questId) => Player.B51RacingManager?.GetQuestInfo(questId) ?? Player.MechCraftManager?.GetQuestInfo(questId);
    private bool IsActivityQuest(int questId) => GetActivityQuest(questId) != null;

    #region Handler

    public void OnBattleStart(BattleInstance instance)
    {
        foreach (var questInfo in GetRunningQuest())
        {
            var questExcel = GameData.QuestDataData.GetValueOrDefault(questInfo.QuestId);
            if (questExcel == null) continue;
            var finishWayExcel = GameData.FinishWayData.GetValueOrDefault(questExcel.FinishWayID);
            if (finishWayExcel == null) continue;
            if (finishWayExcel.FinishType == MissionFinishTypeEnum.BattleChallenge)
                foreach (var target in finishWayExcel.ParamIntList)
                    instance.AddBattleTarget(2, target, GetQuestProgress(questExcel.QuestID), finishWayExcel.Progress);
        }
    }

    #endregion

    #region Actions

    public async ValueTask AcceptQuestByCondition()
    {
        if (!ConfigManager.Config.ServerOption.EnableQuest) return;

        var syncList = new List<QuestInfo>();
        foreach (var quest in GameData.QuestDataData.Values)
        {
            if (IsActivityQuest(quest.QuestID)) continue;
            if (Data.Quests.ContainsKey(quest.QuestID)) continue; // Already accepted
            // Daily training quests are handed out only by the daily roll (DailyActiveManager), 8 per day.
            if (quest.QuestType == DailyQuestType) continue;
            QuestInfo? acceptQuest = null;
            switch (quest.UnlockType)
            {
                case QuestUnlockTypeEnum.AutoUnlock:
                    acceptQuest = await AcceptQuest(quest.QuestID, false);
                    break;
                case QuestUnlockTypeEnum.FinishMission:
                    var accept = true;

                    foreach (var missionId in quest.UnlockParamList)
                        if (Player.MissionManager!.GetMainMissionStatus(missionId) != MissionPhaseEnum.Finish)
                        {
                            accept = false;
                            break;
                        }

                    if (accept) acceptQuest = await AcceptQuest(quest.QuestID, false);
                    break;
                case QuestUnlockTypeEnum.FinishQuest:
                    var accept2 = true;

                    foreach (var questId in quest.UnlockParamList)
                        if (GetQuestStatus(questId) != QuestStatus.QuestFinish &&
                            GetQuestStatus(questId) != QuestStatus.QuestClose)
                        {
                            accept2 = false;
                            break;
                        }

                    if (accept2) acceptQuest = await AcceptQuest(quest.QuestID, false);
                    break;
                case QuestUnlockTypeEnum.ManualUnlock: // what triggers this is not known
                    break;
                case QuestUnlockTypeEnum.BattlePassWeekly:
                case QuestUnlockTypeEnum.CakeCatchNotFinish:
                case QuestUnlockTypeEnum.Unknown:
                default:
                    break;
            }

            if (acceptQuest != null) syncList.Add(acceptQuest);
        }

        if (syncList.Count > 0)
            await Player.SendPacket(new PacketPlayerSyncScNotify(syncList));
    }

    /// <summary>
    ///     Puts a quest record into <paramref name="status" /> with its progress cleared, creating the record if it
    ///     does not exist. Used by the daily roll to re-arm and expire repeatable quests, which AcceptQuest refuses
    ///     because a record already exists. Returns the changed record for syncing.
    /// </summary>
    public QuestInfo ResetQuestState(int questId, QuestStatus status)
    {
        if (!Data.Quests.TryGetValue(questId, out var questInfo))
        {
            questInfo = new QuestInfo { QuestId = questId };
            Data.Quests.Add(questId, questInfo);
        }

        questInfo.QuestStatus = status;
        if (status == QuestStatus.QuestDoing)
        {
            questInfo.Progress = 0;
            questInfo.FinishTime = 0;
        }

        WaitToSync.Remove(questInfo);
        MarkQuestDataDirty();
        return questInfo;
    }

    public async ValueTask<QuestInfo?> AcceptQuest(int questId, bool sync = true)
    {
        if (GetActivityQuest(questId) is { } activityQuest)
        {
            if (sync) await Player.SendPacket(new PacketPlayerSyncScNotify(activityQuest));
            return activityQuest;
        }
        if (!ConfigManager.Config.ServerOption.EnableQuest) return null;

        GameData.QuestDataData.TryGetValue(questId, out var questExcel);
        if (questExcel == null) return null;

        if (Data.Quests.ContainsKey(questId)) return null;

        var questInfo = new QuestInfo
        {
            QuestId = questId,
            QuestStatus = QuestStatus.QuestDoing,
            Progress = 0
        };

        Data.Quests.Add(questId, questInfo);
        MarkQuestDataDirty();

        if (sync) await Player.SendPacket(new PacketPlayerSyncScNotify(questInfo));

        return questInfo;
    }

    public async ValueTask FinishQuest(int questId, bool push = true)
    {
        if (IsActivityQuest(questId)) return;
        if (!ConfigManager.Config.ServerOption.EnableQuest) return;

        GameData.QuestDataData.TryGetValue(questId, out var questExcel);
        if (questExcel == null) return;
        GameData.FinishWayData.TryGetValue(questExcel.FinishWayID, out var finishWayExcel);
        if (finishWayExcel == null) return;

        if (!Data.Quests.TryGetValue(questId, out var questInfo)) return;
        if (questInfo.QuestStatus != QuestStatus.QuestDoing) return;

        questInfo.QuestStatus = QuestStatus.QuestFinish;
        questInfo.Progress = finishWayExcel.Progress;
        questInfo.FinishTime = DateTime.Now.ToUnixSec();
        MarkQuestDataDirty();
        if (push)
            await Player.SendPacket(new PacketPlayerSyncScNotify(questInfo));
        else
            WaitToSync.SafeAdd(questInfo);

        // accept next quest
        await AcceptQuestByCondition();
    }

    public async ValueTask<Retcode> FinishQuestByClient(int questId)
    {
        if (IsActivityQuest(questId)) return Retcode.RetSucc;
        if (!ConfigManager.Config.ServerOption.EnableQuest) return Retcode.RetSucc;

        GameData.QuestDataData.TryGetValue(questId, out var questExcel);
        if (questExcel == null) return Retcode.RetFail;
        GameData.FinishWayData.TryGetValue(questExcel.FinishWayID, out var finishWayExcel);
        if (finishWayExcel == null) return Retcode.RetQuestStatusError;
        if (finishWayExcel.FinishType != MissionFinishTypeEnum.AutoFinish &&
            finishWayExcel.FinishType != MissionFinishTypeEnum.TimeLineSetStateCnt &&
            finishWayExcel.FinishType != MissionFinishTypeEnum.FinishQuestByClient) return Retcode.RetQuestStatusError;

        if (!Data.Quests.TryGetValue(questId, out var questInfo)) return Retcode.RetQuestNotAccept;
        if (questInfo.QuestStatus != QuestStatus.QuestDoing) return Retcode.RetQuestStatusError;

        questInfo.QuestStatus = QuestStatus.QuestFinish;
        questInfo.Progress = finishWayExcel.Progress;
        questInfo.FinishTime = DateTime.Now.ToUnixSec();
        MarkQuestDataDirty();
        await Player.SendPacket(new PacketPlayerSyncScNotify(questInfo));

        // accept next quest
        await AcceptQuestByCondition();

        return Retcode.RetSucc;
    }

    public async ValueTask<(Retcode, List<ItemData>?)> TakeQuestReward(int questId)
    {
        if (Player.B51RacingManager?.ManagesQuest(questId) == true)
            return await Player.B51RacingManager.TakeQuestReward(questId);
        if (Player.MechCraftManager?.ManagesQuest(questId) == true)
            return await Player.MechCraftManager.TakeQuestReward(questId);
        GameData.QuestDataData.TryGetValue(questId, out var questExcel);
        if (questExcel == null) return (Retcode.RetFail, null);

        if (!Data.Quests.TryGetValue(questId, out var questInfo))
        {
            if (ConfigManager.Config.ServerOption.EnableQuest) return (Retcode.RetQuestNotAccept, null);
            questInfo = AutoFinishDisabledQuest(questExcel);
        }

        if (questInfo.QuestStatus == QuestStatus.QuestClose) return (Retcode.RetQuestRewardAlreadyTaken, null);
        if (questInfo.QuestStatus != QuestStatus.QuestFinish) return (Retcode.RetQuestNotFinish, null);

        questInfo.QuestStatus = QuestStatus.QuestClose; // Close the quest after taking the reward
        MarkQuestDataDirty();

        // handle reward
        var items = await Player.InventoryManager!.HandleReward(questExcel.RewardID);

        await Player.SendPacket(new PacketPlayerSyncScNotify(questInfo));

        return (Retcode.RetSucc, items);
    }

    public async ValueTask<(Retcode, List<ItemData>)> TakeQuestOptionalReward(int questId, int optionalRewardId)
    {
        if (Player.B51RacingManager?.ManagesQuest(questId) == true)
        {
            if (optionalRewardId != 0) return (Retcode.RetReqParaInvalid, []);
            var (retcode, granted) = await Player.B51RacingManager.TakeQuestReward(questId);
            return (retcode, granted ?? []);
        }
        if (Player.MechCraftManager?.ManagesQuest(questId) == true)
        {
            if (optionalRewardId != 0) return (Retcode.RetFail, []);
            var (retcode, granted) = await Player.MechCraftManager.TakeQuestReward(questId);
            return (retcode, granted ?? []);
        }
        var rewards = new List<ItemData>();

        GameData.QuestDataData.TryGetValue(questId, out var questExcel);
        if (questExcel == null) return (Retcode.RetFail, rewards);

        // QuestData defines no per-quest optional reward choices, so the only grant a quest allows is its
        // configured RewardID; a client-chosen id would otherwise let any reward package or item be claimed.
        if (optionalRewardId != 0) return (Retcode.RetReqParaInvalid, rewards);

        if (!Data.Quests.TryGetValue(questId, out var questInfo))
        {
            if (ConfigManager.Config.ServerOption.EnableQuest) return (Retcode.RetQuestNotAccept, rewards);
            questInfo = AutoFinishDisabledQuest(questExcel);
        }

        if (questInfo.QuestStatus == QuestStatus.QuestClose) return (Retcode.RetQuestRewardAlreadyTaken, rewards);
        if (questInfo.QuestStatus != QuestStatus.QuestFinish) return (Retcode.RetQuestNotFinish, rewards);

        questInfo.QuestStatus = QuestStatus.QuestClose;
        MarkQuestDataDirty();

        if (questExcel.RewardID != 0)
        {
            await AppendGrantedRewardForRsp(questExcel.RewardID, rewards);
        }

        await Player.SendPacket(new PacketPlayerSyncScNotify(questInfo));

        return (Retcode.RetSucc, rewards);
    }

    private async ValueTask AppendGrantedRewardForRsp(int rewardId, List<ItemData> rewards)
    {
        var granted = await Player.InventoryManager!.HandleReward(rewardId);
        if (granted.Count > 0)
        {
            rewards.AddRange(granted);
            return;
        }

        // Fallback: still fill rsp from resource config when runtime grant list is empty.
        if (!GameData.RewardDataData.TryGetValue(rewardId, out var rewardData) || rewardData == null) return;

        foreach (var (itemId, count) in rewardData.GetItems())
            if (itemId > 0 && count > 0)
                rewards.Add(new ItemData { ItemId = itemId, Count = count });

        if (rewardData.Hcoin > 0)
            rewards.Add(new ItemData { ItemId = (int)VirtualItemEnum.Hcoin, Count = rewardData.Hcoin });
    }

    public async ValueTask AddQuestProgress(int questId, int progress, bool push = false)
    {
        if (IsActivityQuest(questId)) return;
        GameData.QuestDataData.TryGetValue(questId, out var questExcel);
        if (questExcel == null) return;
        GameData.FinishWayData.TryGetValue(questExcel.FinishWayID, out var finishWayExcel);
        if (finishWayExcel == null) return;

        if (!Data.Quests.TryGetValue(questId, out var questInfo)) return;
        if (questInfo.QuestStatus != QuestStatus.QuestDoing) return;

        questInfo.Progress += progress;
        MarkQuestDataDirty();
        if (questInfo.Progress >= finishWayExcel.Progress) await FinishQuest(questId, push);
        else if (push)
            await Player.SendPacket(new PacketPlayerSyncScNotify(questInfo));
        else
            WaitToSync.SafeAdd(questInfo);
    }

    public async ValueTask UpdateQuestProgress(int questId, int progress, bool push = false)
    {
        if (IsActivityQuest(questId)) return;
        GameData.QuestDataData.TryGetValue(questId, out var questExcel);
        if (questExcel == null) return;
        GameData.FinishWayData.TryGetValue(questExcel.FinishWayID, out var finishWayExcel);
        if (finishWayExcel == null) return;

        if (!Data.Quests.TryGetValue(questId, out var questInfo)) return;
        if (questInfo.QuestStatus != QuestStatus.QuestDoing) return;

        if (progress < questInfo.Progress) return; // prevent rollback
        if (progress == questInfo.Progress) return;
        questInfo.Progress = progress;
        MarkQuestDataDirty();
        if (questInfo.Progress >= finishWayExcel.Progress) await FinishQuest(questId, push);
        else if (push)
            await Player.SendPacket(new PacketPlayerSyncScNotify(questInfo));
        else
            WaitToSync.SafeAdd(questInfo);
    }

    public async ValueTask SyncQuest()
    {
        if (WaitToSync.Count == 0) return;
        await Player.SendPacket(new PacketPlayerSyncScNotify(WaitToSync));
        WaitToSync.Clear();
    }

    #endregion

    #region Information

    public QuestStatus GetQuestStatus(int questId)
    {
        if (GetActivityQuest(questId) is { } activityQuest) return activityQuest.QuestStatus;
        if (!ConfigManager.Config.ServerOption.EnableQuest)
        {
            if (!Data.Quests.TryGetValue(questId, out var disabledQuestInfo)) return QuestStatus.QuestFinish;
            return disabledQuestInfo.QuestStatus == QuestStatus.QuestClose
                ? QuestStatus.QuestClose
                : QuestStatus.QuestFinish;
        }

        if (!Data.Quests.TryGetValue(questId, out var questInfo)) return QuestStatus.QuestNone;
        return questInfo.QuestStatus;
    }

    public List<QuestInfo> GetRunningQuest()
    {
        if (!ConfigManager.Config.ServerOption.EnableQuest) return [];

        return Data.Quests.Values.Where(x => x.QuestStatus == QuestStatus.QuestDoing &&
            !IsActivityQuest(x.QuestId)).ToList();
    }

    public int GetQuestProgress(int questId)
    {
        if (GetActivityQuest(questId) is { } activityQuest) return activityQuest.Progress;
        if (!Data.Quests.TryGetValue(questId, out var questInfo)) return 0;
        return questInfo.Progress;
    }

    public QuestInfo? GetQuestInfo(int questId) => GetActivityQuest(questId) ?? Data.Quests.GetValueOrDefault(questId);

    #endregion

    private QuestInfo AutoFinishDisabledQuest(QuestDataExcel questExcel)
    {
        var questInfo = new QuestInfo
        {
            QuestId = questExcel.QuestID,
            QuestStatus = QuestStatus.QuestFinish,
            Progress = GetFinishProgress(questExcel.FinishWayID),
            FinishTime = DateTime.Now.ToUnixSec()
        };

        Data.Quests[questExcel.QuestID] = questInfo;
        MarkQuestDataDirty();

        return questInfo;
    }

    private static int GetFinishProgress(int finishWayId)
    {
        return GameData.FinishWayData.TryGetValue(finishWayId, out var finishWayExcel) ? finishWayExcel.Progress : 0;
    }

    private void MarkQuestDataDirty()
    {
        MarkDirty();
    }
}

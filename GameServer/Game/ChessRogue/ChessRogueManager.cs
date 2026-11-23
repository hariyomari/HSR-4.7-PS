using March7thHoney.Enums.Mission;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.ChessRogue;
using March7thHoney.Enums.Rogue;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.RogueCommon;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.ChessRogue;

public partial class ChessRogueManager : BasePlayerManager
{
    // Legacy run defaults: 100 rogue money, 15 action points, revive 80, lineup mp 5.
    private const int StartMoney = 100;
    private const int StartActionPoint = 15;
    private const int ReviveCost = 80;
    private const int StartMp = 5;
    private const int ActionPointOverdrawFloor = -50;

    public ChessRogueManager(PlayerInstance player) : base(player)
    {
        Data = DatabaseHelper.Instance!.GetInstanceOrCreateNew<ChessRogueData>(player.Uid);
        NousData = DatabaseHelper.Instance!.GetInstanceOrCreateNew<ChessRogueNousData>(player.Uid);
        ResurrectRun();
    }

    public ChessRogueData Data { get; }
    public ChessRogueNousData NousData { get; }
    public ChessRogueRunData? RunInstance { get; private set; }

    #region Persistence

    public void Save()
    {
        Data.RunInstance = RogueRunBlob.Serialize(RunInstance);
        DatabaseHelper.MarkDirty(Player.Uid);
    }

    private void ResurrectRun()
    {
        if (!RogueRunBlob.TryLoad<ChessRogueRunData>(Data.RunInstance, out var run)) return;

        if (run == null || run.LevelStatus != ChessRogueStates.LevelProcessing || run.CurCell() == null)
        {
            Data.RunInstance = null;
            DatabaseHelper.MarkDirty(Player.Uid);
            return;
        }

        RunInstance = run;
    }

    #endregion

    #region Dice presets

    public Dictionary<int, int> GetDiceSurfaces(int branchId)
    {
        return NousData.RogueDiceData.TryGetValue(branchId, out var saved) && saved.Surfaces.Count > 0
            ? saved.Surfaces
            : ChessRogueResources.DefaultSurfaces(branchId);
    }

    public HGCIAPKELAF? EditDice(HGCIAPKELAF? dice)
    {
        if (dice == null || !GameData.RogueNousDiceBranchData.ContainsKey((int)dice.PDFAHEKNHNJ)) return null;

        var branchId = (int)dice.PDFAHEKNHNJ;
        var surfaces = new Dictionary<int, int>(GetDiceSurfaces(branchId));
        foreach (var surface in dice.LFLFEKEAOCL)
        {
            if (surface.SlotId is < 1 or > ChessRogueResources.DiceSlotCount) continue;
            if (!GameData.RogueNousDiceSurfaceData.ContainsKey((int)surface.AMPFBGKHJBF)) continue;
            surfaces[(int)surface.SlotId] = (int)surface.AMPFBGKHJBF;
        }

        NousData.RogueDiceData[branchId] = new ChessRogueNousDiceData { BranchId = branchId, Surfaces = surfaces };
        DatabaseHelper.MarkDirty(Player.Uid);
        return ChessRogueProtoBuilder.BuildDiceBranch(branchId, surfaces);
    }

    #endregion

    #region Run lifecycle

    public async ValueTask<Retcode> StartRun(int areaId, int aeonId, int branchId, List<int> avatarIds,
        List<int> difficultyIds)
    {
        if (RunInstance != null) await ClearRun();

        branchId = ChessRogueResources.ResolveBranchId(branchId);

        if (!GameData.RogueDLCAreaData.TryGetValue(areaId, out var area) || area.LayerIDList.Count == 0)
            return Retcode.RetRogueAreaInvalid;
        if (!GameData.RogueNousAeonData.ContainsKey(aeonId) || !GameData.RogueNousDiceBranchData.ContainsKey(branchId))
            return Retcode.RetRogueAreaInvalid;

        var run = new ChessRogueRunData
        {
            AreaId = areaId,
            SubMode = (int)area.RogueVersionId,
            AeonId = aeonId,
            BranchId = branchId,
            DifficultyIds = difficultyIds.Where(GameData.RogueNousDifficultyLevelData.ContainsKey).ToList(),
            BaseAvatarIds = avatarIds,
            LayerIds = [.. area.LayerIDList],
            Money = StartMoney,
            ActionPoint = StartActionPoint,
            ReviveCost = ReviveCost,
            Dice = new ChessRogueDiceStateData { Surfaces = new Dictionary<int, int>(GetDiceSurfaces(branchId)) }
        };

        if (!GenerateLayer(run)) return Retcode.RetRogueAreaInvalid;
        if (!await ActivateLineup(avatarIds)) return Retcode.RetRogueStatusNotMatch;

        RunInstance = run;
        if (!await EnterCell(run.StartCellId, 0))
        {
            await ClearRun();
            return Retcode.RetRogueAreaInvalid;
        }

        Save();
        return Retcode.RetSucc;
    }

    private static bool GenerateLayer(ChessRogueRunData run)
    {
        var level = run.CurLayerIndex + 1;
        var board = ChessRogueResources.PickBoard(run.SubMode, level);
        if (board?.MapInfo == null) return false;

        run.LayerEntranceId = GameConstants.AllowedChessRogueEntranceId.RandomElement();
        run.BoardId = board.ChessBoardID;
        run.StartCellId = board.MapInfo.StartGridItemID;
        run.CurCellId = 0;
        run.FirstEnterBattle = true;
        run.LevelStatus = ChessRogueStates.LevelProcessing;
        run.Cells.Clear();
        run.HistoryCellIds.Clear();
        run.CanMoveCellIds.Clear();

        foreach (var (cellId, item) in board.MapInfo.RogueChestGridItemMap)
        {
            var cell = new ChessRogueCellData
            {
                CellId = cellId,
                PosX = item.PosX,
                PosY = item.PosY,
                BlockType = (int)ChessRogueResources.RollBlockType(item)
            };
            ChessRogueResources.InitCellMonsters(run, cell, level);
            run.Cells[cellId] = cell;
        }

        return run.Cells.ContainsKey(run.StartCellId);
    }

    public async ValueTask<bool> EnterCell(int cellId, int monsterId)
    {
        var run = RunInstance;
        if (run == null || !run.Cells.TryGetValue(cellId, out var cell)) return false;

        if (monsterId > 0)
        {
            cell.SelectMonsterId = monsterId;
            var picked = cell.Monsters.Find(monster => monster.MonsterId == monsterId);
            if (picked != null)
            {
                if (run.BossAeonId == 0) run.BossAeonId = Math.Max((picked.BossDecayId - 101) / 10, 0);
                if (GameData.RogueDLCBossDecayData.ContainsKey(picked.BossDecayId))
                    run.BossDecayIds.Add(picked.BossDecayId);
            }
        }

        if (!ChessRogueResources.RollRoom(run, cell)) return false;

        run.CurCellId = cellId;
        cell.Status = ChessRogueStates.CellFinish;
        run.HistoryCellIds.Add(cellId);
        run.Dice.CurSurfaceId = 0;
        run.Dice.Status = ChessRogueStates.DiceIdle;
        Save();

        await Player.EnterSceneByEntranceId(cell.EntranceId, cell.AnchorGroup, cell.AnchorId, false);
        await SendDiceNotify(run);
        return true;
    }

    public async ValueTask<Retcode> SelectCell(int cellId)
    {
        var run = RunInstance;
        if (run == null || !run.Cells.TryGetValue(cellId, out var cell)) return Retcode.RetRogueStatusNotMatch;
        if (cell.Status == ChessRogueStates.CellFinish) return Retcode.RetFail;

        cell.Status = ChessRogueStates.CellSelected;
        await SendCellUpdate(run, [cell], RogueModifierSourceType.RogueModifierSourceNone, false);
        await CostActionPoint(run, 1);

        if (run.Modifier != null) await ModifierSelectCell(run, run.Modifier, cellId);

        Save();
        return Retcode.RetSucc;
    }

    public async ValueTask<Retcode> EnterNextLayer()
    {
        var run = RunInstance;
        if (run == null || run.CurLayerIndex + 1 >= run.LayerIds.Count) return Retcode.RetRogueStatusNotMatch;

        run.CurLayerIndex++;
        if (!GenerateLayer(run) || !await EnterCell(run.StartCellId, 0))
        {
            run.CurLayerIndex--;
            return Retcode.RetRogueAreaInvalid;
        }

        Save();
        return Retcode.RetSucc;
    }

    /// <summary>Ends the run (leave or quit) and returns the finished run for the settlement response.</summary>
    public async ValueTask<ChessRogueRunData?> EndRun()
    {
        var run = RunInstance;
        if (run == null) return null;

        if (run.LevelStatus == ChessRogueStates.LevelProcessing) run.LevelStatus = ChessRogueStates.LevelFinish;
        await SendLevelStatusNotify(run);

        await ClearRun();
        await Player.EnterScene(RogueLobby.EntranceId, 0, false);
        await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.RogueFinishCnt);
        return run;
    }

    private async ValueTask ClearRun()
    {
        RunInstance = null;
        await RogueExtraLineup.Release(Player);
        Save();
    }

    // Board runs open with full energy, the other rogue modes with half.
    private ValueTask<bool> ActivateLineup(List<int> avatarIds)
    {
        return RogueExtraLineup.Activate(Player, ExtraLineupType.LineupChessRogue, avatarIds,
            new RogueLineupStart(StartMp, RogueExtraLineup.FullSp, RogueLineupRefill.RequestedFormalAvatars));
    }

    #endregion

    #region Dice

    public async ValueTask<KFACLOGFNJF?> RollDice()
    {
        var run = RunInstance;
        if (run == null || run.Dice.Surfaces.Count == 0) return null;

        run.Dice.CurSurfaceId = run.Dice.Surfaces.Values.ToList().RandomElement();
        run.Dice.Status = ChessRogueStates.DiceRolled;
        Save();
        await SendDiceNotify(run);
        return ChessRogueProtoBuilder.BuildDice(run);
    }

    public async ValueTask<KFACLOGFNJF?> RerollDice()
    {
        var run = RunInstance;
        if (run == null || run.Dice.RerollTimes <= 0) return null;

        run.Dice.RerollTimes--;
        return await RollDice();
    }

    public async ValueTask<KFACLOGFNJF?> CheatDice(int surfaceId)
    {
        var run = RunInstance;
        if (run == null || run.Dice.CheatTimes <= 0 || !run.Dice.Surfaces.ContainsValue(surfaceId)) return null;

        run.Dice.CheatTimes--;
        run.Dice.CurSurfaceId = surfaceId;
        Save();
        await SendDiceNotify(run);
        return ChessRogueProtoBuilder.BuildDice(run);
    }

    public async ValueTask<KFACLOGFNJF?> ConfirmDice()
    {
        var run = RunInstance;
        if (run == null) return null;

        run.Dice.Status = ChessRogueStates.DiceConfirmed;
        var effect = ChessRogueResources.SurfaceEffect(run.Dice.CurSurfaceId);
        if (effect != null)
        {
            if (run.Modifier != null)
                await Player.SendPacket(BasePacket.Create(CmdIds.RogueModifierDelNotify,
                    new RogueModifierDelNotify { FCHLCANJIGL = (ulong)run.Modifier.ModifierId }));

            run.Modifier = new ChessRogueModifierData
            {
                ModifierId = run.NextModifierId++,
                SurfaceId = run.Dice.CurSurfaceId
            };
            await ModifierOnConfirmed(run, run.Modifier, effect);
            await Player.SendPacket(BasePacket.Create(CmdIds.RogueModifierAddNotify,
                new RogueModifierAddNotify { Modifier = ChessRogueProtoBuilder.BuildModifier(run.Modifier) }));
        }

        Save();
        await SendDiceNotify(run);
        return ChessRogueProtoBuilder.BuildDice(run);
    }

    /// <summary>RogueModifierSelectCellCsReq: cell 0 skips the modifier for a small money refund.</summary>
    public async ValueTask<Retcode> ApplyModifier(int cellId)
    {
        var run = RunInstance;
        if (run?.Modifier == null) return Retcode.RetRogueStatusNotMatch;

        if (cellId == 0)
        {
            run.Modifier.IsConfirmed = true;
            await SendModifierStageStart();
            await GainMoney(run, 10);
        }
        else
        {
            await ModifierSelectModifierCell(run, run.Modifier, cellId);
        }

        Save();
        await SendDiceNotify(run);
        return Retcode.RetSucc;
    }

    #endregion

    #region Battle

    public void OnBattleStart(BattleInstance battle)
    {
        var run = RunInstance;
        if (run == null || run.LevelStatus != ChessRogueStates.LevelProcessing) return;
        if (Player.SceneInstance?.GameModeType != GameModeTypeEnum.ChessRogue) return;
        var cell = run.CurCell();
        if (cell == null) return;

        foreach (var (buffId, level) in run.Buffs)
            battle.Buffs.Add(new MazeBuff(buffId, level, -1) { WaveFlag = -1 });

        var difficulties = GameData.RogueDLCAreaData.GetValueOrDefault(run.AreaId)?.DifficultyID
            .Select(id => GameData.RogueDLCDifficultyData.GetValueOrDefault(id))
            .OfType<RogueDLCDifficultyExcel>()
            .Where(difficulty => difficulty.LevelList.Count > 0)
            .ToList() ?? [];
        if (difficulties.Count > 0)
        {
            battle.CustomLevel = difficulties.RandomElement().LevelList.RandomElement();
            AddDifficultyBuffs(run, battle);
        }

        if (run.ActionPoint < 0 &&
            GameData.ActionPointOverdrawData.TryGetValue(Math.Max(run.ActionPoint, ActionPointOverdrawFloor),
                out var overdraw))
            battle.Buffs.Add(new MazeBuff(overdraw.MazeBuff, 1, -1) { WaveFlag = -1 });

        if ((RogueDLCBlockTypeEnum)cell.BlockType is RogueDLCBlockTypeEnum.MonsterNousBoss
            or RogueDLCBlockTypeEnum.MonsterSwarmBoss)
        {
            List<int> bossBuffs = [];
            foreach (var decay in run.BossDecayIds.Select(id => GameData.RogueDLCBossDecayData.GetValueOrDefault(id))
                         .OfType<RogueDLCBossDecayExcel>())
                if (decay.EffectType == BossDecayEffectTypeEnum.AddMazeBuffList)
                    bossBuffs.AddRange(decay.EffectParamList);
                else
                    foreach (var param in decay.EffectParamList)
                        bossBuffs.Remove(param);

            foreach (var buffId in bossBuffs) battle.Buffs.Add(new MazeBuff(buffId, 1, -1) { WaveFlag = -1 });
        }

        if (run.Modifier != null) ModifierBeforeBattle(run, run.Modifier, battle);

        battle.OnBattleEnd += OnBattleEnd;
    }

    // Difficulty dials 101..106: extra buffs on 103/105 and one attribute buff scaled by the layer.
    private static void AddDifficultyBuffs(ChessRogueRunData run, BattleInstance battle)
    {
        if (run.DifficultyIds.Contains(103)) battle.Buffs.Add(new MazeBuff(610121, 1, -1) { WaveFlag = -1 });
        if (run.DifficultyIds.Contains(105)) battle.Buffs.Add(new MazeBuff(610121, 1, -1) { WaveFlag = -1 });

        var attributeBuffId = run.DifficultyIds.Contains(106) ? 650204
            : run.DifficultyIds.Contains(104) ? 650203
            : run.DifficultyIds.Contains(102) ? 650202
            : run.DifficultyIds.Contains(101) ? 650201
            : 0;
        if (attributeBuffId == 0) return;

        var buff = new MazeBuff(attributeBuffId, 1, -1) { WaveFlag = -1 };
        buff.DynamicValues["_RogueLayer"] = run.CurLayerIndex + 1;
        battle.Buffs.Add(buff);
    }

    private async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        var run = RunInstance;
        if (run == null || run.LevelStatus != ChessRogueStates.LevelProcessing) return;
        var cell = run.CurCell();
        if (cell == null) return;

        if (req.EndStatus != BattleEndStatus.BattleEndWin)
        {
            run.LevelStatus = ChessRogueStates.LevelFailed;
            Save();
            await SendLevelStatusNotify(run);
            return;
        }

        var blockType = (RogueDLCBlockTypeEnum)cell.BlockType;
        OpenDraft(run, blockType == RogueDLCBlockTypeEnum.MonsterBoss
            ? RogueDraftGroups.Elite
            : RogueDraftGroups.Normal);

        if (blockType is RogueDLCBlockTypeEnum.MonsterNousBoss or RogueDLCBlockTypeEnum.MonsterSwarmBoss)
            run.LevelStatus = ChessRogueStates.LevelFinish;

        Save();
        await SendPendingNotify(run);

        if (blockType is RogueDLCBlockTypeEnum.MonsterBoss or RogueDLCBlockTypeEnum.MonsterNousBoss
            or RogueDLCBlockTypeEnum.MonsterSwarmBoss)
            await Player.SendPacket(BasePacket.Create(CmdIds.ChessRogueLayerSettlement,
                new ChessRogueLayerSettlement { MINFCKLKLNJ = ChessRogueProtoBuilder.BuildFinishInfo(run, Player) }));

        if (run.LevelStatus == ChessRogueStates.LevelFinish) await SendLevelStatusNotify(run);
    }

    #endregion

    #region Blessing draft

    // A draft replaces any unanswered one; the legacy queue of several drafts is collapsed to the latest.
    private static void OpenDraft(ChessRogueRunData run, int groupId)
    {
        var offer = ChessRogueResources.DraftOffer(groupId, run);
        if (offer.Count == 0) return;

        run.ActionSeq += 2;
        run.PendingBuffKeys = offer.Select(buff => buff.MazeBuffID * 100 + buff.MazeBuffLevel).ToList();
    }

    private static void AddBuff(ChessRogueRunData run, BaseRogueBuffExcel buff)
    {
        if (!run.Buffs.TryGetValue(buff.MazeBuffID, out var level))
        {
            run.Buffs[buff.MazeBuffID] = buff.MazeBuffLevel;
            return;
        }

        if (GameData.RogueBuffData.ContainsKey(buff.MazeBuffID * 100 + level + 1)) run.Buffs[buff.MazeBuffID] = level + 1;
    }

    /// <summary>
    ///     Resolves a HandleRogueCommonPendingActionCsReq (5611) blessing answer for a live ChessRogue run.
    ///     Returns null when the answer does not belong to this run, so the caller falls through to the other modes.
    /// </summary>
    public async ValueTask<HandleRogueCommonPendingActionScRsp?> HandlePendingAction(OFEDNNJIJFL req)
    {
        var run = RunInstance;
        if (run == null || run.LevelStatus != ChessRogueStates.LevelProcessing) return null;
        if (Player.SceneInstance?.GameModeType != GameModeTypeEnum.ChessRogue) return null;
        if (req.DLKIKBIDJDB == null || run.PendingBuffKeys.Count == 0) return null;

        var rsp = new HandleRogueCommonPendingActionScRsp
        {
            QueuePosition = req.MPLGEEGOAMC,
            BFNEPPCNOOI = req.MPLGEEGOAMC
        };

        var buffId = (int)req.DLKIKBIDJDB.BAOFIGFFMLN;
        var key = run.PendingBuffKeys.FirstOrDefault(k => k / 100 == buffId);
        if (key == 0 || !GameData.RogueBuffData.TryGetValue(key, out var buff))
        {
            rsp.Retcode = (uint)Retcode.RetRogueSelectBuffNotExist;
            return rsp;
        }

        AddBuff(run, buff);
        run.PendingBuffKeys.Clear();
        Save();
        await ValueTask.CompletedTask;
        return rsp;
    }

    #endregion

    #region Scene

    /// <summary>
    ///     Entity loader for a Rogue plane scene that belongs to the current chess cell, or null otherwise.
    ///     Stamps the scene's game mode (ChessRogue = 16; the plane's own type is RogueExplore).
    /// </summary>
    public SceneEntityLoader? CreateEntityLoader(SceneInstance scene)
    {
        var run = RunInstance;
        var cell = run?.LevelStatus == ChessRogueStates.LevelProcessing ? run.CurCell() : null;
        if (run == null || cell == null || cell.EntranceId == 0) return null;
        if (!GameData.MapEntranceData.TryGetValue(cell.EntranceId, out var entrance) ||
            entrance.PlaneID != scene.PlaneId) return null;

        scene.GameModeType = GameModeTypeEnum.ChessRogue;
        return new ChessRogueEntityLoader(scene, cell);
    }

    #endregion

    #region Proto

    public DJEKEGMEHLK BuildGameInfo()
    {
        var run = RunInstance;
        return run == null
            ? ChessRogueProtoBuilder.BuildLobbyGameInfo()
            : ChessRogueProtoBuilder.BuildRunGameInfo(run, TakeAllowedCells(run));
    }

    public LKPCDHCKHKP? BuildLevelInfo(ChessRogueRunData run)
    {
        return ChessRogueProtoBuilder.BuildLevelInfo(run, TakeAllowedCells(run));
    }

    // Idle neighbours of the current cell (one column ahead on the adjacent rows, two on the same row), plus any
    // cells a SetColCanMove modifier unlocked; that grant is consumed here, as in the legacy board.
    private List<uint> TakeAllowedCells(ChessRogueRunData run)
    {
        List<uint> allowed = [];
        if (run.CurCell() is { } cur)
            foreach (var cell in run.Cells.Values.Where(cell => cell.Status == ChessRogueStates.CellIdle))
            {
                if ((cell.PosY == cur.PosY - 1 || cell.PosY == cur.PosY + 1) &&
                    (cell.PosX == cur.PosX || cell.PosX == cur.PosX + 1))
                    allowed.Add((uint)cell.CellId);
                if (cell.PosY == cur.PosY && cell.PosX == cur.PosX + 2) allowed.Add((uint)cell.CellId);
            }

        allowed.AddRange(run.CanMoveCellIds.Select(id => (uint)id));
        if (run.CanMoveCellIds.Count > 0)
        {
            run.CanMoveCellIds.Clear();
            Save();
        }

        return allowed;
    }

    #endregion

    #region Notify

    private async ValueTask SendDiceNotify(ChessRogueRunData run)
    {
        await Player.SendPacket(BasePacket.Create(CmdIds.ChessRogueUpdateDiceInfoScNotify,
            new ChessRogueUpdateDiceInfoScNotify { POHEPEEFBOA = ChessRogueProtoBuilder.BuildDice(run) }));
    }

    private async ValueTask SendLevelStatusNotify(ChessRogueRunData run)
    {
        await Player.SendPacket(BasePacket.Create(CmdIds.ChessRogueUpdateLevelBaseInfoScNotify,
            new ChessRogueUpdateLevelBaseInfoScNotify { GKDCPNLOLNF = (GDHLDNFLOOF)run.LevelStatus }));
    }

    private async ValueTask CostActionPoint(ChessRogueRunData run, int cost)
    {
        run.ActionPoint -= cost;
        await Player.SendPacket(BasePacket.Create(CmdIds.ChessRogueUpdateActionPointScNotify,
            new ChessRogueUpdateActionPointScNotify { EABEFBDOJEO = run.ActionPoint }));
    }

    private async ValueTask GainMoney(ChessRogueRunData run, int amount)
    {
        run.Money += amount;
        await Player.SendPacket(BasePacket.Create(CmdIds.ChessRogueUpdateMoneyInfoScNotify,
            new ChessRogueUpdateMoneyInfoScNotify { DHEKLJDBNNP = new AGEOGDOGDMM { RogueMoney = (uint)run.Money } }));
    }

    private async ValueTask SendCellUpdate(ChessRogueRunData run, IEnumerable<ChessRogueCellData> cells,
        RogueModifierSourceType source, bool byModifier)
    {
        var notify = new ChessRogueCellUpdateNotify
        {
            AIHCKNAMPBP = (uint)run.BoardId,
            CFAOFCGJBMA = source,
            Reason = byModifier ? AIMCAMJABNA.Iliioljmkme : AIMCAMJABNA.Nlcdgipgfdj
        };
        notify.CPPCIGMJALM.AddRange(cells.Select(ChessRogueProtoBuilder.BuildCell));
        await Player.SendPacket(BasePacket.Create(CmdIds.ChessRogueCellUpdateNotify, notify));
    }

    private async ValueTask SendModifierStageStart()
    {
        await Player.SendPacket(BasePacket.Create(CmdIds.RogueModifierStageStartNotify,
            new RogueModifierStageStartNotify { SourceType = RogueModifierSourceType.RogueModifierSourceDiceRoll }));
    }

    public async ValueTask SendPendingNotify(ChessRogueRunData run)
    {
        if (run.PendingBuffKeys.Count == 0) return;
        await Player.SendPacket(BasePacket.Create(CmdIds.SyncRogueCommonPendingActionScNotify,
            new SyncRogueCommonPendingActionScNotify
            {
                Action = ChessRogueProtoBuilder.BuildPendingAction(run),
                DLOJFEEHLIJ = (uint)run.SubMode
            }));
    }

    #endregion
}

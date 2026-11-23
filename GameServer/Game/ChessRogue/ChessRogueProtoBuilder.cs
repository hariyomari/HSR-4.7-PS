using March7thHoney.Data;
using March7thHoney.Database.ChessRogue;
using March7thHoney.Enums.Rogue;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.ChessRogue;

/// <summary>
///     ChessRogue wire builders. Lobby shapes follow yuukisr_go's capture-verified ChessRogueQueryScRsp; run-time
///     scalars inside the dice, cell and board messages are placed by structure only (no capture available).
/// </summary>
public static class ChessRogueProtoBuilder
{
    // The official query stamps sub mode 201 and lists aeons 1..7 although RogueNousAeon ships 9 rows.
    private const uint LobbySubMode = 201;
    private const int AeonLimit = 7;
    private const uint RogueMoneyItemId = 31;

    #region Lobby

    public static HIMJKAMHLMJ BuildQueryInfo(ChessRogueManager manager)
    {
        var info = new HIMJKAMHLMJ
        {
            IOCCACNJIGD = BuildQueryDiceInfo(manager),
            JCOBJAMPOAL = BuildTalentTree(),
            EIIHJJEBKOE = new LPCINMGGCNI(),
            FDEEBBFMBLB = new FJKGDHKKDFF()
        };
        foreach (var areaId in GameData.RogueDLCAreaData.Keys.OrderBy(x => x))
        {
            info.FEBKOJODAAM.Add((uint)areaId);
            info.HKCDKHJCNDC.Add((uint)areaId);
        }

        return info;
    }

    public static PPAGCEGGCAK BuildGetInfo(ChessRogueManager manager)
    {
        var info = new PPAGCEGGCAK
        {
            IOCCACNJIGD = BuildQueryDiceInfo(manager),
            JCOBJAMPOAL = BuildTalentTree(),
            EIIHJJEBKOE = new LPCINMGGCNI(),
            FDEEBBFMBLB = new FJKGDHKKDFF()
        };
        foreach (var areaId in GameData.RogueDLCAreaData.Keys.OrderBy(x => x))
        {
            info.FEBKOJODAAM.Add((uint)areaId);
            info.HKCDKHJCNDC.Add((uint)areaId);
        }

        return info;
    }

    private static MOOKMKEKMKC BuildQueryDiceInfo(ChessRogueManager manager)
    {
        var dice = new MOOKMKEKMKC();
        for (var slot = 1; slot <= ChessRogueResources.DiceSlotCount; slot++) dice.PEGDMOMCFAD[(uint)slot] = false;
        dice.ADLKMLFOFPF.AddRange(GameData.RogueNousDiceSurfaceData.Keys.OrderBy(x => x).Select(x => (uint)x));
        foreach (var branchId in GameData.RogueNousDiceBranchData.Keys.OrderBy(x => x))
            dice.BILCGENIGJM.Add(BuildDiceBranch(branchId, manager.GetDiceSurfaces(branchId)));
        return dice;
    }

    public static HGCIAPKELAF BuildDiceBranch(int branchId, Dictionary<int, int> surfaces)
    {
        var row = new HGCIAPKELAF { PDFAHEKNHNJ = (uint)branchId };
        foreach (var (slot, surfaceId) in surfaces.OrderBy(x => x.Key))
            row.LFLFEKEAOCL.Add(new EditChessRogueNousBranchSurfaceCsReq
                { SlotId = (uint)slot, AMPFBGKHJBF = (uint)surfaceId });
        return row;
    }

    // Every Nous talent is reported Enable: this server grants full ChessRogue access.
    public static FDBDLBAPDBE BuildTalentInfo()
    {
        var info = new FDBDLBAPDBE();
        foreach (var talentId in GameData.RogueNousTalentData.Keys.OrderBy(x => x))
            info.BLHJIPBHHFO.Add(new OKPIPFKONMG { CKHCLKOALKE = (uint)talentId, Status = RogueTalentStatus.Enable });
        return info;
    }

    private static DDAFEPBGOHK BuildTalentTree()
    {
        return new DDAFEPBGOHK { GMILDFJFOGD = BuildTalentInfo() };
    }

    public static AEMAPGKPPJA BuildAeonInfo(ChessRogueRunData? run)
    {
        var info = new AEMAPGKPPJA { FDEEBBFMBLB = new FJKGDHKKDFF() };
        info.OPAEGCBNACJ.AddRange(GameData.RogueNousAeonData.Keys
            .Where(id => id is > 0 and <= AeonLimit)
            .OrderBy(id => id)
            .Select(id => (uint)id));
        if (run != null) info.JPPAGGPJOMO = (uint)run.AeonId;
        return info;
    }

    /// <summary>
    ///     The idle game info. Presence matters, not values: the client NREs in ChessRogueAreaDataItem.Sync when
    ///     the level info's board or its grid/layout arms decode as null (yuukisr_go GAP_ANALYSIS §2C.7).
    /// </summary>
    public static DJEKEGMEHLK BuildLobbyGameInfo()
    {
        return new DJEKEGMEHLK
        {
            DNPCCFFFBHN = new NHLBNBBDHMI { HAJJBOFCFOO = new GMIJHCONFIH() },
            HABJHGPFGEB = new EGLAPJGFKPN { JPKACKNNHCA = new APNCCNFJBKO() },
            EIOHFJMFOKF = new KCHPLCFMGLF { MFEODNJNGDP = new GAJDPEBBFMG { LNIEFKBMIPD = new ItemCostData() } },
            StoryInfo = new DLCNDCKCIPC(),
            DHEKLJDBNNP = new AGEOGDOGDMM(),
            POHEPEEFBOA = new KFACLOGFNJF(),
            FMIGOKIDKJL = new INHBMHCJJCN(),
            OEIBHLMDJKA = new LKPCDHCKHKP
            {
                Board = new DPELLHBJGML { ONKBPKPOHEC = new IKCONCOKNAN(), BAGNPGNGLOJ = new EJHDHGGMILA() }
            },
            AEFMMOHKCHJ = BuildAeonInfo(null),
            DLOJFEEHLIJ = LobbySubMode
        };
    }

    public static GetChessRogueNousStoryInfoScRsp BuildNousStoryInfo()
    {
        var rsp = new GetChessRogueNousStoryInfoScRsp();
        foreach (var storyId in GameData.RogueNousMainStoryData.Keys.OrderBy(x => x))
            rsp.FNKLMMACCMJ.Add(new PJBODOPJFGA { KPDLMKFLJAI = (uint)storyId, Status = FILMJJMEBJA.Cihfgjdchia });
        foreach (var storyId in GameData.RogueNousSubStoryData.Keys.OrderBy(x => x))
            rsp.POABJDELNGH.Add(new BKJCADFCHAB { HONCDMKPBEG = (uint)storyId });
        return rsp;
    }

    #endregion

    #region Run

    public static DJEKEGMEHLK BuildRunGameInfo(ChessRogueRunData run, List<uint> allowedCells)
    {
        var info = BuildLobbyGameInfo();
        info.HABJHGPFGEB.JPKACKNNHCA = BuildBuffList(run);
        info.EIOHFJMFOKF = BuildLineupInfo(run);
        info.DHEKLJDBNNP.RogueMoney = (uint)run.Money;
        info.POHEPEEFBOA = BuildDice(run);
        info.FMIGOKIDKJL = BuildPendingAction(run);
        info.OEIBHLMDJKA = BuildLevelInfo(run, allowedCells);
        info.AEFMMOHKCHJ = BuildAeonInfo(run);
        info.DLOJFEEHLIJ = (uint)run.SubMode;
        return info;
    }

    public static MANINNKMFHG BuildStage(PlayerInstance player)
    {
        var lineup = player.LineupManager!.GetCurLineup()!.ToProto();
        player.LineupManager.ApplyMaxMp(lineup);
        var scene = player.SceneInstance!;
        var stage = new MANINNKMFHG { Lineup = lineup, Scene = scene.ToProto() };
        scene.MarkClientSceneSnapshotSynced();
        return stage;
    }

    public static LKPCDHCKHKP BuildLevelInfo(ChessRogueRunData run, List<uint> allowedCells)
    {
        var info = new LKPCDHCKHKP
        {
            GKDCPNLOLNF = (uint)run.LevelStatus,
            EABEFBDOJEO = run.ActionPoint,
            Id = (uint)run.AreaId,
            // MDAIAMFJNHM sits next to LayerIndex in the other rogue layer structs, so it carries the layer id.
            MDAIAMFJNHM = (uint)run.CurLayerId(),
            MLLDCLNMIOG = (uint)(run.CurLayerIndex + 1),
            Board = new DPELLHBJGML
            {
                AAKGDLDFIIE = (uint)run.CurCellId,
                EPOHKNLFEAB = (uint)run.BoardId,
                ONKBPKPOHEC = BuildGrid(run),
                BAGNPGNGLOJ = new EJHDHGGMILA()
            }
        };
        info.Board.AADAFLKEJGD.AddRange(allowedCells);
        foreach (var cellId in run.HistoryCellIds) info.Board.AGIJECGIBGJ.Add(new GKDJBPJOGPO { IHAOMLEPHOM = (uint)cellId });
        if (run.Modifier != null)
            info.Board.BAGNPGNGLOJ.OLJCAJMFJPO = new LMNCBIDKHLF { HDLGLFGKONC = { BuildModifier(run.Modifier) } };
        return info;
    }

    private static IKCONCOKNAN BuildGrid(ChessRogueRunData run)
    {
        var grid = new IKCONCOKNAN { GHKDAJNDBBA = (uint)run.BoardId };
        if (GameData.RogueSwarmChessBoardData.Values.Concat(GameData.RogueNousChessBoardData.Values)
                .SelectMany(boards => boards)
                .FirstOrDefault(board => board.ChessBoardID == run.BoardId)?.MapInfo is { } map)
        {
            grid.Width = (uint)map.Width;
            grid.Height = (uint)map.Height;
        }

        grid.CPPCIGMJALM.AddRange(run.Cells.Values.OrderBy(cell => cell.CellId).Select(BuildCell));
        return grid;
    }

    public static OFGJFLCBBJP BuildBoardSnapshot(ChessRogueRunData run)
    {
        var board = new OFGJFLCBBJP { EIMFLPNFPGM = (uint)run.BoardId };
        board.CPPCIGMJALM.AddRange(run.Cells.Values.OrderBy(cell => cell.CellId).Select(BuildCell));
        return board;
    }

    public static CGMCMAKMJDK BuildCell(ChessRogueCellData cell)
    {
        var proto = new CGMCMAKMJDK
        {
            Id = (uint)cell.CellId,
            RoomId = (uint)cell.RoomId,
            IsUnlock = true,
            LDDHLGINDIJ = true,
            EJEDLGNLOHB = (ChessRogueBoardCellStatus)cell.Status,
            KFEMJMKMGIJ = ChessRogueCellSpecialType.None,
            JMPMDGJLIPF = (uint)cell.PosX,
            HLALFNEDFED = (uint)cell.PosY,
            CFGEJHAOKED = (uint)cell.BlockType,
            PDKOHKKMKLF = (uint)cell.MarkType
        };

        if (cell.Monsters.Count == 0) return proto;

        var monsters = new DFKMNLIJMFB { POFBAHEECGB = (uint)cell.SelectMonsterId };
        monsters.GEJNMNBLFLG.AddRange(cell.Monsters.Select(monster => new DECHJACNLGP
            { MonsterId = (uint)monster.MonsterId, BJBALPKFELE = (uint)monster.BossDecayId }));

        proto.StageInfo = cell.SelectedDecayIds.Count > 0
            ? new MCDONFNGMJE
            {
                LPAAMNFFAIB = new JKLCLLAFHOC
                {
                    MGACFLNJLCK = monsters,
                    MKMLKGOOAMK = new JECIOGKPHFN { JJHFHLAHLGP = { cell.SelectedDecayIds.Select(x => (uint)x) } }
                }
            }
            : new MCDONFNGMJE { MGACFLNJLCK = monsters };
        return proto;
    }

    public static KFACLOGFNJF BuildDice(ChessRogueRunData run)
    {
        var slot = run.Dice.Surfaces.FirstOrDefault(x => x.Value == run.Dice.CurSurfaceId && run.Dice.CurSurfaceId != 0).Key;
        var dice = new KFACLOGFNJF
        {
            CEBDDDJMJIJ = ChessRogueDiceType.ChessRogueDiceEditable,
            OACDALINONI = (ChessRogueDiceStatus)run.Dice.Status,
            GMDKBGODBCB = BuildDiceBranch(run.BranchId, run.Dice.Surfaces),
            ONCIOGBKPPI = (uint)run.BranchId,
            EFDMLFGILKK = (uint)run.BranchId,
            FLADMNIKGEI = (uint)run.Dice.CurSurfaceId,
            DGLIOIEKGNP = (uint)slot,
            DGHEPBKLKDP = (uint)Math.Max(run.Dice.RerollTimes, 0),
            JDLKMDFFMGP = (uint)Math.Max(run.Dice.CheatTimes, 0),
            BGDHHJLICFA = run.Dice.RerollTimes > 0
        };
        if (run.Modifier != null) dice.OBLADGDFCCN = BuildModifier(run.Modifier);
        return dice;
    }

    public static BMCNDLNONMO BuildModifier(ChessRogueModifierData modifier)
    {
        var effect = ChessRogueResources.SurfaceEffect(modifier.SurfaceId);
        var proto = new BMCNDLNONMO
        {
            FCHLCANJIGL = (ulong)modifier.ModifierId,
            SourceType = RogueModifierSourceType.RogueModifierSourceDiceRoll,
            OOPCPMANCBO = new KIJEFIIBBGG { IKDHAMLPNPJ = (uint)(effect?.EffectType ?? ModifierEffectTypeEnum.None) },
            NLGJFIGPENF = new FGHIMGPGBNF
            {
                FOHIFIMHENN = (uint)modifier.SelectedCell,
                IsSelected = modifier.IsConfirmed
            }
        };
        proto.NLGJFIGPENF.OGOILMLPDEO.AddRange(modifier.SelectableCells.Select(x => (uint)x));
        return proto;
    }

    private static APNCCNFJBKO BuildBuffList(ChessRogueRunData run)
    {
        var list = new APNCCNFJBKO();
        foreach (var (buffId, level) in run.Buffs)
            list.BuffList.Add(new KJAMNOGFAMJ { BuffId = (uint)buffId, HAGDKKBAAKK = (uint)level });
        return list;
    }

    private static KCHPLCFMGLF BuildLineupInfo(ChessRogueRunData run)
    {
        var info = new KCHPLCFMGLF
        {
            MFEODNJNGDP = new GAJDPEBBFMG
            {
                LNIEFKBMIPD = new ItemCostData
                {
                    ItemList =
                    {
                        new ItemCost
                            { PileItem = new PileItem { ItemId = RogueMoneyItemId, ItemNum = (uint)run.ReviveCost } }
                    }
                }
            }
        };
        info.JOLEGACINNK.AddRange(run.BaseAvatarIds.Select(id => new IFFEHFHAKCB { AvatarId = (uint)id }));
        return info;
    }

    public static INHBMHCJJCN BuildPendingAction(ChessRogueRunData run)
    {
        var action = new INHBMHCJJCN();
        if (run.PendingBuffKeys.Count == 0) return action;

        var buffs = run.PendingBuffKeys
            .Select(key => GameData.RogueBuffData.GetValueOrDefault(key))
            .OfType<Data.Excel.BaseRogueBuffExcel>()
            .ToList();

        // Same draft shape as the classic Simulated Universe blessing select.
        var select = new GGGBAIAPDLO { NKGGAOJLCMD = 1, EIALMEADLGF = 1, FIMMFJCKCNK = 1 };
        select.EBMEEKJGPPM.AddRange(buffs.Select(x => (uint)x.RogueBuffType).Distinct());
        foreach (var buff in buffs)
            select.LEOFNINDJDB.Add(new KJAMNOGFAMJ { BuffId = (uint)buff.MazeBuffID, HAGDKKBAAKK = (uint)buff.MazeBuffLevel });

        action.QueuePosition = (uint)run.ActionSeq;
        action.Detail = new DCFPEIMAMCN { PCHGEGFBKLF = select };
        return action;
    }

    public static OGHPKFMNNHL BuildFinishInfo(ChessRogueRunData run, PlayerInstance player)
    {
        var info = new OGHPKFMNNHL
        {
            DLOJFEEHLIJ = (uint)run.SubMode,
            HABJHGPFGEB = BuildBuffList(run),
            DNPCCFFFBHN = new GMIJHCONFIH(),
            IsFinish = run.LevelStatus == ChessRogueStates.LevelFinish
        };
        if (player.LineupManager?.GetCurLineup() is { } lineup) info.BJDLGBBPFCK = lineup.ToProto();
        return info;
    }

    #endregion
}

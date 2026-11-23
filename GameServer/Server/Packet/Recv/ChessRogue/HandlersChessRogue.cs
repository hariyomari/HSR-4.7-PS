using March7thHoney.GameServer.Game.ChessRogue;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.ChessRogue;

[Opcode(CmdIds.ChessRogueQueryCsReq)]
public sealed class HandlerChessRogueQueryCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.ChessRogueManager!;
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueQueryScRsp, new ChessRogueQueryScRsp
        {
            IHNENEGKEPO = ChessRogueProtoBuilder.BuildQueryInfo(manager),
            LFLCDNGALME = ChessRogueProtoBuilder.BuildGetInfo(manager),
            Info = manager.BuildGameInfo()
        }));
    }
}

[Opcode(CmdIds.GetChessRogueNousStoryInfoCsReq)]
public sealed class HandlerGetChessRogueNousStoryInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.GetChessRogueNousStoryInfoScRsp,
            ChessRogueProtoBuilder.BuildNousStoryInfo()));
    }
}

[Opcode(CmdIds.ChessRogueNousGetRogueTalentInfoCsReq)]
public sealed class HandlerChessRogueNousGetRogueTalentInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueNousGetRogueTalentInfoScRsp,
            new ChessRogueNousGetRogueTalentInfoScRsp { JCOBJAMPOAL = ChessRogueProtoBuilder.BuildTalentInfo() }));
    }
}

// Every Nous talent is already reported enabled, so enabling one only echoes the tree.
[Opcode(CmdIds.ChessRogueNousEnableRogueTalentCsReq)]
public sealed class HandlerChessRogueNousEnableRogueTalentCsReq : Handler<ChessRogueNousEnableRogueTalentCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player,
        ChessRogueNousEnableRogueTalentCsReq req)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueNousEnableRogueTalentScRsp,
            new ChessRogueNousEnableRogueTalentScRsp { JCOBJAMPOAL = ChessRogueProtoBuilder.BuildTalentInfo() }));
    }
}

// The generated set has no ChessRogueNousEditDiceCsReq class; IACEIJKMLLN is the only message carrying a single dice
// row, so it is taken as the request by structure.
[Opcode(CmdIds.IACEIJKMLLN)]
public sealed class HandlerChessRogueNousEditDiceCsReq : Handler<IACEIJKMLLN>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, IACEIJKMLLN req)
    {
        var dice = player.ChessRogueManager!.EditDice(req.JPJPDLBENDH);
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueNousEditDiceScRsp,
            dice == null
                ? new ChessRogueNousEditDiceScRsp { Retcode = (uint)Retcode.RetFail }
                : new ChessRogueNousEditDiceScRsp { IOCCACNJIGD = dice }));
    }
}

[Opcode(CmdIds.ChessRogueStartCsReq)]
public sealed class HandlerChessRogueStartCsReq : Handler<ChessRogueStartCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, ChessRogueStartCsReq req)
    {
        var manager = player.ChessRogueManager!;
        var avatarIds = req.NOEPBNMJFGL.Select(x => (int)x).ToList();
        if (avatarIds.Count == 0) avatarIds = req.JKPIPNMGGOA.Select(x => (int)x).ToList();

        var ret = await manager.StartRun((int)req.Id, (int)req.AFEMBLFCCCH, (int)req.PDFAHEKNHNJ, avatarIds,
            req.OCEGEKKFLAL.Select(x => (int)x).ToList());
        var run = manager.RunInstance;
        if (ret != Retcode.RetSucc || run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueStartScRsp,
                new ChessRogueStartScRsp { Retcode = (uint)(ret == Retcode.RetSucc ? Retcode.RetFail : ret) }));
            return;
        }

        await connection.SendPacket(new PacketSyncLineupNotify(player.LineupManager!.GetCurLineup()!));
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueStartScRsp, new ChessRogueStartScRsp
        {
            StageInfo = ChessRogueProtoBuilder.BuildStage(player),
            Info = manager.BuildGameInfo(),
            HPHKDGJHIJL = ChessRogueProtoBuilder.BuildBoardSnapshot(run)
        }));
    }
}

[Opcode(CmdIds.ChessRogueRollDiceCsReq)]
public sealed class HandlerChessRogueRollDiceCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var dice = await player.ChessRogueManager!.RollDice();
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueRollDiceScRsp,
            dice == null
                ? new ChessRogueRollDiceScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }
                : new ChessRogueRollDiceScRsp { POHEPEEFBOA = dice }));
    }
}

[Opcode(CmdIds.ChessRogueReRollDiceCsReq)]
public sealed class HandlerChessRogueReRollDiceCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var dice = await player.ChessRogueManager!.RerollDice();
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueReRollDiceScRsp,
            dice == null
                ? new ChessRogueReRollDiceScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }
                : new ChessRogueReRollDiceScRsp { POHEPEEFBOA = dice }));
    }
}

// AMPFBGKHJBF is the cheat request's only field besides the id shared with roll/reroll, so it is the chosen surface.
[Opcode(CmdIds.ChessRogueCheatRollCsReq)]
public sealed class HandlerChessRogueCheatRollCsReq : Handler<ChessRogueCheatRollCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, ChessRogueCheatRollCsReq req)
    {
        var dice = await player.ChessRogueManager!.CheatDice((int)req.AMPFBGKHJBF);
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueCheatRollScRsp,
            dice == null
                ? new ChessRogueCheatRollScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }
                : new ChessRogueCheatRollScRsp { POHEPEEFBOA = dice }));
    }
}

[Opcode(CmdIds.ChessRogueConfirmRollCsReq)]
public sealed class HandlerChessRogueConfirmRollCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var dice = await player.ChessRogueManager!.ConfirmDice();
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueConfirmRollScRsp,
            dice == null
                ? new ChessRogueConfirmRollScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }
                : new ChessRogueConfirmRollScRsp { POHEPEEFBOA = dice }));
    }
}

[Opcode(CmdIds.ChessRogueSelectCellCsReq)]
public sealed class HandlerChessRogueSelectCellCsReq : Handler<ChessRogueSelectCellCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, ChessRogueSelectCellCsReq req)
    {
        var ret = await player.ChessRogueManager!.SelectCell((int)req.IHAOMLEPHOM);
        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueSelectCellScRsp,
            new ChessRogueSelectCellScRsp { Retcode = (uint)ret }));
    }
}

[Opcode(CmdIds.ChessRogueEnterCellCsReq)]
public sealed class HandlerChessRogueEnterCellCsReq : Handler<ChessRogueEnterCellCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, ChessRogueEnterCellCsReq req)
    {
        var manager = player.ChessRogueManager!;
        if (!await manager.EnterCell((int)req.IHAOMLEPHOM, (int)req.LOFMJHJOELO))
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueEnterCellScRsp,
                new ChessRogueEnterCellScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueEnterCellScRsp, new ChessRogueEnterCellScRsp
        {
            StageInfo = ChessRogueProtoBuilder.BuildStage(player),
            Info = manager.BuildGameInfo()
        }));
    }
}

[Opcode(CmdIds.RogueModifierSelectCellCsReq)]
public sealed class HandlerRogueModifierSelectCellCsReq : Handler<RogueModifierSelectCellCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player,
        RogueModifierSelectCellCsReq req)
    {
        var ret = await player.ChessRogueManager!.ApplyModifier((int)req.IHAOMLEPHOM);
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueModifierSelectCellScRsp,
            new RogueModifierSelectCellScRsp { Retcode = (uint)ret }));
    }
}

[Opcode(CmdIds.ChessRogueEnterNextLayerCsReq)]
public sealed class HandlerChessRogueEnterNextLayerCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.ChessRogueManager!;
        var ret = await manager.EnterNextLayer();
        var run = manager.RunInstance;
        if (ret != Retcode.RetSucc || run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueEnterNextLayerScRsp,
                new ChessRogueEnterNextLayerScRsp { Retcode = (uint)ret }));
            return;
        }

        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueEnterNextLayerScRsp,
            new ChessRogueEnterNextLayerScRsp
            {
                StageInfo = ChessRogueProtoBuilder.BuildStage(player),
                RogueGameInfo = manager.BuildGameInfo(),
                HPHKDGJHIJL = ChessRogueProtoBuilder.BuildBoardSnapshot(run)
            }));
    }
}

[Opcode(CmdIds.ChessRogueLeaveCsReq)]
public sealed class HandlerChessRogueLeaveCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.ChessRogueManager!;
        if (await manager.EndRun() == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueLeaveScRsp,
                new ChessRogueLeaveScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueLeaveScRsp, new ChessRogueLeaveScRsp
        {
            StageInfo = ChessRogueProtoBuilder.BuildStage(player),
            IHNENEGKEPO = ChessRogueProtoBuilder.BuildQueryInfo(manager),
            LFLCDNGALME = ChessRogueProtoBuilder.BuildGetInfo(manager),
            AEFMMOHKCHJ = ChessRogueProtoBuilder.BuildAeonInfo(null)
        }));
    }
}

[Opcode(CmdIds.ChessRogueQuitCsReq)]
public sealed class HandlerChessRogueQuitCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.ChessRogueManager!;
        var levelInfo = manager.RunInstance is { } live ? manager.BuildLevelInfo(live) : null;
        var run = await manager.EndRun();
        if (run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueQuitScRsp,
                new ChessRogueQuitScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        await connection.SendPacket(BasePacket.Create(CmdIds.ChessRogueQuitScRsp, new ChessRogueQuitScRsp
        {
            MINFCKLKLNJ = ChessRogueProtoBuilder.BuildFinishInfo(run, player),
            OEIBHLMDJKA = levelInfo,
            StageInfo = ChessRogueProtoBuilder.BuildStage(player),
            IHNENEGKEPO = ChessRogueProtoBuilder.BuildQueryInfo(manager),
            LFLCDNGALME = ChessRogueProtoBuilder.BuildGetInfo(manager),
            AEFMMOHKCHJ = ChessRogueProtoBuilder.BuildAeonInfo(null)
        }));
    }
}

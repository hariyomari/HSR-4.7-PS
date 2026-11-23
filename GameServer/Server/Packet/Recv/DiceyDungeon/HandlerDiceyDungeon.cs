using March7thHoney.GameServer.Game.Activity.DiceyDungeon;
using March7thHoney.GameServer.Server.Packet.Send.DiceyDungeon;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server.Packet.Recv.DiceyDungeon;

// Containment Starts With a Roll requests, sent by the client's DiceyDungeon network service (LCBFIHEMOOC in the
// 4.7.51 dump): GetActivityDataAsync, EnterNodeAsync, FinishNodeAsync, _SendSelectRewardAsync and RerollRewardAsync.

/// <summary>GetData (DLDDOMMLPBK, 10646, empty).</summary>
[Opcode(CmdIds.DLDDOMMLPBK)]
public sealed class HandlerDiceyDungeonGetDataCsReq : Handler<DLDDOMMLPBK>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, DLDDOMMLPBK req)
    {
        await connection.SendPacket(new PacketDiceyDungeonGetDataScRsp(player.GetComponent<DiceyDungeonManager>()));
    }
}

/// <summary>
///     EnterNode (NOJMGKOMJHP, 10658): IJAPHCOHMIN level, CDMFHNGEFCN node, ILHPFNCIOJN avatar, JHLGHLBDMLH skills,
///     KIIIDKADAIN miracles.
/// </summary>
[Opcode(CmdIds.NOJMGKOMJHP)]
public sealed class HandlerDiceyDungeonEnterNodeCsReq : Handler<NOJMGKOMJHP>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, NOJMGKOMJHP req)
    {
        var manager = player.GetComponent<DiceyDungeonManager>();
        var retcode = manager.EnterNode((int)req.IJAPHCOHMIN, (int)req.CDMFHNGEFCN, (int)req.ILHPFNCIOJN,
            req.JHLGHLBDMLH.Select(x => (int)x).ToList(), req.KIIIDKADAIN.Select(x => (int)x).ToList());
        await connection.SendPacket(
            new PacketDiceyDungeonEnterNodeScRsp(retcode, req.IJAPHCOHMIN, req.CDMFHNGEFCN, manager));
    }
}

/// <summary>
///     FinishNode (PIEGCFMAEOO, 10654): IJAPHCOHMIN level, CDMFHNGEFCN node, EKFDEEHCKDH win, GNGBGCHGJPD player HP
///     left, BOILBFJANKL enemy HP left, BJKGNCLCKBG rounds, LALKFADHJJL time.
/// </summary>
[Opcode(CmdIds.PIEGCFMAEOO)]
public sealed class HandlerDiceyDungeonFinishNodeCsReq : Handler<PIEGCFMAEOO>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, PIEGCFMAEOO req)
    {
        var manager = player.GetComponent<DiceyDungeonManager>();
        var result = await manager.FinishNode((int)req.IJAPHCOHMIN, (int)req.CDMFHNGEFCN, req.EKFDEEHCKDH,
            (int)req.GNGBGCHGJPD, (int)req.BOILBFJANKL);
        await connection.SendPacket(new PacketDiceyDungeonFinishNodeScRsp(result, req, manager));
    }
}

/// <summary>SelectReward (KPNDLNCAECC, 10657): skill (9), miracle (4) or give up (11; 8 is treated alike).</summary>
[Opcode(CmdIds.KPNDLNCAECC)]
public sealed class HandlerDiceyDungeonSelectRewardCsReq : Handler<KPNDLNCAECC>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, KPNDLNCAECC req)
    {
        var manager = player.GetComponent<DiceyDungeonManager>();
        await connection.SendPacket(new PacketDiceyDungeonSelectRewardScRsp(manager.SelectReward(req), manager));
    }
}

/// <summary>RerollReward (IMPHFFIJDKP, 10645): ALPKKNKAFBG (13) rerolls the skill choice, ANBKDJKHCBI (2) the miracle one.</summary>
[Opcode(CmdIds.IMPHFFIJDKP)]
public sealed class HandlerDiceyDungeonRerollRewardCsReq : Handler<IMPHFFIJDKP>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, IMPHFFIJDKP req)
    {
        var manager = player.GetComponent<DiceyDungeonManager>();
        var (retcode, isMiracle) = manager.RerollReward(req);
        await connection.SendPacket(new PacketDiceyDungeonRerollRewardScRsp(retcode, isMiracle, manager));
    }
}

/// <summary>
///     MDOHKDNNDPI (10648, empty) answered by LHDOHPHPABA (retcode only). No C# code in the client builds it, so it
///     comes from the Lua side and its purpose is not known; it is acknowledged without touching the run.
/// </summary>
[Opcode(CmdIds.MDOHKDNNDPI)]
public sealed class HandlerDiceyDungeonUnknownCsReq : Handler<MDOHKDNNDPI>
{
    private static readonly Logger Logger = new("DiceyDungeon");

    protected override async Task OnHandle(Connection connection, PlayerInstance player, MDOHKDNNDPI req)
    {
        Logger.Debug($"uid {player.Uid} sent MDOHKDNNDPI (10648); acknowledged without a state change.");
        await connection.SendPacket(new PacketDiceyDungeonRetcodeScRsp(Retcode.RetSucc));
    }
}

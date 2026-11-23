using March7thHoney.GameServer.Game.Activity.ActivityBangboo;
using March7thHoney.GameServer.Server.Packet.Send.ActivityBangboo;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.ActivityBangboo;

// Planarcadia Reverie requests (4.8 command group MFNJLCNMBON, 10602-10640, left unnamed by the dump). They are built
// by the client's NFFAPNKNMBF; the names below are the client methods that send them (async state machines and
// lambdas keep the real names): RefreshAsync, ReadAsync, SetLikeAsync, _StartPreparedGameAsync,
// _FinishPerformanceAsync, _SettleAsync / SaveAsync / _ExitAsync / AbandonInfinityRunAsync, and
// _SettlePreparationAsync / _EnsureEndlessRunClearedAsync.

/// <summary>GetData (FDFNDGOOCGJ, 10604, empty), sent by CBAPNPOMOII.RefreshAsync.</summary>
[Opcode(CmdIds.FDFNDGOOCGJ)]
public sealed class HandlerActivityBangbooGetDataCsReq : Handler<FDFNDGOOCGJ>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, FDFNDGOOCGJ req)
    {
        await connection.SendPacket(new PacketActivityBangbooGetDataScRsp(player.GetComponent<ActivityBangbooManager>()));
    }
}

/// <summary>ReadPost (MGJEKPCMNDD, 10634): CBGODLHCLPK post.</summary>
[Opcode(CmdIds.MGJEKPCMNDD)]
public sealed class HandlerActivityBangbooReadPostCsReq : Handler<MGJEKPCMNDD>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, MGJEKPCMNDD req)
    {
        var (retcode, post) = await player.GetComponent<ActivityBangbooManager>().ReadPost((int)req.CBGODLHCLPK);
        await connection.SendPacket(new PacketActivityBangbooReadPostScRsp(retcode, post));
    }
}

/// <summary>SetLike (HBHPDMLDLBL, 10616): CBGODLHCLPK post, IAJAOMAKAHA like.</summary>
[Opcode(CmdIds.HBHPDMLDLBL)]
public sealed class HandlerActivityBangbooSetLikeCsReq : Handler<HBHPDMLDLBL>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, HBHPDMLDLBL req)
    {
        var (retcode, post) = player.GetComponent<ActivityBangbooManager>().SetLike((int)req.CBGODLHCLPK, req.IAJAOMAKAHA);
        await connection.SendPacket(new PacketActivityBangbooSetLikeScRsp(retcode, post));
    }
}

/// <summary>StartGame (HKIFIOMLLCE, 10606): IJAPHCOHMIN stage, OEDLOIPDGKG the Bangboo picked.</summary>
[Opcode(CmdIds.HKIFIOMLLCE)]
public sealed class HandlerActivityBangbooStartGameCsReq : Handler<HKIFIOMLLCE>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, HKIFIOMLLCE req)
    {
        var result = player.GetComponent<ActivityBangbooManager>()
            .StartGame((int)req.IJAPHCOHMIN, req.OEDLOIPDGKG.Select(x => (int)x).ToHashSet());
        await connection.SendPacket(new PacketActivityBangbooStartGameScRsp(result));
    }
}

/// <summary>FinishPerformance (BNMEGDLCIKO, 10638): CIGGAEJNEFM anecdote group.</summary>
[Opcode(CmdIds.BNMEGDLCIKO)]
public sealed class HandlerActivityBangbooFinishPerformanceCsReq : Handler<BNMEGDLCIKO>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, BNMEGDLCIKO req)
    {
        var retcode = await player.GetComponent<ActivityBangbooManager>().FinishPerformance((int)req.CIGGAEJNEFM);
        await connection.SendPacket(new PacketActivityBangbooFinishPerformanceScRsp(retcode, req.CIGGAEJNEFM));
    }
}

/// <summary>
///     Settle (MBCCKEIJAGF, 10620): IJAPHCOHMIN stage, OJHNAJCAKNB GameTransNo, PKKMAAIJEOD reason (FDHNDGJCOAC),
///     FFCBOJAACMD statistics (MPAJNPPLKMP), BBEMIGELGJC board archive (Infinity wins only).
/// </summary>
[Opcode(CmdIds.MBCCKEIJAGF)]
public sealed class HandlerActivityBangbooSettleCsReq : Handler<MBCCKEIJAGF>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, MBCCKEIJAGF req)
    {
        var result = await player.GetComponent<ActivityBangbooManager>().Settle((int)req.IJAPHCOHMIN,
            req.OJHNAJCAKNB, (uint)req.PKKMAAIJEOD, req.FFCBOJAACMD, req.BBEMIGELGJC);
        await connection.SendPacket(new PacketActivityBangbooSettleScRsp(result));
        // New stages, Bangboo or the Infinity holes: the notify marks them new and makes the client refresh.
        if (result.HasUnlocks) await connection.SendPacket(new PacketActivityBangbooChangeScNotify(result));
    }
}

/// <summary>SettleInfinityRun (FFPHBNAGJNP, 10635): IDCGICECPHO final statistics of the run.</summary>
[Opcode(CmdIds.FFPHBNAGJNP)]
public sealed class HandlerActivityBangbooSettleInfinityRunCsReq : Handler<FFPHBNAGJNP>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, FFPHBNAGJNP req)
    {
        var retcode = player.GetComponent<ActivityBangbooManager>().SettleInfinityRun();
        await connection.SendPacket(new PacketActivityBangbooSettleInfinityRunScRsp(retcode));
    }
}

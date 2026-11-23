using March7thHoney.GameServer.Game.Activity.DiceyDungeon;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.DiceyDungeon;

// Containment Starts With a Roll replies. The 4.8 dump left the command group (LDJDDDDPLLH, 10643-10659) unnamed;
// docs/development/dicey-dungeon.md maps each message and field to the client code that reads it.

/// <summary>GetData reply (OBJJEHIKFJC, 10659): FJCAKOIDONG = run + level records, JKCOAIGJMMM = retcode.</summary>
public class PacketDiceyDungeonGetDataScRsp : BasePacket
{
    public PacketDiceyDungeonGetDataScRsp(DiceyDungeonManager manager) : base(CmdIds.OBJJEHIKFJC)
    {
        SetData(new OBJJEHIKFJC { FJCAKOIDONG = manager.ToDataProto() });
    }
}

/// <summary>EnterNode reply (OHCCEANPCIL, 10652): level, node, the run, retcode.</summary>
public class PacketDiceyDungeonEnterNodeScRsp : BasePacket
{
    public PacketDiceyDungeonEnterNodeScRsp(Retcode retcode, uint levelId, uint nodeId, DiceyDungeonManager manager)
        : base(CmdIds.OHCCEANPCIL)
    {
        SetData(new OHCCEANPCIL
        {
            JKCOAIGJMMM = (uint)retcode,
            IJAPHCOHMIN = levelId,
            CDMFHNGEFCN = nodeId,
            NBNKLEBNPFA = retcode == Retcode.RetSucc ? manager.ToRunProto() : null
        });
    }
}

/// <summary>
///     FinishNode reply (NMNFJDBNBPG, 10655). NIMINCMIMEE = the level is settled (the client then shows the result
///     and ignores the run), CCBNBBGBKCE = the level's farthest node (0 leaves the client's value alone).
/// </summary>
public class PacketDiceyDungeonFinishNodeScRsp : BasePacket
{
    public PacketDiceyDungeonFinishNodeScRsp(DiceyDungeonManager.FinishResult result, PIEGCFMAEOO req,
        DiceyDungeonManager manager) : base(CmdIds.NMNFJDBNBPG)
    {
        SetData(new NMNFJDBNBPG
        {
            JKCOAIGJMMM = (uint)result.Retcode,
            IJAPHCOHMIN = req.IJAPHCOHMIN,
            CDMFHNGEFCN = req.CDMFHNGEFCN,
            EKFDEEHCKDH = req.EKFDEEHCKDH,
            NIMINCMIMEE = result.LevelFinished,
            BDCOFIOCJFO = (uint)result.Star,
            CCBNBBGBKCE = (uint)result.FarthestNodeId,
            NBNKLEBNPFA = result.Retcode == Retcode.RetSucc && !result.LevelFinished ? manager.ToRunProto() : null
        });
    }
}

/// <summary>SelectReward reply (GOPAMMODIJI, 10656): retcode and the run after the choice.</summary>
public class PacketDiceyDungeonSelectRewardScRsp : BasePacket
{
    public PacketDiceyDungeonSelectRewardScRsp(Retcode retcode, DiceyDungeonManager manager) : base(CmdIds.GOPAMMODIJI)
    {
        SetData(new GOPAMMODIJI { JKCOAIGJMMM = (uint)retcode, NBNKLEBNPFA = manager.ToRunProto() });
    }
}

/// <summary>RerollReward reply (HJLMNPJFEMI, 10651): the redrawn skill (6) or miracle (14) choice.</summary>
public class PacketDiceyDungeonRerollRewardScRsp : BasePacket
{
    public PacketDiceyDungeonRerollRewardScRsp(Retcode retcode, bool isMiracle, DiceyDungeonManager manager)
        : base(CmdIds.HJLMNPJFEMI)
    {
        var rsp = new HJLMNPJFEMI { JKCOAIGJMMM = (uint)retcode };
        if (manager.PendingRewardProto(isMiracle) is { } reward)
        {
            if (isMiracle) rsp.CACADJOIHHI = reward;
            else rsp.DPLODKBIJDI = reward;
        }

        SetData(rsp);
    }
}

/// <summary>Reply (LHDOHPHPABA, 10643) to the empty request MDOHKDNNDPI (10648), which carries only a retcode.</summary>
public class PacketDiceyDungeonRetcodeScRsp : BasePacket
{
    public PacketDiceyDungeonRetcodeScRsp(Retcode retcode) : base(CmdIds.LHDOHPHPABA)
    {
        SetData(new LHDOHPHPABA { JKCOAIGJMMM = (uint)retcode });
    }
}

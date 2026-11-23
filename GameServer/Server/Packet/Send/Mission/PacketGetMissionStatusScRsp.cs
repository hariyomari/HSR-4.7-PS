using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Mission;

public class PacketGetMissionStatusScRsp : BasePacket
{
    public PacketGetMissionStatusScRsp(GetMissionStatusCsReq req, PlayerInstance player) : base(
        CmdIds.GetMissionStatusScRsp)
    {
        var proto = new GetMissionStatusScRsp();

        foreach (var item in req.MainMissionIdList)
        {
            // The hub mission of an activity's standing entrance stays open for the client even though it
            // reads as finished here; calling it finished would unload the entrance NPC's group.
            var status = player.MissionManager!.GetMainMissionStatus((int)item);
            if (status == MissionPhaseEnum.Finish && !player.MissionManager.IsOpenStandingEntranceMission((int)item))
            {
                proto.FinishedMainMissionIdList.Add(item);
                proto.CurversionFinishedMainMissionIdList.Add(item);
            }
            else
            {
                proto.UnfinishedMainMissionIdList.Add(item);
            }
        }

        player.MissionManager!.NoteMainMissionsReportedFinished(proto.FinishedMainMissionIdList);

        foreach (var item in req.SubMissionIdList)
        {
            var status = player.MissionManager!.GetSubMissionStatus((int)item);
            proto.SubMissionStatusList.Add(new Proto.Mission
            {
                Id = item,
                Status = player.MissionManager.GetStandingEntranceSubMissionStatus((int)item) ?? status.ToProto()
            });
        }

        SetData(proto);
    }
}

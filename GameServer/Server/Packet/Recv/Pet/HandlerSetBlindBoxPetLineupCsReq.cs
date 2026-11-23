using March7thHoney.GameServer.Game.Pet;
using March7thHoney.GameServer.Server.Packet.Send.Pet;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Pet;

/// <summary>
///     Sets the blind box pet (盲盒宠物) companion lineup. The official server sends
///     PetDataChangeScNotify{blind_box_lineup} first and the ScRsp second; both echo the source enum of the request.
///     An invalid request (an instance that is not owned or is repeated, more than 3 slots) leaves the save untouched
///     and only returns a failure code.
/// </summary>
[Opcode(CmdIds.SetBlindBoxPetLineupCsReq)]
public class HandlerSetBlindBoxPetLineupCsReq : Handler<SetBlindBoxPetLineupCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, SetBlindBoxPetLineupCsReq req)
    {
        var pets = player.GetComponent<PetManager>();
        if (!pets.SetBlindBoxLineup(req.POJGKJENENA))
        {
            await connection.SendPacket(new PacketSetBlindBoxPetLineupScRsp(Retcode.RetReqParaInvalid));
            return;
        }

        await connection.SendPacket(new PacketPetDataChangeScNotify(pets.ToLineupProto(req.CGBJOAJEMKN)));
        await connection.SendPacket(new PacketSetBlindBoxPetLineupScRsp(pets.ToLineupProto(req.CGBJOAJEMKN)));
    }
}

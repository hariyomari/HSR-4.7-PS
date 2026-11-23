using March7thHoney.GameServer.Game.Pet;
using March7thHoney.GameServer.Server.Packet.Send.Pet;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Pet;

/// <summary>
///     4.6 summon: the request is { ref_type enum, oneof { first-generation pet id, blind box pet unique id } }, and
///     the reply puts the same content into cur_pet. In official captures, summoning the blind box squad carries only
///     the enum (3), and cur_pet in the reply holds only the enum as well. The older CurPetChangedScNotify no longer
///     exists in 4.6.
/// </summary>
[Opcode(CmdIds.SummonPetCsReq)]
public class HandlerSummonPetCsReq : Handler<SummonPetCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, SummonPetCsReq req)
    {
        var curPet = player.GetComponent<PetManager>().Summon(req);

        await connection.SendPacket(new PacketSummonPetScRsp(curPet));
        await player.TrainCakeCatchManager!.BroadcastPlayerStateAsync();
    }
}

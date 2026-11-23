using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Gacha;

public class PacketSetGachaDecideItemScRsp : BasePacket
{
    public PacketSetGachaDecideItemScRsp(uint gachaId, uint decideItemType, List<uint> order,
        Retcode retcode = Retcode.RetSucc) : base(CmdIds.SetGachaDecideItemScRsp)
    {
        // 4.3: the obfuscated names in the SetGachaDecideItemScRsp sub-message changed
        //   LECPJJAMNPF -> NBLOJLDLBEB (type OEIEJHBCOOM -> NEIMLKNMDBM)
        //   PAPOKACIPPJ(gachaId) -> MMELNCHIDNC, DGOMHDMJHEK(order) -> OJNEFBJHCCK,
        //   MBOEFLAHLEM(decideItemType) -> GFANBHAEKOK (the same names as in SetGachaDecideItemCsReq)
        var proto = new SetGachaDecideItemScRsp
        {
            Retcode = (uint)retcode,
            NBLOJLDLBEB = new NEIMLKNMDBM
            {
                MMELNCHIDNC = gachaId,
                OJNEFBJHCCK = { order },
                GFANBHAEKOK = decideItemType
            }
        };

        SetData(proto);
    }
}


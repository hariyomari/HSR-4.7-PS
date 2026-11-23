using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Pet;

/// <summary>
///     The blind box pools queried by the inventory's blind box recycling page ("盲盒回收"). The BlindBoxPool table is
///     not in the cache, so the only pool in the 4.6.0 resources, 1001, is hardcoded. The begin time is that of the
///     official first 4.6 event phase (1790499600) and the end time is pushed out to 2100-01-01, so the pool never
///     expires on a private server (yuukisr_go c4f5979). The counter field CANDEKPIMCC appears only once a box has
///     been opened and is absent from captures of a new account, so it is not sent here either.
/// </summary>
public class PacketBlindBoxGetDataScRsp : BasePacket
{
    public const uint PoolId = 1001;
    public const long PoolBeginTime = 1790499600;
    public const long PoolEndTime = 4102444800;

    public PacketBlindBoxGetDataScRsp() : base(CmdIds.BlindBoxGetDataScRsp)
    {
        var proto = new BlindBoxGetDataScRsp();
        proto.CEGOIBGJDBO.Add(new MFCCCIMGMFL
        {
            PoolId = PoolId,
            JPLIJJFCAOO = PoolBeginTime,
            EndTime = PoolEndTime
        });

        SetData(proto);
    }
}

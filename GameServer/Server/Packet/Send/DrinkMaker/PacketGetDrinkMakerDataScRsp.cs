using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.DrinkMaker;

/// <summary>
///     Drink maker state the server does not model yet. DrinkMakerBar._UpdateGameplayData looks the current guest
///     sequence (EMIAIOMOLPO, field 14) up in DrinkMakerGuestSequence and the current chat (FGBELHJAKCC, field 15) in
///     DrinkMakerChat, and throws when either is missing, so an empty body - sequence 0 - is not enough. These values
///     mirror the official 4.4.0 capture (as kept by yuukisr_go); the 4.7.51 tables still have sequence 112 (guest 2,
///     start chat 11201), chat 11201 (request 20004) and day 5.
/// </summary>
public class PacketGetDrinkMakerDataScRsp : BasePacket
{
    public const uint GuestSequenceId = 112;
    public const uint ChatId = 11201;

    public PacketGetDrinkMakerDataScRsp() : base(CmdIds.GetDrinkMakerDataScRsp)
    {
        var rsp = new GetDrinkMakerDataScRsp
        {
            Level = 4,
            Day = 5,
            Exp = 0,
            EMIAIOMOLPO = GuestSequenceId,
            FGBELHJAKCC = ChatId,
            FHPKJONKHAC = 29,
            LMOGDPMLKFH = new DFJPGILAAON
            {
                PDHDOHAIJKD = 2,
                DALCMIELFDD = 52,
                KMADACHJHJC = 9,
                HCCKHLMLENE = 2,
                ECIOKDDBEPN = { 10, 12, 8, 2, 13 }
            }
        };
        rsp.CJKAGEDKAFL.AddRange(Enumerable.Range(1, 12).Select(id => (uint)id));
        foreach (var (ingredients, guest) in new (uint[] Ingredients, uint Guest)[]
                 {
                     ([3, 1, 4], 2), ([3, 2], 1), ([3, 4, 2], 6), ([4, 2, 3], 4), ([1, 2, 3], 5), ([4, 3], 3)
                 })
            rsp.ONFNEDNFOLL.Add(new KKIACMJGIKN { OHPIDOOJDHJ = 2, JOIKIENJNJI = guest, OBJOLNOJIKC = { ingredients } });

        SetData(rsp);
    }
}

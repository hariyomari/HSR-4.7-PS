using March7thHoney.Data.Custom;
using March7thHoney.Enums.RogueMagic;

namespace March7thHoney.Data.Rogue;

/// <summary>Room layouts of the Unknowable Domain (RogueMagic) from Config/RogueMagicRoomGen.json.</summary>
public static class RogueMagicRoomPools
{
    /// <summary>
    ///     The file has no Reward or Encounter rooms: those room types are played on the event room layouts, so each
    ///     event room is appended again under both types.
    /// </summary>
    public static void AddEventRoomVariants(List<RogueMagicRoomConfig> rooms)
    {
        foreach (var room in rooms.Where(room => room.RoomType == RogueMagicRoomTypeEnum.Event).ToList())
        {
            rooms.Add(room.Clone(RogueMagicRoomTypeEnum.Reward));
            rooms.Add(room.Clone(RogueMagicRoomTypeEnum.Encounter));
        }
    }
}

using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Enums.Item;

namespace March7thHoney.GameServer.Game.Player;

/// <summary>
///     Ownership and display of head frames.
///     <para>
///         Ownership: the client's <c>HeadFrameUtils.HasHeadFrame</c> checks whether the inventory holds an item of
///         subtype HeadIconFrame, so granting a head frame must also put the item in the inventory (one per kind).
///     </para>
///     <para>
///         Display: the player setting <see cref="Database.Player.PrivacySettingsPb.HideHeadFrame" />; the client's
///         <c>PlayerSettingData.IsDisplayHeadFrame</c> is its negation. Hiding only affects the profile other players
///         see (<see cref="Database.Player.PlayerData.ToPublicHeadFrameProto" />); it neither takes back nor replaces
///         the equipped head frame.
///     </para>
/// </summary>
public static class HeadFrameService
{
    public static bool IsHeadFrameItem(uint itemId)
    {
        return GameData.ItemConfigData.GetValueOrDefault((int)itemId)?.ItemSubType == ItemSubTypeEnum.HeadIconFrame;
    }

    /// <summary>
    ///     Grants and equips a head frame: the item goes into the inventory (not duplicated when already owned), then
    ///     becomes the current head frame. The player's display setting is left unchanged.
    /// </summary>
    public static async ValueTask Grant(PlayerInstance player, uint frameId, long expireTime)
    {
        await EnsureOwned(player, frameId, true);
        await player.SetPlayerHeadFrameId(frameId, expireTime);
    }

    /// <summary>
    ///     Login repair: a save can hold head frame info without the matching item, and the client only shows the
    ///     display toggle once the current head frame is in the inventory, so the item is added here. The client
    ///     fetches the inventory again after login, so no separate sync is sent.
    /// </summary>
    public static async ValueTask RepairOwnedItem(PlayerInstance player)
    {
        await EnsureOwned(player, player.Data.HeadFrame.HeadFrameId, false);
    }

    /// <summary>UpdatePlayerSetting oneof field 1600: true hides the head frame, false shows it.</summary>
    public static void SetHidden(PlayerInstance player, bool hidden)
    {
        var settings = player.Data.PrivacySettings;
        if (settings.HideHeadFrame == hidden) return;
        settings.HideHeadFrame = hidden;
        DatabaseHelper.MarkDirty(player.Uid);
    }

    private static async ValueTask EnsureOwned(PlayerInstance player, uint frameId, bool sync)
    {
        if (!IsHeadFrameItem(frameId) || player.InventoryManager!.GetItem((int)frameId) != null) return;
        await player.InventoryManager.AddItem((int)frameId, 1, false, sync: sync);
    }
}

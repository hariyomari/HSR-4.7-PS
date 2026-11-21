namespace March7thHoney.Enums;

/// <summary>
///     Source of the limited warp banners (ServerOption.Warps.Mode). Permanent pools (Newbie / Normal) always come
///     from Config/Custom/Banners.json.
/// </summary>
public enum WarpScheduleModeEnum
{
    // Banners.json as written, including its own time windows.
    Static = 0,

    // One phase of the history pool per rotation period, in order.
    Rotation = 1,

    // The admin-picked list in ServerOption.Warps.Manual.
    Manual = 2
}

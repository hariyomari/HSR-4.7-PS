using March7thHoney.Database.Avatar;
using March7thHoney.Internationalization;
using March7thHoney.Util;

namespace March7thHoney.Database.Migrations;

/// <summary>
///     Converts avatar saves older than <see cref="GameConstants.AvatarDbVersion" /> (one <c>Avatars</c> entry per
///     character, with per-path info) into <c>FormalAvatars</c> with one <see cref="PathInfo" /> per path. Runs during
///     startup, after every save is in memory and before the server accepts players, so no session sees a half-migrated
///     save.
/// </summary>
public static class FormalAvatarMigration
{
    private static readonly Logger Logger = new("Database");

    public static void Run(DatabaseHelper database)
    {
        var updated = false;
        foreach (var avatarData in DatabaseHelper.GetAllInstanceFromMap<AvatarData>() ?? [])
        {
            if (avatarData.DatabaseVersion == GameConstants.AvatarDbVersion) continue;

            foreach (var avatar in avatarData.Avatars)
            {
                var formalAvatar = new FormalAvatarInfo
                {
                    BaseAvatarId = avatar.AvatarId,
                    AvatarId = avatar.PathId == 0 ? avatar.AvatarId : avatar.PathId,
                    CurrentHp = avatar.CurrentHp,
                    CurrentSp = avatar.CurrentSp,
                    Exp = avatar.Exp,
                    ExtraLineupHp = avatar.ExtraLineupHp,
                    ExtraLineupSp = avatar.ExtraLineupSp,
                    IsMarked = avatar.IsMarked,
                    Level = avatar.Level,
                    Promotion = avatar.Promotion,
                    PathInfos = []
                };

                foreach (var info in avatar.PathInfoes)
                {
                    if (info.Value.PathId == 0)
                        info.Value.PathId = avatar.AvatarId;
                    formalAvatar.PathInfos.Add(info.Value.PathId, new PathInfo(info.Value.PathId)
                    {
                        PathId = info.Value.PathId,
                        EquipId = info.Value.EquipId,
                        Rank = info.Value.Rank,
                        Relic = info.Value.Relic,
                        Skin = info.Value.Skin,
                        EnhanceInfos =
                        {
                            {
                                0, new EnhanceInfo(0)
                                {
                                    SkillTree = avatar.SkillTreeExtra.GetValueOrDefault(info.Value.PathId) ?? []
                                }
                            }
                        }
                    });
                }

                avatarData.FormalAvatars.Add(formalAvatar);
            }

            avatarData.DatabaseVersion = GameConstants.AvatarDbVersion;
            updated = true;
            DatabaseHelper.MarkDirty(avatarData.Uid);
        }

        if (!updated) return;

        Logger.Info(I18NManager.Translate("Server.ServerInfo.UpdatedItem", I18NManager.Translate("Word.Database")));
        database.SaveDatabase();
    }
}

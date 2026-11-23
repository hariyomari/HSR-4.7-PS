using March7thHoney.Data;
using March7thHoney.Internationalization;

namespace March7thHoney.Command.Command.Cmd;

[CommandInfo("reload", "Game.Command.Reload.Desc", "Game.Command.Reload.Usage", permission: CommandPermissions.Reload)]
public class CommandReload : ICommand
{
    [CommandMethod("0 banner")]
    public async ValueTask ReloadBanner(CommandArg arg)
    {
        CustomConfigFiles.Reload(ReloadableCustomConfig.Banners);
        await arg.SendMsg(I18NManager.Translate("Game.Command.Reload.ConfigReloaded",
            I18NManager.Translate("Word.Banner")));
    }

    [CommandMethod("0 activity")]
    public async ValueTask ReloadActivity(CommandArg arg)
    {
        CustomConfigFiles.Reload(ReloadableCustomConfig.Activity);
        await arg.SendMsg(I18NManager.Translate("Game.Command.Reload.ConfigReloaded",
            I18NManager.Translate("Word.Activity")));
    }

    [CommandMethod("0 videokey")]
    public async ValueTask ReloadVideoKey(CommandArg arg)
    {
        CustomConfigFiles.Reload(ReloadableCustomConfig.VideoKeys);
        await arg.SendMsg(I18NManager.Translate("Game.Command.Reload.ConfigReloaded",
            I18NManager.Translate("Word.VideoKeys")));
    }
}

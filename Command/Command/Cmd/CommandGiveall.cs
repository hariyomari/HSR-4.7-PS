using March7thHoney.Data;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Server.Packet.Send.Avatar;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Internationalization;
using March7thHoney.Proto;

namespace March7thHoney.Command.Command.Cmd;

[CommandInfo("giveall", "Game.Command.GiveAll.Desc", "Game.Command.GiveAll.Usage", ["ga"], permission: CommandPermissions.GiveAll)]
public class CommandGiveall : ICommand
{
    [CommandMethod("0 avatar")]
    public async ValueTask GiveAllAvatar(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        // Only values the caller typed are applied to avatars the player already owns; a bare
        // "/giveall avatar" must not reset built characters to level 1 / E0.
        int? level = null;
        int? rank = null;
        if (arg.CharacterArgs.TryGetValue("l", out var levelStr))
        {
            if (!int.TryParse(levelStr, out var parsedLevel))
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
                return;
            }

            level = Math.Clamp(parsedLevel, 1, 80);
        }

        if (arg.CharacterArgs.TryGetValue("r", out var rankStr))
        {
            if (!int.TryParse(rankStr, out var parsedRank))
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
                return;
            }

            rank = Math.Clamp(parsedRank, 0, 6);
        }

        // GiveAllPath below may create multi-path base avatars; they still count as new here.
        var ownedBefore = player.AvatarManager!.Data.FormalAvatars.Select(x => x.BaseAvatarId).ToHashSet();

        await GiveAllPath(arg);

        var avatarList = GameData.AvatarConfigData.Values;
        foreach (var avatar in avatarList)
        {
            if (avatar.AvatarID > 2000 && avatar.AvatarID != 8001)
                continue;

            if (GameData.MultiplePathAvatarConfigData.TryGetValue(avatar.AvatarID, out var multiPathAvatar)
                && avatar.AvatarID != multiPathAvatar.BaseAvatarID)
                continue;

            try
            {
                if (player.AvatarManager!.GetFormalAvatar(avatar.AvatarID) == null)
                    await player.InventoryManager!.AddItem(avatar.AvatarID, 1, false, sync: false);

                var formalAvatar = player.AvatarManager!.GetFormalAvatar(avatar.AvatarID);
                if (formalAvatar == null) continue;

                var isNew = !ownedBefore.Contains(formalAvatar.BaseAvatarId);
                var targetLevel = isNew ? level ?? 1 : level;
                var targetRank = isNew ? rank ?? 0 : rank;

                if (targetLevel is { } newLevel)
                {
                    formalAvatar.Level = newLevel;
                    formalAvatar.Promotion = GameData.GetMinPromotionForLevel(newLevel);
                }

                if (targetRank is { } newRank)
                {
                    // Multi-path characters keep Eidolons per path; set every owned path like /avatar rank.
                    formalAvatar.GetCurPathInfo();
                    foreach (var path in formalAvatar.PathInfos.Values) path.Rank = newRank;
                }
            }
            catch (Exception)
            {
            }
        }

        await player.SendPacket(new PacketPlayerSyncScNotify(player.AvatarManager!.Data.FormalAvatars));

        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Avatar"), "1"));
    }

    [CommandMethod("0 equipment")]
    public async ValueTask GiveAllLightcone(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        arg.CharacterArgs.TryGetValue("r", out var rankStr);
        arg.CharacterArgs.TryGetValue("l", out var levelStr);
        arg.CharacterArgs.TryGetValue("x", out var amountStr);
        rankStr ??= "1";
        levelStr ??= "1";
        amountStr ??= "1";
        if (!int.TryParse(rankStr, out var rank) || !int.TryParse(levelStr, out var level) ||
            !int.TryParse(amountStr, out var amount) || amount < 1)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        var lightconeList = GameData.EquipmentConfigData.Values.Where(x => x.Release);
        var items = new List<ItemData>();

        for (var i = 0; i < amount; i++)
            foreach (var lightcone in lightconeList)
            {
                var item = await player.InventoryManager!.AddItem(lightcone.EquipmentID, 1, false,
                    Math.Max(Math.Min(rank, 5), 0), Math.Max(Math.Min(level, 80), 0), sync: false);

                if (item != null)
                    items.Add(item);
            }

        await player.SendPacket(new PacketPlayerSyncScNotify(items));

        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Equipment"), amount.ToString()));
    }

    [CommandMethod("0 lightcone")]
    public async ValueTask GiveAllLightconeAlias(CommandArg arg)
    {
        await GiveAllLightcone(arg);
    }

    [CommandMethod("0 material")]
    public async ValueTask GiveAllMaterial(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        arg.CharacterArgs.TryGetValue("x", out var amountStr);
        amountStr ??= "1";
        if (!int.TryParse(amountStr, out var amount) || amount < 1)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        var materialList = GameData.ItemConfigData.Values;
        var items = new List<ItemData>();
        foreach (var material in materialList)
            if (material.ItemMainType == ItemMainTypeEnum.Material ||
                material.ItemSubType == ItemSubTypeEnum.Food ||
                material.ItemSubType == ItemSubTypeEnum.FindChest)
                items.Add(new ItemData
                {
                    ItemId = material.ID,
                    Count = amount
                });

        await player.InventoryManager!.AddItems(items, false);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Material"), amount.ToString()));
    }

    [CommandMethod("0 pet")]
    public async ValueTask GiveAllPet(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        arg.CharacterArgs.TryGetValue("x", out var amountStr);
        amountStr ??= "1";
        if (!int.TryParse(amountStr, out var amount) || amount < 1)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        var petList = GameData.ItemConfigData.Values;
        var items = new List<ItemData>();
        foreach (var pet in petList)
            if (pet.ItemMainType == ItemMainTypeEnum.Pet)
                items.Add(new ItemData
                {
                    ItemId = pet.ID,
                    Count = amount
                });
        await player.InventoryManager!.AddItems(items);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Pet"), "1"));
    }

    [CommandMethod("0 relic")]
    public async ValueTask GiveAllRelic(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        arg.CharacterArgs.TryGetValue("l", out var levelStr);
        levelStr ??= "1";
        if (!int.TryParse(levelStr, out var level))
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        arg.CharacterArgs.TryGetValue("x", out var amountStr);
        amountStr ??= "1";
        if (!int.TryParse(amountStr, out var amount) || amount < 1)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        var relicList = GameData.RelicConfigData.Values;
        var items = new List<ItemData>();

        for (var i = 0; i < amount; i++)
            foreach (var relic in relicList)
            {
                var item = await player.InventoryManager!.AddItem(relic.ID, 1, false, 1,
                    Math.Max(Math.Min(level, relic.MaxLevel), 1), sync: false);

                if (item != null)
                    items.Add(item);
            }

        await player.SendPacket(new PacketPlayerSyncScNotify(items));

        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Relic"), amount.ToString()));
    }

    [CommandMethod("0 unlock")]
    public async ValueTask GiveAllUnlock(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        var materialList = GameData.ItemConfigData.Values;
        foreach (var material in materialList)
            if (material.ItemMainType == ItemMainTypeEnum.Usable)
                if (material.ItemSubType == ItemSubTypeEnum.HeadIcon ||
                    material.ItemSubType == ItemSubTypeEnum.PhoneTheme ||
                    material.ItemSubType == ItemSubTypeEnum.ChatBubble ||
                    material.ItemSubType == ItemSubTypeEnum.PersonalCard ||
                    material.ItemSubType == ItemSubTypeEnum.PhoneCase)
                    await player.InventoryManager!.AddItem(material.ID, 1, false);

        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Unlock"), "1"));
    }

    [CommandMethod("0 skin")]
    public async ValueTask GiveAllSkin(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        var materialList = GameData.ItemConfigData.Values;
        foreach (var material in materialList)
            if (material.ItemMainType == ItemMainTypeEnum.Usable)
                if (material.ItemSubType == ItemSubTypeEnum.AvatarSkin ||
                    material.ItemSubType == ItemSubTypeEnum.PlayerOutfit)
                    await player.InventoryManager!.AddItem(material.ID, 1, false);

        await player.SendPacket(new PacketGetAvatarDataScRsp(player));

        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Skin"), "1"));
    }

    [CommandMethod("0 materials")]
    public async ValueTask GiveAllMaterials(CommandArg arg)
    {
        await GiveAllMaterial(arg);
    }

    [CommandMethod("0 train")]
    public async ValueTask GiveAllTrainItem(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        // Reset
        player.TrainPartyManager!.Data.Fund = 1000000;
        player.TrainPartyManager!.Data.Areas.Clear();
        player.TrainPartyManager!.CompleteAllAreas();

        foreach (var floorInfo in GameData.FloorInfoData.Values)
        {
            var savedData = new Dictionary<string, int>();

            foreach (var floorSavedValue in floorInfo.FloorSavedValue)
                if (floorSavedValue.Name.StartsWith("Build_") || floorSavedValue.Name == "Onboarded")
                    savedData[floorSavedValue.Name] = 1;
                else if (floorSavedValue.Name.StartsWith("Progress_"))
                    savedData[floorSavedValue.Name] = 100;
                else if (floorSavedValue.Name.StartsWith("Connection_") || floorSavedValue.Name.StartsWith("Utility_"))
                    savedData[floorSavedValue.Name] = 0;

            if (savedData.Count > 0)
                player.SceneData!.FloorSavedData[floorInfo.FloorID] = savedData;
        }

        player.TrainPartyManager!.MarkDataDirty();

        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.TrainParty"), "1"));
    }

    [CommandMethod("0 path")]
    public async ValueTask GiveAllPath(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        foreach (var multiPathAvatar in GameData.MultiplePathAvatarConfigData.Values)
        {
            if (player.AvatarManager!.GetFormalAvatar(multiPathAvatar.BaseAvatarID) == null)
            {
                await player.InventoryManager!.AddItem(multiPathAvatar.BaseAvatarID, 1, false, sync: false);
                player.AvatarManager!.GetFormalAvatar(multiPathAvatar.BaseAvatarID)!.Level =
                    Math.Max(Math.Min(1, 80), 0);
                player.AvatarManager!.GetFormalAvatar(multiPathAvatar.BaseAvatarID)!.Promotion =
                    GameData.GetMinPromotionForLevel(Math.Max(Math.Min(1, 80), 0));
                player.AvatarManager!.GetFormalAvatar(multiPathAvatar.BaseAvatarID)!.GetCurPathInfo().Rank =
                    Math.Max(Math.Min(0, 6), 0);
            }

            var avatarData = player.AvatarManager!.GetFormalAvatar(multiPathAvatar.BaseAvatarID)!;
            // Trailblazer paths come in male (odd) / female (even) pairs; unlock the player's own half.
            if (multiPathAvatar.BaseAvatarID == 8001 &&
                multiPathAvatar.AvatarID % 2 != (player.Data.CurrentGender == Gender.Woman ? 0 : 1)) continue;
            // Unlock without switching the current path, so owned characters keep the path they are using.
            avatarData.EnsurePathInfo(multiPathAvatar.AvatarID);
        }

        await player.SendPacket(new PacketPlayerSyncScNotify(player.AvatarManager!.Data.FormalAvatars));

        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Avatar"),
            "1"));
    }
}

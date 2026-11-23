using March7thHoney.Data;
using March7thHoney.GameServer.Game.Player;

// Player state must not be the same object as a shared config list or a static default: writing a player's progress
// through such a reference changed the data every other player reads.
internal static class SharedConfigRegression
{
    public static async Task Run(Action<string, bool> check)
    {
        await MessageOffersStayOutOfTheConfig(check);
        await TrainAreasBelongToOnePlayer(check);
    }

    private static async Task MessageOffersStayOutOfTheConfig(Action<string, bool> check)
    {
        // A section whose opening item offers a follow-up with a different continuation: finishing that follow-up
        // replaces the offered list, which is where a list shared with the config would be rewritten.
        var found = GameData.MessageSectionConfigData.Values.OrderBy(section => section.ID)
            .Select(section => (Section: section, Start: section.StartMessageItemIDList.Count == 0
                ? null
                : GameData.MessageItemConfigData.GetValueOrDefault(section.StartMessageItemIDList[0])))
            .FirstOrDefault(pair => pair.Start is { NextItemIDList.Count: > 0 } start &&
                                    start.SectionID == pair.Section.ID &&
                                    GameData.MessageItemConfigData.TryGetValue(start.NextItemIDList[0], out var next) &&
                                    next.SectionID == pair.Section.ID &&
                                    !next.NextItemIDList.SequenceEqual(start.NextItemIDList));
        check("the resource cache has a message section whose opening item offers a follow-up", found.Start != null);
        if (found.Start == null) return;

        var sectionId = found.Section.ID;
        var start = found.Start;
        var offered = start.NextItemIDList.ToArray();

        var first = new PlayerInstance(60023);
        await first.InitializeAsync();
        await first.MessageManager!.AddMessageSection(sectionId);
        var section = first.MessageManager.GetMessageSectionData(sectionId);
        check("a started message section offers the opening item's follow-ups in a list of its own",
            section != null && section.ToChooseItemId.SequenceEqual(offered) &&
            !ReferenceEquals(section.ToChooseItemId, start.NextItemIDList));

        await first.MessageManager.FinishMessageItem(offered[0], false);
        check("finishing an offered message item advances the player's section",
            section != null && section.Items.Any(item => item.ItemId == offered[0]));
        check("finishing an offered message item leaves the shared message config unchanged",
            start.NextItemIDList.SequenceEqual(offered));

        var second = new PlayerInstance(60024);
        await second.InitializeAsync();
        await second.MessageManager!.AddMessageSection(sectionId);
        check("another player starting the same message section gets the original follow-ups",
            second.MessageManager.GetMessageSectionData(sectionId)?.ToChooseItemId.SequenceEqual(offered) == true);
    }

    private static async Task TrainAreasBelongToOnePlayer(Action<string, bool> check)
    {
        const int marker = 990001;
        var first = new PlayerInstance(60025);
        await first.InitializeAsync();
        var second = new PlayerInstance(60026);
        await second.InitializeAsync();
        first.TrainPartyManager!.EnsureDefaults();
        second.TrainPartyManager!.EnsureDefaults();

        var firstAreas = first.TrainPartyManager.Data.Areas;
        var secondAreas = second.TrainPartyManager.Data.Areas;
        check("two new players get the same default train areas as separate objects",
            firstAreas.Count > 0 && firstAreas.Keys.Order().SequenceEqual(secondAreas.Keys.Order()) &&
            firstAreas.All(pair => !ReferenceEquals(pair.Value, secondAreas[pair.Key]) &&
                                   !ReferenceEquals(pair.Value.StepList, secondAreas[pair.Key].StepList) &&
                                   !ReferenceEquals(pair.Value.DynamicInfo, secondAreas[pair.Key].DynamicInfo)));
        if (firstAreas.Count == 0) return;

        var areaId = firstAreas.Keys.Min();
        var slotId = areaId * 100000 + 9901;
        firstAreas[areaId].DynamicInfo[slotId] = marker;
        firstAreas[areaId].StepList.Add(marker);
        check("one player's train DIY slot and build step do not appear in another player's area",
            secondAreas.TryGetValue(areaId, out var other) && !other.DynamicInfo.ContainsKey(slotId) &&
            !other.StepList.Contains(marker));

        var third = new PlayerInstance(60027);
        await third.InitializeAsync();
        third.TrainPartyManager!.EnsureDefaults();
        check("a player created afterwards still gets the unmodified default train area",
            third.TrainPartyManager.Data.Areas.TryGetValue(areaId, out var fresh) &&
            !fresh.DynamicInfo.ContainsKey(slotId) && !fresh.StepList.Contains(marker));
    }
}

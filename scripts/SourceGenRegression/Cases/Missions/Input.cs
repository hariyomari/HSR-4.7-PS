using System;
using System.Collections.Generic;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Mission.FinishAction;
using March7thHoney.GameServer.Game.Mission.FinishType;

namespace March7thHoney.Enums.Mission
{
    public enum FinishActionTypeEnum
    {
        None = 0,
        ChangeLineup = 2,
        AddItem = 5,
    }

    public enum MissionFinishTypeEnum
    {
        Unknown = 0,
        Talk = 1,
        Fight = 7,
    }
}

namespace March7thHoney.GameServer.Game.Mission.FinishAction
{
    [AttributeUsage(AttributeTargets.Class)]
    public class MissionFinishActionAttribute(FinishActionTypeEnum finishAction) : Attribute
    {
        public FinishActionTypeEnum FinishAction { get; } = finishAction;
    }

    public abstract class MissionFinishActionHandler;
}

namespace March7thHoney.GameServer.Game.Mission.FinishType
{
    [AttributeUsage(AttributeTargets.Class)]
    public class MissionFinishTypeAttribute(MissionFinishTypeEnum finishType) : Attribute
    {
        public MissionFinishTypeEnum FinishType { get; } = finishType;
    }

    public abstract class MissionFinishTypeHandler;
}

namespace March7thHoney.GameServer.Game.Mission
{
    public static class MissionManager
    {
        public static readonly Dictionary<FinishActionTypeEnum, MissionFinishActionHandler> ActionHandlers = new();
        public static readonly Dictionary<MissionFinishTypeEnum, MissionFinishTypeHandler> FinishTypeHandlers = new();
    }
}

namespace Sample.Missions
{
    // Declared out of value order: the registry lists handlers by enum value.
    [MissionFinishAction(FinishActionTypeEnum.AddItem)]
    public class AddItemHandler : MissionFinishActionHandler;

    [MissionFinishAction(FinishActionTypeEnum.ChangeLineup)]
    public class ChangeLineupHandler : MissionFinishActionHandler;

    [MissionFinishType(MissionFinishTypeEnum.Fight)]
    public class FightHandler : MissionFinishTypeHandler;

    [MissionFinishType(MissionFinishTypeEnum.Talk)]
    public class TalkHandler : MissionFinishTypeHandler;

    // An abstract handler cannot be constructed and is left out.
    [MissionFinishType(MissionFinishTypeEnum.Unknown)]
    public abstract class SharedHandler : MissionFinishTypeHandler;
}

# Mission System

English | [简体中文](mission-system.zh-CN.md)

This document describes the parts of the mission system a contributor touches most: the mission manager and the two kinds of mission handlers. Read it before adding a handler for a mission finish type or finish action.

Source: [`GameServer/Game/Mission/`](../../GameServer/Game/Mission/)

## Contents

- [MissionManager](#missionmanager)
- [FinishTypeHandler](#finishtypehandler)
- [FinishActionHandler](#finishactionhandler)

## MissionManager

- `MissionManager.cs`: manages the player's mission data.

## FinishTypeHandler

Class name style: **MissionHandler<Type>**, in `GameServer/Game/Mission/FinishType/Handler/`.

Example:

```csharp
[MissionFinishType(MissionFinishTypeEnum.<Type>)]
public class MissionHandler<Type> : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        // your code
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        // your code
    }
}
```

## FinishActionHandler

Class name style: **MissionHandler<Type>**, in `GameServer/Game/Mission/FinishAction/Handler/`.

Example:

```csharp
[MissionFinishAction(FinishActionTypeEnum.<Type>)]
public class MissionHandler<Type> : MissionFinishActionHandler
{
    public override async ValueTask OnHandle(List<int> @params, List<string> paramString, PlayerInstance player)
    {
        // your code
    }
}
```

Both kinds of handlers are registered at compile time by `MissionHandlerRegistryGenerator` (see [Source Generators](source-generators.md)); no separate registration is needed.

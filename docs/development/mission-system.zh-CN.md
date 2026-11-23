# 任务系统

[English](mission-system.md) | 简体中文

本文说明贡献者最常接触的任务系统部分：任务管理器和两类任务处理器。为任务完成类型或完成动作新增处理器之前请先阅读。

Source: [`GameServer/Game/Mission/`](../../GameServer/Game/Mission/)

## 目录

- [MissionManager](#missionmanager)
- [FinishTypeHandler](#finishtypehandler)
- [FinishActionHandler](#finishactionhandler)

## MissionManager

- `MissionManager.cs`：管理玩家的任务数据。

## FinishTypeHandler

类名风格：**MissionHandler<Type>**，放在 `GameServer/Game/Mission/FinishType/Handler/`。

示例：

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

类名风格：**MissionHandler<Type>**，放在 `GameServer/Game/Mission/FinishAction/Handler/`。

示例：

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

两类处理器都由 `MissionHandlerRegistryGenerator` 在编译期注册（见[源生成器](source-generators.zh-CN.md)），不需要另外登记。

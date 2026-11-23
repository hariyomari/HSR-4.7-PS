# 玩家组件

[English](player-components.md) | 简体中文

本文说明玩家组件：为什么使用组件、如何创建组件以及如何使用组件。新增玩家功能之前请先阅读。

Source: [`GameServer/Game/Player/Components/`](../../GameServer/Game/Player/Components/)

## 为什么使用组件

组件以模块化的方式组织代码，使职责划分更清晰、维护更容易。

把所有内容都放进 `PlayerInstance` 类会形成难以管理和扩展的单体设计。组件提供了更灵活的架构，添加或修改功能时不会影响整个玩家实例。

## 创建组件

`BasePlayerComponent` 是所有玩家组件的基类。继承它即可创建新组件，其构造函数接收 `PlayerInstance`。

```csharp
public class CustomComponent(PlayerInstance player) : BasePlayerComponent(player)
{
}
```

在 `PlayerInstance` 中注册组件，确保它被正确初始化：组件在 `PlayerInstance.NormalizePostLoad` 中加入 `Components`。

## 使用组件

通过 `PlayerInstance` 类访问组件：

```csharp
var component = player.GetComponent<CustomComponent>();
```

# Player Components

English | [简体中文](player-components.zh-CN.md)

This document describes player components: why they exist, how to create one and how to use it. Read it before adding player functionality.

Source: [`GameServer/Game/Player/Components/`](../../GameServer/Game/Player/Components/)

## Why components

Components organize code in a modular fashion, which gives better separation of concerns and easier maintenance.

Putting everything in the `PlayerInstance` class would lead to a monolithic design that is harder to manage and extend. Components allow a more flexible architecture in which functionality can be added or changed without affecting the whole player instance.

## Creating a component

`BasePlayerComponent` is the base class for all player components. Inherit from it to create a new component; its constructor takes the `PlayerInstance`.

```csharp
public class CustomComponent(PlayerInstance player) : BasePlayerComponent(player)
{
}
```

Register the component in `PlayerInstance` so that it is initialized properly: components are added to `Components` in `PlayerInstance.NormalizePostLoad`.

## Using a component

Components are accessed through the `PlayerInstance` class:

```csharp
var component = player.GetComponent<CustomComponent>();
```

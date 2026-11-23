# 枚举约定

[English](enum-conventions.md) | 简体中文

本文说明 `Common/Enums/` 下枚举的命名方式和所属命名空间。在该目录新增枚举时请遵循。

Source: [`Common/Enums/`](../../Common/Enums/)

## 命名空间

模块子目录中的枚举使用命名空间 `March7thHoney.Enums.<Module>`，`<Module>` 为子目录名（例如 `Common/Enums/Mission/` 使用 `March7thHoney.Enums.Mission`）。直接放在 `Common/Enums/` 下的枚举使用 `March7thHoney.Enums`。

## 命名

该目录中的每个枚举都以所描述的内容命名，并以 `Enum` 结尾。

示例：`MissionFinishTypeEnum`

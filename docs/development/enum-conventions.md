# Enum Conventions

English | [简体中文](enum-conventions.zh-CN.md)

This document describes how enums under `Common/Enums/` are named and which namespace they go in. Follow it when adding an enum there.

Source: [`Common/Enums/`](../../Common/Enums/)

## Namespace

An enum in a module subdirectory uses the namespace `March7thHoney.Enums.<Module>`, where `<Module>` is the subdirectory name (for example `Common/Enums/Mission/` uses `March7thHoney.Enums.Mission`). Enums placed directly in `Common/Enums/` use `March7thHoney.Enums`.

## Naming

Every enum in this directory is named after what it describes followed by `Enum`.

Example: `MissionFinishTypeEnum`

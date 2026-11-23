# 服务器日志

[English](server-logs.md) | 简体中文

本页说明服务器把日志写到哪里，以及 `Config.json` 中哪些开关会改变这一点。

## 日志去向

每次运行都会在 `Path.LogPath`（默认 `Config/Logs`）下写两个文件。启动时会把之前运行的日志打包成 zip。

| 级别 | 控制台 | `yyyy-MM-dd-N.log` | `yyyy-MM-dd-N-debug.log` |
| --- | --- | --- | --- |
| `INFO`、`WARN`、`ERROR`、`FATAL` | 是 | 是 | 否 |
| `DEBUG` | 仅在开启 `ShowDebugInConsole` 时 | 否 | 是 |

信息、警告和错误日志面向服主：它们按 `ServerOption.Language` 选择的语言包翻译，说明发生了什么或需要修什么。调试日志是给开发和问题反馈用的诊断信息，例如单个玩家的流程记录、被拒绝的请求、连接事件以及缺失的单个资源文件。调试日志保持英文，默认只写入调试日志文件，需要时才输出到控制台。

首次启动生成 `Config.json` 时，生成的 MUIP 管理密钥会显示在控制台并写入主日志，同时保存在 `Config.json` 的 `MuipServer.AdminKey` 中。

## LogOption 开关

这些开关位于 `Config.json` 的 `ServerOption.LogOption` 下，也可以在管理后台的设置页修改。

| 选项 | 默认值 | 作用 |
| --- | --- | --- |
| `ShowDebugInConsole` | `false` | 把调试日志也输出到控制台。无论是否开启，调试日志都会写入调试日志文件。 |
| `EnableGamePacketLog` | Release 构建为 `false` | 记录服务器收发的每个游戏数据包。 |
| `LogPacketToConsole` | `true` | 开启 `EnableGamePacketLog` 时，即使没有开启 `ShowDebugInConsole`，也把数据包日志输出到控制台。数据包日志同时写入调试日志文件。 |
| `DisableLogDetailPacket` | `false` | 只记录数据包名称和命令 ID，不记录包体。 |
| `SavePersonalDebugFile` | `false` | 开启 `EnableGamePacketLog` 时，另外把每个玩家的数据包写到日志目录下的 `Debug/<UID>/`。 |
| `LogResourceCacheLoading` | `true` | 当前服务器不读取此项。 |

反馈问题时，请附上该次运行的调试日志，而不只是主日志。

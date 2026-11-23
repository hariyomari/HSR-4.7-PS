# Server Logs

English | [简体中文](server-logs.zh-CN.md)

This page explains where the server writes its log lines and which `Config.json` switches change that.

## Where lines go

Every run writes two files into `Path.LogPath` (`Config/Logs` by default). Earlier runs are packed into a zip at startup.

| Level | Console | `yyyy-MM-dd-N.log` | `yyyy-MM-dd-N-debug.log` |
| --- | --- | --- | --- |
| `INFO`, `WARN`, `ERROR`, `FATAL` | Yes | Yes | No |
| `DEBUG` | Only with `ShowDebugInConsole` | No | Yes |

Info, warning and error lines are meant for the server owner: they are translated through the language pack selected by `ServerOption.Language` and say what happened or what to fix. Debug lines are diagnostics for developers and bug reports, such as per-player traces, rejected requests, connection events and individual resource files that are missing. They stay in English and only reach the debug log unless you ask for them on the console.

When `Config.json` is created on the first start, the generated MUIP admin key is printed on the console and written to the main log. It is also saved in `Config.json` under `MuipServer.AdminKey`.

## LogOption switches

These live under `ServerOption.LogOption` in `Config.json` and can also be changed on the settings page of the admin console.

| Option | Default | Effect |
| --- | --- | --- |
| `ShowDebugInConsole` | `false` | Also prints debug lines on the console. They are written to the debug log either way. |
| `EnableGamePacketLog` | `false` in Release builds | Logs every game packet the server sends and receives. |
| `LogPacketToConsole` | `true` | With `EnableGamePacketLog`, prints the packet lines on the console even when `ShowDebugInConsole` is off. They are also written to the debug log. |
| `DisableLogDetailPacket` | `false` | Logs only the packet name and command ID, without the packet body. |
| `SavePersonalDebugFile` | `false` | With `EnableGamePacketLog`, also writes each player's packets to `Debug/<UID>/` under the log directory. |
| `LogResourceCacheLoading` | `true` | Not read by the current server. |

When reporting a bug, attach the debug log of the run, not only the main log.

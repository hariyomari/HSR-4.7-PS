# 参与贡献 March7thHoney

[English](CONTRIBUTING.md) | 简体中文

本指南说明构建项目需要什么、各部分代码在哪里，以及一项改动在合并前需要满足什么。面向编码助手的说明在 [AGENTS.md](AGENTS.zh-CN.md)，规则相同。

## 环境准备

需要 .NET SDK 10（10.0.100 或更高，`global.json` 固定了最低版本）、Git，以及运行检查脚本用的 Python 3。所有命令都从仓库根目录执行。

```sh
dotnet restore March7thHoney.sln
dotnet build March7thHoney.sln -c Release --no-restore
```

仓库不包含游戏资源。下文"不需要游戏资源"一栏的回归可以直接运行；其余的需要匹配的资源缓存，见[从客户端导出重建资源缓存](docs/operations/resource-cache-from-client-export.zh-CN.md)。

## 代码结构

| 路径 | 内容 |
| --- | --- |
| `Program/` | 启动入口：配置、资源、数据库、网络服务和关闭流程 |
| `GameServer/Game/` | 玩家、场景、战斗、任务以及各玩法的管理器 |
| `GameServer/Server/Packet/` | 请求处理器（`Recv/`）与响应、通知包（`Send/`） |
| `WebServer/` | HTTP 服务、登录、MUIP、后台和玩家控制台页面 |
| `Command/` | 游戏内命令和管理命令 |
| `Common/` | 共享配置、资源模型、数据库实体、枚举、本地化 |
| `SourceGen/` | 注册处理器、命令、任务处理器和数据库存储的源生成器 |
| `KcpSharp/` | KCP 传输和生成的命令号 |
| `Proto/` | 按游戏版本保存的协议定义（活动版本 `Proto/4.7/` 由 `Proto.csproj` 的 `ActiveProtocolVersion` 选定）、旧版本归档及其维护工具 |
| `scripts/` | 回归程序、缓存工具、检查脚本和 Linux 部署脚本 |
| `docs/` | 文档，索引见 [docs/README.zh-CN.md](docs/README.zh-CN.md) |

## 分支

- `main` 是开发分支，保存当前游戏版本的协议。
- 以游戏版本命名的分支（`4.7`、`4.8` 等）是 `main` 切换到下一个版本时留下的冻结快照，不要向这些分支提交。
- 在短期分支上工作，向 `main` 发起 PR。合并后删除该分支。

## 提交与 PR

- 一次提交、一个 PR 只做一件事。无关的清理另开改动。
- 提交标题遵循 Conventional Commits：`type(scope): summary`，例如 `fix(gacha): keep the pity counter when a banner rotates`。常用类型有 `feat`、`fix`、`refactor`、`docs`、`test`、`build`、`ci`、`chore`。
- 正文说明原来哪里有问题或缺了什么、行为上有什么变化、你是怎么验证的。写明没有验证的部分，尤其是有没有用真实客户端试过。
- 不要提交本地运行数据：`Config.json`、数据库、日志、令牌、游戏资源。

## 发起 PR 之前

运行 CI 会运行的检查：

```sh
dotnet build March7thHoney.sln -c Release -warnaserror "-warnnotaserror:NU1901;NU1902;NU1903;NU1904"
python3 scripts/verify-security.py --skip-build
python3 scripts/check-docs.py
```

然后运行与改动范围对应的回归。修复行为的改动应当在对应的回归里增加一项检查，这项检查在没有修复时会失败。

不需要游戏资源（都包含在 `scripts/verify-security.py` 里）：

| 范围 | 命令 |
| --- | --- |
| 账号、鉴权、封禁、令牌 | `dotnet run --project scripts/AuthRegression/AuthRegression.csproj` |
| Web 后台、MUIP、命令隔离 | `dotnet run --project scripts/WebServerRegression/WebServerRegression.csproj` |
| KCP 来源绑定与握手准入 | `dotnet run --project scripts/KcpSecurityRegression/KcpSecurityRegression.csproj` 和 `scripts/KcpAdmissionRegression/KcpAdmissionRegression.csproj` |
| 源生成器 | `dotnet run --project scripts/SourceGenRegression/SourceGenRegression.csproj -c Release` |
| 背包消耗、商店、合成 | `dotnet run --project scripts/InventoryRegression/InventoryRegression.csproj -c Release` |
| 协议构建 | `python3 scripts/test-proto-generation.py` |

需要资源缓存（`Config/Resource.bin`）：

| 范围 | 命令 |
| --- | --- |
| 登录协议、场景、传送 | `dotnet run --project scripts/LoginSceneRegression/LoginSceneRegression.csproj -c Release` |
| 货币战争（GridFight） | `dotnet run --project scripts/GridFightRegression/GridFightRegression.csproj` |
| 每日实训与每日委托 | `dotnet run --project scripts/DailyActiveRegression/DailyActiveRegression.csproj -c Release` |
| 原始资源与枚举兼容 | `dotnet run --project scripts/ResourceRegression/ResourceRegression.csproj -c Release -- <资源目录> <报告目录>` |

每套回归的说明在 [docs/testing/](docs/README.zh-CN.md) 下。被跳过的回归不等于通过，请写明实际运行了哪些。凡是玩家能看到的东西（进入场景、界面、动画、战斗），服务端回归不能代替真实客户端测试。

## 约定

- 遵循所修改文件的 C# 风格：四个空格、文件范围命名空间、PascalCase 的类型和方法名。`.editorconfig` 描述了这些约定，但它不是重排未改动代码的理由。
- 保持 nullable 检查有意义，不要为了消除告警而加 `!` 或关闭检查。
- 注释和 XML 文档用英文书写，说明原因、约束和协议依据，不叙述代码的修改历史。不要留下注释掉的代码。`TODO` 要写清缺什么、为什么缺。游戏内的专有名词可以在英文名后用括号附一次中文原名。
- 新的玩家功能放进管理器或组件，不要堆进 `PlayerInstance`，见[玩家组件](docs/development/player-components.zh-CN.md)。
- 请求处理器使用 `[Opcode(CmdIds.XxxCsReq)]`，命令使用 `[CommandInfo]`，任务处理器使用各自的特性。注册代码在编译期生成，不要再加基于反射的发现机制，见[源生成器](docs/development/source-generators.zh-CN.md)。
- 涉及奖励、购买、结算或账号时，先校验输入和权限再修改状态，并考虑重复请求和并发请求。
- 修改存档、数据库实体或 MemoryPack 模型时，必须保证旧数据仍然可读。
- 服务端以 NativeAOT 发布。Web 服务新增的 JSON 请求或响应类型需要在 `WebServer/WebJsonContext.cs` 里登记。
- 包版本集中在 `Directory.Packages.props`，`PackageReference` 不带 `Version`。
- 协议改动遵循[协议说明](docs/development/protobuf.zh-CN.md)。绝不要从其他游戏版本照抄命令号或字段号。

## 文档

英文是权威版本：`name.md`。每篇文档旁边都有简体中文译本 `name.zh-CN.md`，随同一项改动一起更新。只有项目 README 保留其他语言的版本。每篇文档都列在 [docs/README.md](docs/README.md) 和 [docs/README.zh-CN.md](docs/README.zh-CN.md) 里；`scripts/check-docs.py` 检查链接、译本和索引。

## 报告问题

- 缺陷：提交 issue，写明你运行的提交号、游戏客户端版本和相关的服务器日志。
- 安全漏洞：按[安全策略](SECURITY.zh-CN.md)处理，不要公开提 issue。

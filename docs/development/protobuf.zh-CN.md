# Proto

[English](protobuf.md) | 简体中文

本文说明服务器的协议定义如何保存、构建以及迁移到新版本的客户端。给协议类型或字段改名、重新生成命令号或切换活动协议版本之前请先阅读。

Source: [`Proto/`](../../Proto/)

protobuf 定义按游戏版本保存、按 `Cmd*Type` 分组。当前活动版本为 **4.7**：由 [`Proto/Proto.csproj`](../../Proto/Proto.csproj) 的 `ActiveProtocolVersion` 选定，构建时把 `Proto/4.7/` 的顶层 `*.proto` 自动生成为 `obj/<configuration>/<framework>/protobuf/4.7/` 中的 C#，再编译；生成的 C# 不纳入版本控制。

活动的 4.7 协议定义与正式服 OSPRODWin4.7.0 客户端一致。上游 `main` 已改用单个 `Proto/StarRail.proto` 保存的 4.8 协议；它的目录布局、ProtoOrganizer 命令（`carry`、`cmdids`）和升级步骤都不适用于本分支。

## 目录

```text
Proto/
  <version>/                   每个游戏版本一个目录
    Cmd<Xxx>Type.proto         按命令模块分组的定义
    Common.proto               跨模块共享类型
    Orphans*.proto             尚未归入命令闭包的类型
    Ungrouped.proto            只被未列入任何 Cmd*Type 枚举的命令号引用的类型
    CmdIds.cs                  该版本的 cmdid 快照
    Raw/                       扁平 master StarRail.proto、packetIds.txt 和 translations.txt
    dumper/Raw/                自有 Dumper 原始产物，不与 reference 混放
    dumper/Retail/             正式服静态 dump，仅在本地保存（已被 Git 忽略，不提交）
    reference/                 可选的外部 reference，只在对应版本真实存在时保存
    compat-types.txt           进门阶段暂从上一版本保留的 API 类型
    cmdid-aliases.txt          旧服务端命令名到当前 cmdid 名的兼容别名
  4.5v3/translations.txt       LoginSceneRegression 的同种子输入，在归档之外单独跟踪
  Archives/                    旧版协议无损 ZIP（迁移时按需恢复）
  Build/                       MSBuild 自动生成目标
  obj/.../protobuf/4.7/        活动 C#，构建时生成且 Git 忽略
  Tool/ProtoOrganizer/         版本迁移、分组、cmdid 和 C# 生成工具
```

只有活动版本的顶层 `*.proto` 参与编译。ProtoOrganizer 从 `Raw/` 和文本清单重新生成 4.7 协议；`dumper/` 中的文件保留这些输入的原始依据。`Proto/4.7/` 中的文件见下文“4.7 活动版本”一节。

旧版本使用其他布局，已经归档。4.4 是迁移到上述布局之前的兼容基线：grouped proto 和 `Raw/` 在版本根目录，自有 Dumper 原始产物在 `4.4/dumper/Raw/`；4.5 和 4.5v3 的分组结果放在 `dumper/` 下。4.5 没有创建虚假的 reference；在 4.5 reference 实际可用前，只使用 Dumper 结果、同混淆种子的 4.4 对齐信息和显式兼容清单。

资源提取与 Honey 资源缓存是两个边界：Mar7thDumper 仍执行包括原始资源导出在内的完整流程，ProtoOrganizer 只消费 proto/cmdid 产物，不会写 `Config/Resource.bin` 或切换 Honey 的资源目录。

## 归档与同种子输入

4.4、4.5、4.5v3 和 4.6 的非活动参考文件以无损 ZIP 保存在 `Proto/Archives/`，正常 4.7 构建不需要它们。运行读取旧版本路径的命令（例如下面的示例）前，按[归档说明](../../Proto/Archives/README.zh-CN.md)解压所需版本；解压出的目录被 Git 忽略。

`Proto/4.5v3/translations.txt` 不在 ZIP 中，仍作为原文件由 Git 跟踪，因为 `LoginSceneRegression` 直接读取它。回归中的同种子检查把这个文件的强制映射交给 ProtoOrganizer 的 `SameSeedMigrator`（回归为此引用了 ProtoOrganizer 项目），要求 `SceneMapInfo` 的 4.5 显式字段修正（`floor_id = 14`、`retcode = 6`）在完整的同种子重新生成后依然成立，而不只是对 protoc 成立。只要这项检查还在，就不要把该文件归档或删除。

## 历史 4.5 迁移示例（不是本分支的活动版本）

先按[归档说明](../../Proto/Archives/README.zh-CN.md)解压 4.4 和 4.5；生成到 `Proto/Generated` 的 C# 仅供工具检查，服务器构建不会编译这个旧路径。该命令还会写入 `KcpSharp/CmdIds.cs`，不要在本分支上用它替换当前命令号或活动版本。

```sh
dotnet run --project Proto/Tool/ProtoOrganizer/ProtoOrganizer.csproj -- \
  --proto-file Proto/4.5/dumper/Raw \
  --raw Proto/4.5/dumper/Raw \
  --out Proto/4.5/dumper \
  --same-seed-raw Proto/4.4/dumper/Raw \
  --same-seed-proto Proto/4.4 \
  --translation Proto/4.5/translations.txt \
  --compat-types Proto/4.5/compat-types.txt \
  --cmdid-aliases Proto/4.5/cmdid-aliases.txt \
  --version 4.5 --active --compile \
  --cs-out Proto/Generated \
  --cmdids-out Proto/4.5/dumper/CmdIds.cs \
  --cmdids-out KcpSharp/CmdIds.cs
```

工具会依次完成：读取 Dumper 原始定义与 `Cmd*Type` enum、恢复 cmdid、利用同种子旧版本对齐类型和字段、加入版本化兼容类型/命令别名、重新分组、运行 `protoc`。一个版本必须全部 proto 编译成功后才会用于 Honey 构建。

已有 grouped proto 只需补语义名时，使用 `--rewrite-proto` 指定原位翻译的文件、使用 `--compile-rewrite` 指定需要重新生成同名 C# 的文件。该模式只应用翻译表（`--translation`）中以 `!` 开头的类型映射，不引入 Raw 中的其他定义；可同时通过 `--cmdid-aliases` 与 `--cmdids-out` 更新语义命令别名。

`protoc` 默认从 `Google.Protobuf.Tools` NuGet 缓存按当前系统自动定位，也可用 `--protoc <path>` 覆盖。

## 自动生成与验证

```sh
dotnet restore March7thHoney.sln
dotnet build March7thHoney.sln -c Release --no-restore
python3 scripts/test-proto-generation.py
```

NuGet 固定 `Google.Protobuf.Tools 3.33.1`（protoc 33.1），与现有 `Google.Protobuf 3.33.1` 运行时一致；两个版本都在仓库根目录的 `Directory.Packages.props` 中声明。首次构建需要还原此工具包；工具不会进入服务器发布产物。Linux、Windows 使用宿主平台工具，交叉发布不改变宿主选择。该版本 macOS 工具为 x64，Apple Silicon 需要 Rosetta；也可用 `-p:ProtocolCompiler=<path>` 指向版本严格匹配的 protoc 33.1。

目标检测 schema 的新增、删除、修改和缺失的生成文件；无变化时跳过生成。`dotnet clean` 后再次构建会恢复全部生成代码。修改分组定义，不手改 `obj/`。

目标还会在生成代码旁写出 `ProtocolDescriptors.g.cs`，其中 `ProtocolDescriptors.BuildAll()` 列出每个已编译 schema 的 `<File>Reflection.Descriptor`（每个 schema 都保留 `option csharp_namespace = "March7thHoney.Proto"`），服务器启动时据此构建全部描述符而无需反射，避免 NativeAOT 裁剪使其悄然失效。

`scripts/test-proto-generation.py` 在路径含空格的临时目录里，用 `Proto.csproj`、`Build/` 和活动版本 schema 的副本，加上仓库的 `global.json`、`Directory.Build.props` 和 `Directory.Packages.props` 运行生成，检查首次与增量生成、缺失输出的恢复、schema 的新增/修改/删除，以及目标运行时不会改变宿主编译器的选择。

构建前的检出体积明显减少，但构建时生成代码和 NuGet 缓存仍占空间。旧 Git 历史没有改写；完整克隆仍可能下载历史大文件。

## 命令号

`KcpSharp/CmdIds.cs` 保存活动版本的命令号。ProtoOrganizer 把它和该版本的快照 `Proto/<version>/CmdIds.cs` 一起生成（本文命令中的两个 `--cmdids-out`）：每个命令号一个常量，以对应的 `Cmd*Type` 枚举成员或 `Raw/packetIds.txt` 中的可读名命名，后面是 `cmdid-aliases.txt` 里的别名。别名指向现有常量名，而不是数值。

`scripts/verify-security.py` 检查 `KcpSharp/CmdIds.cs` 与 `Proto/<ActiveProtocolVersion>/CmdIds.cs` 逐字节一致。ProtoOrganizer 没有只比较不写入的模式，因此两份要一起重新生成，不要手改其中一份。

命令枚举名不一定等于 protobuf 消息类型名：处理器绑定当前命令常量并解析真实消息类型，仅按枚举名搜索同名 `message` 会漏掉现有结构。请求没有处理器时框架自动发送空成功响应，不能用该响应判断功能已执行。

## 4.7 活动版本

4.7 测试服的定义来自 [DemiurgeCSharp](https://github.com/Mar7thDev/DemiurgeCSharp) 对客户端 `GameAssembly.dll` 和 `global-metadata.dat` 的静态 dump（`demiurge proto`）。当前活动定义采用正式服 OSPRODWin4.7.0 的静态协议 dump；两者均不依赖运行时 dumper。

```text
Proto/4.7/
  dumper/Raw/StarRail.proto     测试服 Demiurge 原始输出：混淆类型名（恢复出的真名带 // obf:）、字段号与 cmdId
  dumper/Retail/StarRail.proto  从正式服 IL2CPP 元数据提取的协议 dump；仅作本地证据，已被 Git 忽略
  Raw/StarRail.proto           翻译后的扁平 master，分组文件由它生成
  Raw/packetIds.txt            cmdid -> 名称（// obf: 原混淆名）
  Raw/translations.txt         obf 类型 -> 名称
  overrides-manual.txt         逐条审定的类型名和字段名，附证据
  migration-report.txt         4.6 -> 4.7 测试服及测试服 -> 4.7.0 正式服的变更和命名依据
  cmdid-aliases.txt            4.6 别名按 cmdid -> obf 跟随到 4.7 名称
```

从 4.6 迁移到 4.7 测试服时，已审定的类型名、字段名和枚举成员名按混淆身份对应到测试服的字段号。Demiurge 按属性名给字段命名；字段号、cmdId 和 backing field 名可能变化，但属性名通常不变。字段身份按证据强弱逐级匹配：4.6 手工钉死的字段与原样保留的混淆名 → 4.4/4.5 Mar7thDumper 常量名与同版本可读定义按字段号的对应 → 4.6 master 同字段号 → 4.6 自动 overrides → 4.6 dumper raw（原名或 `nt.txt` 译名）。`nt.txt` 来自按存储槽位匹配，相邻同类型字段会互换（SceneInfo 的 `world_id` / `leader_entity_id`、`lighten_section_list` / `opened_chests_list` 即此类），只用来识别身份，不单独决定名称。

切换到正式服布局时，混淆身份不变的类型和属性沿用测试服 master 的名称。新增项只有在上游 4.8 存在相同混淆身份时才采用其名称，否则保留混淆名。正式服 dump 根据 C# 的 `int` 和 `long` 推断标量类型，无法区分 `int32` 与 `sint32`、`int64` 与 `sint64`；若仅有此差异，保留测试服 master 的编码。各类型的改动及依据见 `Proto/4.7/migration-report.txt`。

维护 4.7 定义时修改 `Raw/StarRail.proto`，再用下列命令重建分组定义和命令号。`--cs-out Proto/Generated` 是可选的工具校验输出，已经忽略且不参与服务器编译；随后必须正常构建，让 MSBuild 从活动分组定义自动生成到 `obj/`：

```sh
dotnet run --project Proto/Tool/ProtoOrganizer/ProtoOrganizer.csproj -- \
  --proto-file Proto/4.7/Raw --raw Proto/4.7/Raw --out Proto/4.7 \
  --version 4.7 --active --compile --cs-out Proto/Generated \
  --cmdid-aliases Proto/4.7/cmdid-aliases.txt --compat-types Proto/4.7/compat-types.txt \
  --cmdids-out Proto/4.7/CmdIds.cs --cmdids-out KcpSharp/CmdIds.cs
```

`LoginSceneRegression` 中有按版本写死的 cmdId 与字段号断言（VideoKeyInfo、SceneMapInfo、SyncServerSceneChangeNotify、CommonRogueQuery 分支、快速升级、常用队伍、经典模拟宇宙 18xx 命令），切换活动版本时一并更新。

快速升级是命令枚举名与消息类型名不一致的例子：4.6 的 `KELLLHMHFHL`（376）在 `dumper/Raw/cmdid-registry.txt` 中对应 `EKLIDIDKFHC`，捕获的响应为 `PMEMIDKDAFG`（357，枚举 `MILFFHPMLMN`）；4.7 的 `Raw/packetIds.txt` 和同身份定义分别对应 355/341。4.6 官服抓包 seq `36813-36829` 可验证请求/响应配对与升级费用，不能将其旧命令号直接用于 4.7。

光锥快速升级的身份为 `EOBJKAMNNPI → AMNBMKEMMFL`。4.6 原始定义重复误标为 `PromoteEquipmentCsReq/ScRsp`；官服 seq `36852/36859/36927` 的原始消息分别可解出目标等级、唯一 ID、经验消耗、突破消耗及突破等级。4.7 已映射为 `EquipmentQuickUpCsReq/ScRsp`（513/517）：`IGOOJKMPMDC` 为目标等级，`KLMIBJIKIBB` 为目标突破，`BOBOKJBAMNB` 为经验材料或光锥狗粮，`IDGEEENIMMK` 为突破材料。按属性身份使用现有活动定义，不改成仅执行突破的 `PromoteEquipmentCsReq`。

## 不参与构建的代码

`GameServer/GameServer.csproj` 用显式的 `<Compile Remove>` 把部分代码排除在构建之外，每一项旁边的注释写明原因：

- DiceCombat 处理器 `HandlerDiceCombatFinishPveStageCsReq`：4.6 只剩两个同型 `uint32` 候选，旧映射本就是未验证猜测，不再续猜。
- 从上游合入的两项 4.8 专属活动：[邦布联动](activity-bangboo.zh-CN.md)和[骰子收容](dicey-dungeon.zh-CN.md)，包括玩法逻辑、请求处理器、发送包和任务完成处理器。在正式服协议 dump 及其 IL2CPP 元数据中，尚未找到这两项活动可验证的命令号或消息映射。[成名之路](reality-show.zh-CN.md)使用正式服 4.7 协议参与构建，不再排除。

`LoginSceneRegression` 也跳过邦布联动和骰子收容的用例：项目文件排除 `ActivityBangbooRegression.cs`，`#if PROTOCOL_48_ACTIVITIES` 排除对应调用和内联用例。成名之路的用例照常运行。重新启用其他文件前，应按活动版本确认协议依据并解决兼容性问题，同时恢复对应的回归用例。不得把 4.8 的命令号或字段号写进 4.7。

## 新版本约束

- 原始 dump 保留在对应版本的 `dumper/` 下，不与 `reference/` 混放。不同客户端构建的产物分目录保存，例如 4.7 的 `dumper/Raw/` 与 `dumper/Retail/`。正式服客户端的 dump 不提交：`dumper/Retail/` 已被 Git 忽略，只提交由它翻译出的 master 和分组定义。
- `reference/` 只保存同一游戏版本的参考定义，未更新时保持不存在或保持旧版本目录，不得伪造新版本 reference。
- 兼容类型和命令别名必须放在对应版本清单中，禁止把旧 cmdid 数值覆盖到新版本。
- 不能仅凭同型字段认定映射。
- 切换活动版本时同步更新 `Proto.csproj` 的 `ActiveProtocolVersion`、分组定义和 `KcpSharp/CmdIds.cs`，再执行 `dotnet build March7thHoney.sln` 与生成回归；同时更新 `LoginSceneRegression` 中按版本写死的断言，并按新协议核对 `GameServer.csproj` 的 `<Compile Remove>` 排除项。

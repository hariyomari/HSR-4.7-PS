# 源生成器

[English](source-generators.md) | 简体中文

本文说明 `SourceGen/` 中的 Roslyn 源生成器：各自读取和生成什么、数据库映射规则、报告的诊断，以及如何修改和测试它们。新增由它们注册的类型或修改生成器之前请先阅读。

Source: [`SourceGen/`](../../SourceGen/)

`SourceGen` 是一组 Roslyn 增量源生成器，以分析器的形式被 `Common`、`GameServer`、`Command` 引用。它们在编译期把"按特性或基类找出一批类型再注册"这类原本要靠反射完成的工作写成普通 C#，服务器因此能在 NativeAOT 下运行。

## 生成器一览

| 生成器 | 读取 | 生成 | 使用方 |
| --- | --- | --- | --- |
| `HandlerRegistryGenerator` | 带 `[Opcode(cmdId)]` 的非抽象类 | `GeneratedHandlerRegistry.Build()`：cmdId 到处理器实例的字典 | `HandlerManager` |
| `CommandRegistryGenerator` | 带 `[CommandInfo]` 的非抽象类，及其 `[CommandMethod]`、`[CommandDefault]` 方法 | `GeneratedCommandRegistry.Build()`：命令特性、实例和各方法的调用委托 | `CommandManager` |
| `MissionHandlerRegistryGenerator` | 带 `[MissionFinishAction]`、`[MissionFinishType]` 的非抽象类 | `GeneratedMissionHandlers.Register()`：填充 `MissionManager` 的两张处理器表 | `ServerUtils` |
| `MemoryPackRegistrarGenerator` | 程序集内的 `[MemoryPackable]` 类和结构 | 模块初始化器 `MemoryPackRegistrar`：提前运行各类型的静态构造函数，注册其 formatter | 程序集加载时自动执行 |
| `DatabaseStoreGenerator` | `BaseDatabaseDataHelper` 的非抽象子类 | `DbStores.g.cs`：每个实体的行类型和建表、读取、插入、更新、删除代码，`EntityStores.Map`，以及 BLOB 列所需 formatter 的注册 | `DatabaseHelper` |

新增处理器、命令、任务处理器或数据库实体时只需按约定声明类型，不需要另外登记。抽象类不会被注册。

## 数据库实体的映射规则

- 可读写的实例属性各对应一列；`[DbIgnore]` 的属性、只读属性和静态属性不存储。基类的属性排在前面，`Uid` 是主键。
- 表名默认取类名，可用 `[DbTable("name")]` 指定。
- 数值、`bool`、`string`、枚举和 `byte[]` 按数据库原生类型存储（枚举存为整数）。
- 其他类型用 MemoryPack 序列化为 BLOB；实现 `Google.Protobuf.IMessage` 的类型用 protobuf 序列化为 BLOB。
- 实体新增属性后，已有的表会在启动时自动补上对应的列（`DbSchema.EnsureColumns`）；删除或改类型不会自动迁移。
- 实体必须声明在定义 `BaseDatabaseDataHelper` 的程序集（`Common`）里，`DatabaseHelper` 只使用该程序集的 `EntityStores.Map`。

## 诊断

| ID | 含义 |
| --- | --- |
| `M7H001` | 两个处理器声明了同一个 cmdId |
| `M7H002` | 两个任务处理器注册了同一个枚举值 |
| `M7H003` | 两个数据库实体的类名相同（类名决定生成的 store 名） |

三者都是错误，报在重复的那个声明上。

## 查看生成结果

生成的文件默认只存在于编译器内存里。需要查看时让编译器把它们写到磁盘：

```sh
dotnet build Common/Common.csproj -c Release --no-incremental \
  -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=<输出目录>
```

不要手改这些文件，改动应落在 `SourceGen/` 的源码里。

## 修改生成器

- 每个生成器先把语法和符号读成模型，再从模型写出代码。模型只包含字符串、数字和 `EquatableArray<T>`，按值比较：增量管线靠比较模型决定是否重新生成，模型里放进符号、语法节点或普通数组都会让每次编辑触发重新生成。
- 写代码用 `CodeWriter`，缩进随打开的块自动变化，行尾固定为 `\n`。
- 生成的代码中的类型名一律带 `global::`，所需的 `using` 由生成的文件自己声明，不依赖使用方项目的全局 using。
- 生成器项目的目标框架是 `netstandard2.0`，不能使用需要新运行时的 API。

改完后运行回归：

```sh
dotnet run --project scripts/SourceGenRegression/SourceGenRegression.csproj -c Release
```

它对每个生成器跑一份样例输入（`scripts/SourceGenRegression/Cases/<名称>/Input.cs`），检查：

- 输入和生成的代码都能通过编译；
- 生成的文件与同目录下的期望文件逐字符一致；
- 无关的编辑不会让生成器重新产出结果；
- 重复声明会报出对应的诊断。

预期最后一行为 `SourceGen regression checks: <数量>, failures: 0`。有意改变输出时追加 `-- --update` 重写期望文件，并在提交前逐行审查期望文件的差异。该回归不需要游戏资源，已包含在 `scripts/verify-security.py` 中。

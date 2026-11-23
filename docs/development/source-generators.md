# Source Generators

English | [简体中文](source-generators.zh-CN.md)

This document describes the Roslyn source generators in `SourceGen/`: what each one reads and generates, the database mapping rules, the diagnostics they report, and how to change and test them. Read it before adding a type they register or changing a generator.

Source: [`SourceGen/`](../../SourceGen/)

`SourceGen` is a set of Roslyn incremental source generators, referenced as analyzers by `Common`, `GameServer` and `Command`. At compile time they turn work that would otherwise need reflection, "find a set of types by attribute or base class and register them", into ordinary C#, which is what lets the server run under NativeAOT.

## Generators

| Generator | Reads | Generates | Used by |
| --- | --- | --- | --- |
| `HandlerRegistryGenerator` | Non-abstract classes with `[Opcode(cmdId)]` | `GeneratedHandlerRegistry.Build()`: a dictionary from cmdId to handler instance | `HandlerManager` |
| `CommandRegistryGenerator` | Non-abstract classes with `[CommandInfo]`, and their `[CommandMethod]` and `[CommandDefault]` methods | `GeneratedCommandRegistry.Build()`: command attributes, instances and an invocation delegate for each method | `CommandManager` |
| `MissionHandlerRegistryGenerator` | Non-abstract classes with `[MissionFinishAction]` or `[MissionFinishType]` | `GeneratedMissionHandlers.Register()`: fills the two handler tables of `MissionManager` | `ServerUtils` |
| `MemoryPackRegistrarGenerator` | `[MemoryPackable]` classes and structs in the assembly | Module initializer `MemoryPackRegistrar`: runs each type's static constructor early, which registers its formatter | Runs automatically when the assembly loads |
| `DatabaseStoreGenerator` | Non-abstract subclasses of `BaseDatabaseDataHelper` | `DbStores.g.cs`: a row type for each entity and the code to create its table, read, insert, update and delete, `EntityStores.Map`, and registration of the formatters needed by BLOB columns | `DatabaseHelper` |

To add a handler, command, mission handler or database entity, declare the type following the convention; no separate registration is needed. Abstract classes are not registered.

## Database entity mapping rules

- Each readable and writable instance property maps to a column; properties marked `[DbIgnore]`, read-only properties and static properties are not stored. Base class properties come first, and `Uid` is the primary key.
- The table name defaults to the class name and can be set with `[DbTable("name")]`.
- Numbers, `bool`, `string`, enums and `byte[]` are stored as native database types (enums as integers).
- Other types are serialized to a BLOB with MemoryPack; types implementing `Google.Protobuf.IMessage` are serialized to a BLOB with protobuf.
- When an entity gains a property, existing tables get the matching column added automatically at startup (`DbSchema.EnsureColumns`); removing a property or changing its type is not migrated automatically.
- Entities must be declared in the assembly that defines `BaseDatabaseDataHelper` (`Common`); `DatabaseHelper` only uses that assembly's `EntityStores.Map`.

## Diagnostics

| ID | Meaning |
| --- | --- |
| `M7H001` | Two handlers declare the same cmdId |
| `M7H002` | Two mission handlers register the same enum value |
| `M7H003` | Two database entities have the same class name (the class name determines the generated store name) |

All three are errors, reported on the duplicate declaration.

## Inspecting the generated code

By default the generated files exist only in the compiler's memory. To inspect them, have the compiler write them to disk:

```sh
dotnet build Common/Common.csproj -c Release --no-incremental \
  -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=<output directory>
```

Do not edit these files by hand; changes belong in the source under `SourceGen/`.

## Changing a generator

- Each generator first reads syntax and symbols into a model, then writes code from the model. The model contains only strings, numbers and `EquatableArray<T>`, compared by value: the incremental pipeline decides whether to regenerate by comparing models, and putting symbols, syntax nodes or plain arrays into a model makes every edit trigger regeneration.
- Code is written with `CodeWriter`; indentation follows the open blocks automatically, and line endings are always `\n`.
- Type names in generated code are always prefixed with `global::`, and the generated file declares the `using` directives it needs itself instead of relying on the consuming project's global usings.
- The generator project targets `netstandard2.0` and cannot use APIs that need a newer runtime.

After a change, run the regression:

```sh
dotnet run --project scripts/SourceGenRegression/SourceGenRegression.csproj -c Release
```

It runs a sample input for each generator (`scripts/SourceGenRegression/Cases/<name>/Input.cs`) and checks that:

- the input and the generated code both compile;
- the generated files match the expected files in the same directory character by character;
- unrelated edits do not make the generator produce output again;
- duplicate declarations report the corresponding diagnostic.

The expected last line is `SourceGen regression checks: <count>, failures: 0`. When the output changes on purpose, append `-- --update` to rewrite the expected files, and review the diff of the expected files line by line before committing. The regression needs no game resources and is included in `scripts/verify-security.py`.

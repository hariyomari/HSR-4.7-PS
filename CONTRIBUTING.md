# Contributing to March7thHoney

English | [简体中文](CONTRIBUTING.zh-CN.md)

This guide covers what you need to build the project, where things live, and what a change needs before it can
be merged. Instructions aimed at coding assistants are in [AGENTS.md](AGENTS.md); the rules are the same.

## Setting up

You need the .NET SDK 10 (10.0.100 or later; `global.json` pins the minimum), Git and Python 3 for the check
scripts. Run everything from the repository root.

```sh
dotnet restore March7thHoney.sln
dotnet build March7thHoney.sln -c Release --no-restore
```

The repository does not contain game resources. The regression suites listed under "Without game resources"
below run without them; the others need a matching resource cache, see
[rebuilding the resource cache](docs/operations/resource-cache-from-client-export.md).

## Where things are

| Path | Contents |
| --- | --- |
| `Program/` | Entry point: configuration, resources, database, network services, shutdown |
| `GameServer/Game/` | Players, scenes, battles, missions and the managers of each game mode |
| `GameServer/Server/Packet/` | Request handlers (`Recv/`) and response or notify packets (`Send/`) |
| `WebServer/` | HTTP services, login, MUIP, the admin and player console pages |
| `Command/` | In-game and administrative commands |
| `Common/` | Shared configuration, resource models, database entities, enums, localization |
| `SourceGen/` | Source generators that register handlers, commands, mission handlers and database stores |
| `KcpSharp/` | KCP transport and the generated command ids |
| `Proto/` | Protocol definitions per game version (the active one, `Proto/4.7/`, is selected by `ActiveProtocolVersion` in `Proto.csproj`), archives of older versions, and the tool that maintains them |
| `scripts/` | Regression programs, cache tools, check scripts and Linux deployment scripts |
| `docs/` | Documentation, indexed in [docs/README.md](docs/README.md) |

## Branches

- `main` is the development branch. It holds the protocol of the current game version.
- A branch named after a game version (`4.7`, `4.8`, ...) is a frozen snapshot of `main` taken when `main` moved
  on to the next version. Do not target these branches.
- Work on a short-lived branch and open a pull request against `main`. The branch is deleted after the merge.

## Commits and pull requests

- One concern per commit and per pull request. Unrelated clean-ups go in their own change.
- Subjects follow Conventional Commits: `type(scope): summary`, for example
  `fix(gacha): keep the pity counter when a banner rotates`. Common types are `feat`, `fix`, `refactor`, `docs`,
  `test`, `build`, `ci` and `chore`.
- The body says what was wrong or missing, what changes in behavior, and how you verified it. State what you did
  not verify, in particular whether the change was tried with a real client.
- Do not commit local runtime data: `Config.json`, databases, logs, tokens, game resources.

## Before you open a pull request

Run the checks CI runs:

```sh
dotnet build March7thHoney.sln -c Release -warnaserror "-warnnotaserror:NU1901;NU1902;NU1903;NU1904"
python3 scripts/verify-security.py --skip-build
python3 scripts/check-docs.py
```

Then run the regression suite that matches what you changed. A change that fixes behavior should add a check to
the matching suite that fails without the fix.

Without game resources (all run by `scripts/verify-security.py`):

| Area | Command |
| --- | --- |
| Accounts, authentication, bans, tokens | `dotnet run --project scripts/AuthRegression/AuthRegression.csproj` |
| Web admin, MUIP, command isolation | `dotnet run --project scripts/WebServerRegression/WebServerRegression.csproj` |
| KCP endpoint binding and admission | `dotnet run --project scripts/KcpSecurityRegression/KcpSecurityRegression.csproj` and `scripts/KcpAdmissionRegression/KcpAdmissionRegression.csproj` |
| Source generators | `dotnet run --project scripts/SourceGenRegression/SourceGenRegression.csproj -c Release` |
| Inventory costs, shop, synthesis | `dotnet run --project scripts/InventoryRegression/InventoryRegression.csproj -c Release` |
| Protocol build | `python3 scripts/test-proto-generation.py` |

With a resource cache (`Config/Resource.bin`):

| Area | Command |
| --- | --- |
| Login protocol, scenes, teleport | `dotnet run --project scripts/LoginSceneRegression/LoginSceneRegression.csproj -c Release` |
| Currency War (GridFight) | `dotnet run --project scripts/GridFightRegression/GridFightRegression.csproj` |
| Daily training and daily missions | `dotnet run --project scripts/DailyActiveRegression/DailyActiveRegression.csproj -c Release` |
| Raw resources and enum compatibility | `dotnet run --project scripts/ResourceRegression/ResourceRegression.csproj -c Release -- <resources> <report directory>` |

Each suite is described under [docs/testing/](docs/README.md). A suite that was skipped is not a suite that
passed; say which ones you ran. Server-side regressions do not replace a test with a real client for anything
the player sees: scene entry, UI, animation, battle.

## Conventions

- Follow the C# style of the file you are editing: four spaces, file-scoped namespaces, PascalCase types and
  methods. `.editorconfig` describes it; it is not a reason to reformat code you are not changing.
- Keep nullable checks meaningful. Do not add `!` or disable a warning to make it go away.
- Comments and XML documentation are written in English. They explain reasons, constraints and protocol evidence,
  not the history of the code. Do not leave commented-out code. A `TODO` says what is missing and why. A game
  term may be followed once by its Chinese name in parentheses.
- New player features go into a manager or component, not into `PlayerInstance`; see
  [player components](docs/development/player-components.md).
- Request handlers use `[Opcode(CmdIds.XxxCsReq)]`, commands use `[CommandInfo]`, mission handlers use their
  attributes. Registration is generated at compile time; do not add reflection-based discovery. See
  [source generators](docs/development/source-generators.md).
- Validate input and permissions before changing state wherever rewards, purchases, settlements or accounts are
  involved, and consider repeated requests and concurrent ones.
- Changes to saved data, database entities or MemoryPack models must keep old data readable.
- The server is published with NativeAOT. New JSON request or response types for the web server need an entry in
  `WebServer/WebJsonContext.cs`.
- Package versions live in `Directory.Packages.props`; a `PackageReference` carries no `Version`.
- Protocol changes follow the [protocol guide](docs/development/protobuf.md). Never copy command ids or field
  numbers from another game version.

## Documentation

English is the authoritative language: `name.md`. Each document has a Simplified Chinese translation next to it,
`name.zh-CN.md`, which is updated in the same change. Only the project README is kept in other languages. Every
document is listed in [docs/README.md](docs/README.md) and [docs/README.zh-CN.md](docs/README.zh-CN.md);
`scripts/check-docs.py` checks links, translations and the indexes.

## Reporting problems

- Bugs: open an issue with the commit you are running, the game client version, and the relevant server log.
- Vulnerabilities: follow the [security policy](SECURITY.md) and do not open a public issue.

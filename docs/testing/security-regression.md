# Security regression gate

English | [简体中文](security-regression.zh-CN.md)

`scripts/verify-security.py` is the single entry point for the security regressions that need no private game resources. Run it before submitting changes to authentication, the web server, the KCP transport, protocol generation or dependencies; with `--with-resources` it also runs the suites that need a real resource cache.

## How to run

From the repository root:

```sh
python3 scripts/verify-security.py
```

Options:

- `--with-resources` also runs the LoginScene, GridFight and DailyActive regressions. It requires `Config/Resource.bin`; if the file is missing the gate fails instead of reporting the skip as a pass.
- `--skip-build` reuses a solution build you have already verified.

## Prerequisites

- .NET SDK 10 and Python 3.
- For `--with-resources`: a `Config/Resource.bin` that matches the current program version.
- The Linux deployment mock suite runs only when `bash` and `flock` are available; otherwise it is reported as SKIP.

## What it covers

The gate runs, in order:

- `scripts/check-comments.py`.
- A check that `KcpSharp/CmdIds.cs` is byte-identical to `Proto/4.7/CmdIds.cs`, the command ID snapshot of the active protocol version (`ActiveProtocolVersion` in `Proto/Proto.csproj`).
- A full Release build of the solution.
- The Auth and Web authentication and session-security regressions, the KCP source-binding, handshake-routing and handshake-capacity regressions (`KcpSecurityRegression`, `KcpAdmissionRegression`), the inventory cost regression (`InventoryRegression`) and `SourceGenRegression`.
- The protocol generation regression, `scripts/test-proto-generation.py`.
- The failure and rollback scenarios of the Linux update script (`scripts/test-update-server.py`), tested with isolated mock commands. It never touches a real systemd service.

The tests use only temporary databases, in-memory resources and each regression's own isolated configuration. Nothing is deployed.

### UDP session boundary

The KCP conversation ID is used only for routing; another source IP or port can no longer inject data into an existing session. Both channel types compare the endpoint recorded at creation before parsing and queueing. The test injects synthetic data into the in-memory receive queue only and sends no network traffic. A client whose IP or port changes must handshake again, which matches the channel's fixed send destination.

### Dependency patch

WebServer pins `System.Security.Cryptography.Pkcs` 10.0.13 explicitly, overriding the older minimum version that MimeKit pulls in transitively. Microsoft's advisories list 10.0.13 as the fixed line:

- https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7
- https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx

The project has no confirmed reachable entry point where an attacker's CMS data is processed. The patch removes a known dependency risk; it is not described as a verified remote vulnerability in this project.

## Expected result

The gate ends with `PASS: requested security gate; skipped resource suites are NOT validated` and exits with code 0. Without `--with-resources` it also prints `SKIP: LoginScene/GridFight/DailyActive resource suites (use --with-resources)`; skipped suites have not been validated.

## Known gaps

- The resource-backed LoginScene, GridFight and DailyActive regressions are skipped unless `--with-resources` is given.
- Real clients, Windows/macOS, NativeAOT and upgrades of real production data still need separate acceptance.
- The UDP endpoint check is not cryptographic authentication. It does not stop a network attacker who can forge the same source address; public deployments still need a firewall or upstream traffic limits.

# Native Linux Setup

English | [简体中文](native-linux.zh-CN.md)

This is the simple deployment path: one checkout, one server process, no Docker,
and no split dispatch/game node setup. After .NET is installed, Linux can run it
directly from source just like Windows.

## Debian 12/13

Install the server dependencies once:

```bash
sudo apt-get update
sudo apt-get install -y git
git clone <repo-url> March7thHoney
cd March7thHoney
bash scripts/setup-debian.sh
```

The project targets .NET 10. On Debian 12 and 13, Microsoft's supported package
feed provides `dotnet-sdk-10.0` for x64 and Arm64.

## Run

Start the server directly from the checkout:

```bash
dotnet run --project Program/Program.csproj
```

Linux paths are case-sensitive, so `Program` works and `program` does not. You
can also use the root launcher:

```bash
bash Start.sh
```

The app writes `Config.json` on first launch if it does not exist. Keep the
single-server settings like this unless you deliberately split services later:

```json
{
  "ServerOption": {
    "ServerConfig": {
      "RunDispatch": true,
      "RunGateway": true,
      "Regions": []
    }
  }
}
```

## Run Under systemd

Install a service from the checkout:

```bash
sudo bash scripts/install-systemd-service.sh
sudo systemctl start march7thhoney
```

Useful commands:

```bash
journalctl -u march7thhoney -f
sudo systemctl restart march7thhoney
sudo systemctl stop march7thhoney
```

For an interactive command console, run `bash scripts/run-server.sh` inside
`tmux` or `screen` instead of systemd.

## Update

From the checkout:

```bash
git pull --ff-only
dotnet run --project Program/Program.csproj
```

For the systemd unit installed by `install-systemd-service.sh`, use the update
helper instead of pulling directly into a running checkout:

```bash
bash scripts/update-server.sh --restart
```

Run the helper as the checkout/service owner, not with sudo for the entire script
(it requests sudo only for service control). The helper requires a clean checkout
with an upstream branch. Update
`update-server.sh` and `run-server.sh` together as one committed version before
using this managed-release workflow for the first time. Both the current and
candidate commit must carry the versioned published-runner contract; an older
runner is rejected before touching the service, rather than attempting an unsafe
rollback that would recompile source. Custom service units must invoke this
checkout's `scripts/run-server.sh`, as the supplied installer does.

It fetches a
fast-forward candidate and publishes it in a temporary detached worktree **before
stopping the service or changing the deployed checkout**. A failed restore or
build therefore leaves the running version untouched. On the first managed
update it also publishes the old commit as a rollback snapshot. This needs extra
disk space and SDK/network access while staging, but does not run the game server.

Published outputs are kept under the checkout's Git directory in
`march7thhoney-deploy/releases/`. Its `current` symlink is switched atomically.
`run-server.sh` executes that prebuilt DLL directly, without restoring or compiling
again; the working directory stays at the checkout root, so database, logs,
configuration and external resources keep their existing locations. Direct
`dotnet run` / `Start.sh` remains a source-build workflow and does **not** use the
selected published release.

If checkout activation or the immediate systemd start fails, the helper restores
the prior release and attempts to return to the old source with `git reset --keep`;
it will not discard edits made during deployment. A previously running service is
then restarted from the old published output. The failing update retains a
nonzero exit status, and rollback failures are reported explicitly. This is not a
health check: systemd being active does not prove the game server is ready, nor
does it catch a crash that occurs after the check.

Before an update, back up your database, `Config.json`, custom configuration and
resources. Executable rollback does **not** reverse database/resource migrations
or changes made by a briefly started candidate. For such releases follow their
migration-specific restore procedure. If restoring the source is blocked by
concurrent edits, inspect them rather than using `reset --hard`. Retained release
snapshots consume disk space; once an update has been accepted, older unused
snapshots can be removed manually, keeping `current` and a known-good rollback.

`--no-restart` prepares/selects the next release but never stops or starts systemd.
`--no-build` is source-only and is accepted **only with `--no-restart`**; it does
not replace a selected published release. Avoid editing the checkout while an
update is staging. An update lock prevents concurrent helper invocations.

Regression checks use temporary repositories and mocked .NET/systemd commands,
never your real service:

```bash
python3 scripts/test-update-server.py
```

## Network

Expose the HTTP dispatch/gateway port from `Config.json`:

```json
"HttpServer": {
  "BindAddress": "0.0.0.0",
  "PublicAddress": "example.com",
  "Port": 21000,
  "UseSSL": false
}
```

Expose the UDP game port from `Config.json`:

```json
"GameServer": {
  "BindAddress": "0.0.0.0",
  "PublicAddress": "example.com",
  "Port": 23300
}
```

If a reverse proxy terminates HTTPS, keep `UseSSL` false in the app and proxy to
`http://127.0.0.1:21000`. UDP game traffic still needs its own forwarding rule.

## UDP handshake admission limits

Unauthenticated KCP handshakes allocate transport queues before account login.
The listener now checks admission under the registration lock, before allocating
those queues. Existing sessions are never evicted to make room for a handshake.
The following `GameServer` settings apply (shown with defaults):

```json
{
  "MaxConnections": 4096,
  "MaxPendingConnections": 256,
  "MaxPendingConnectionsPerIp": 16
}
```

`MaxConnections` includes all registered connections, active or pending.
The other two limits count connections not yet in the authenticated ACTIVE state.
A successful login frees its pending quota; normal disconnect/unregister frees
all applicable capacity. Duplicate endpoints are checked atomically, including
IPv4-mapped IPv6 addresses. Nonpositive settings fall back to the defaults rather
than disabling the bounds. Rejected handshakes receive no new conversation and
can retry later; do not expect an authenticated error response before login.

For schools, shared networks or carrier NAT, raise the per-IP pending cap if more
than 16 users need to log in simultaneously. It does **not** restrict the total
number of already active players sharing that IP. Adjust the global limits to
host memory and expected load; these bounds prevent unbounded allocation but do
not replace upstream UDP rate limiting or guarantee availability during a flood.
The regression uses mock connection snapshots and parallel lock/check/register
sequences, not real sockets:

```bash
dotnet run --project scripts/KcpAdmissionRegression/KcpAdmissionRegression.csproj -c Release
```

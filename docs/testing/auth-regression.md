# Account security regression

English | [简体中文](auth-regression.zh-CN.md)

`AuthRegression` checks account registration, login, administrator authentication, bans, tokens and account deletion against a throwaway database. Run it after changing anything in accounts, authentication, bans, tokens, deletion or database concurrency.

Project: [`scripts/AuthRegression/`](../../scripts/AuthRegression/)

## How to run

From the repository root:

```powershell
dotnet run --project scripts/AuthRegression/AuthRegression.csproj
```

To check the admin pages in a browser, add `--serve`:

```powershell
dotnet run --project scripts/AuthRegression/AuthRegression.csproj -- --serve
```

This runs the regression first. When it passes, it starts an isolated admin fixture at `http://127.0.0.1:21001/admin/index.html`; sign in with the test key `local-auth-regression-only`. The fixture holds more than one page of synthetic accounts and IP/device records, all disposable. Stop it with Ctrl+C when you are done, and never expose this instance outside the local machine.

## Prerequisites

None beyond the .NET SDK. The regression creates a fresh temporary SQLite database and its own test configuration; it does not read the project's `Config.json` or any real player database.

## What it covers

### Accounts and login

- Migration of old tables.
- Registration and login bypass attempts; concurrent registration.
- Administrator authentication.
- IP normalization and forwarded headers; player IP checks in split deployments.

### Bans, tokens and deletion

- Independent device/IP blocks, token revocation, batch deletion and transaction rollback.
- Disconnecting sessions, deferred writes, and concurrent save and delete.
- Ban and deletion records after a reload.

### Account bans

- An account ban affects only that UID. Other accounts sharing the same PC device or public IP can still log in with a password, verify a token, exchange a Combo ticket and register.
- A banned UID is still rejected after switching device.
- Independent device/IP rules keep blocking, including the historical-device check when the device ID is omitted.
- Public placeholder fingerprints and their old hashes are filtered out.
- Rejection logs distinguish an account ban from an independent rule and contain no password, token or raw device value.

### Account email links

- Password reset emails are refused while `Email.PublicBaseUrl` is empty or not an absolute http(s) URL.
- With it set, the reset link uses that address even when the request carries a forged `Host` header.

### Website game-account API

- On/off switch and secret key; the source address after proxy forwarding.
- Case-insensitive unique user names; concurrent creation.
- A password change revokes tokens; ban and unban.
- With registration closed, the external registration redirect.

## Expected result

The run ends with:

```text
Auth regression failures: 0
```

The simulated game sessions have no real KCP transport, so the disconnect tests log expected send failures.

## Known gaps

- All checks use synthetic accounts and a temporary database. They do not replace testing with a real PC or Android client.

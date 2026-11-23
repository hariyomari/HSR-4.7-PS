# Account Cleanup and Device / IP Blocks

English | [简体中文](account-security.zh-CN.md)

This guide is for server administrators who delete accounts in bulk, block devices or IP addresses, or deploy the server behind a reverse proxy or as separate Dispatch and GameServer processes. It also describes the authentication behaviour after the upgrade and the admin API behind these functions.

The admin panel is at `/admin/index.html`; log in with the admin key.

## Bulk deletion

In **Accounts**, search by username, UID, IP, device ID and so on, then tick the accounts. The table header selects the current page, and **Select filtered** selects all matching accounts. The selection is kept across pages; changing the search term or filter conditions clears it.

Click **Delete selected**, check the list of UIDs and usernames, and type `DELETE`. Each batch holds at most 500 accounts. The operation revokes tokens, waits for online sessions to disconnect, then deletes each account and its game data in a transaction. The result shows the UIDs that succeeded, no longer existed and failed, separately; failed items can be retried. The batch as a whole is not one transaction: one account failing does not roll back the successful deletion of others.

Deletion cannot be undone. The system keeps a UID reservation record without any account data, so that a delayed write from an old session cannot recreate data and a deleted UID is not assigned to someone else. Separate device/IP blocks and global rate-limit counters are not removed with the account. Deleting a single account uses the same process.

## IP and device records

The account list shows the latest IP; the **Profile** page shows the registration IP, latest IP, current game connection IP and registration time. The **Security** page lists recent IPs, device IDs and any existing device fingerprints, and you can pick values to block there directly. The CSV export includes IP and time fields.

IPs come from the server connection or a trusted reverse proxy; only successful registrations and authenticated requests add to the readable history. Recent IPs and device IDs each keep at most 16 entries. Old data has only hashes, so historical IPs cannot be recovered; later logins fill the history in gradually. Old device hashes can still be used for blocking.

## Block rules

An account ban applies only to the specified UID. It does not spread to another account because that account used the same device, fingerprint or IP. To restrict a device or a network egress, create a separate device / IP block rule as described below.

In **Devices**, enter an IP or device ID, fill in a reason and an optional duration (such as `1h`, `7d`). Leave it empty for a permanent block. Exact IPv4 and IPv6 addresses are supported, and IPv4-mapped IPv6 addresses are normalized; CIDR ranges and wildcards are not supported.

Rules are stored in the database on their own and stay in effect after a restart and after the related accounts are deleted. Adding a rule revokes the tokens of known matching accounts and disconnects their sessions. Registration, password login, token verification, Combo ticket exchange and entering the game server all check blocks. An account whose known device is blocked cannot bypass the check by omitting the device ID. Expired rules stop taking effect, and an administrator can also lift a rule manually.

An IP block affects other players behind the same public egress. Device IDs are supplied by the client and can be changed; these restrictions are not unforgeable authentication of a physical device.

Confirmed public placeholder fingerprints such as `testdevicefp`, `asd9123cfa767` and `38d7ecd67b187`, as well as common empty values and all-zero IDs, take no part in device matching, registration identity limits or login rate limiting. Their known historical hashes are ignored too, and no new block rule can be created from these values or hashes; such rules left in the database are shown as invalid. Other device and IP rules remain in effect.

When authentication is refused, the `Auth` module logs at WARN level the entry point, the UID (`unknown` when not yet known before authentication), the source IP, the decision type (`account_ban` / `device_block` / `ip_block`), the matched scope (`uid` / `current` / `known_device`), the ID of the separate rule, the matched identity hash and the expiry time. The log contains no passwords, tokens or raw device IDs. A failed password check is not logged as an account ban.

This behaviour change needs no database change. Back up the program and configuration before deploying; in a split deployment, update both Dispatch and GameServer, then restart. Rolling back restores the original program and configuration, but the old version re-enables cross-account linked bans and placeholder fingerprint matching.

## Authentication behaviour after the upgrade

- Automatic account creation performs the same checks as the registration API: registration switch, device requirement, identity count limit and registration rate limit. The concurrent registration check and account creation are in the same critical section.
- Combo login accepts only valid tokens issued by this server. The path that created an `os_*` account from the client's external UID alone and logged it in without verification has been removed. Clients that relied on that path must first authenticate with an account on this server.
- A first-time user can no longer set the password of an account that has none; an administrator must set it. Automatic account creation also cannot skip an enabled email verification requirement; when email verification is required, submit a real email address through the official registration page.
- Ordinary device models, device names and app IDs are no longer treated as unique device identifiers.
- On startup the database adds new fields and tables automatically; no manual migration is needed. The changes take effect after updating the program and restarting.
- The player console (`/player/*`, `/console/*`), `/sr-tools` and `/sr-tools-export` check UID and password through one shared verifier. Accounts without a password are refused, a client under a login rate-limit block or an IP/device block is refused before the password is checked, every wrong password counts towards the login limit, and banned accounts are refused.
- `/sr-tools-export` is now `POST` with a JSON body `{"username":"<uid>","password":"<password>"}`. The old `GET ?uid=&password=` form returns 405, so the password no longer ends up in URLs, proxy logs or browser history. Update any script or tool that downloaded the export with `GET`.

### Anti-abuse limits

The switches under `ServerOption.Auth.AntiAbuse` (`RateLimitEnabled`, `LimitRegistrationPerIdentity`, `LoginRateLimitEnabled`, `EmailRateLimitEnabled`; `RequireRegistrationIdentity` already was) are off by default, so a self-hosted server lets its players register and sign in without throttling. A public server turns the ones it needs on in `Config.json` or on the admin page under **Settings → antiabuse**; the limits, windows and block durations keep their values.

- **Existing installations keep their value.** `Config.json` is written back on startup, so a file created by an older version still has these switches on.
- **Rollback:** set the switches to `true` again; nothing else depends on them.

### DefaultPermissions

`ServerOption.DefaultPermissions` is added to every account on top of its role. Its built-in default is now empty; it used to be `["*"]`, which made every registered player an administrator (including `command.account` and `command.others`).

- **Existing installations keep their value.** `Config.json` is read and written back on startup, so a file created by an older version still contains `"DefaultPermissions": ["*"]` and still grants everything. On startup, and when the value is changed from the admin page, the server logs a warning if it contains `*` or any permission the built-in User role does not have (for example `command.*`, `command.account`, `command.others`).
- **Migration:** back up `Config.json`, set `"DefaultPermissions": []` (or only low-risk permissions), and give operators their privileges through `PermissionRoles` / account roles. Restart, or change it from the admin access page, which takes effect immediately.
- **Rollback:** restoring the backup, or setting `"DefaultPermissions": ["*"]` again, restores the old behaviour; the startup warning then reappears. Rolling the program back to an older version does not change an existing `Config.json`; a file first created by the new version keeps `[]` under the old program too.

## Reverse proxies and split deployments

`HttpServer.TrustedProxies` lists only the IPs of reverse proxies that connect directly to this service; the default is `127.0.0.1` and `::1`. Set to `[]`, forwarding headers are ignored completely. External proxies must be added explicitly; this can be changed in the admin panel under **Settings → Network** and takes effect after a restart.

Only `X-Forwarded-For` and `X-Forwarded-Proto` from trusted proxies are accepted, and at most the rightmost hop is processed. The reverse proxy must correctly overwrite or append the client's actual IP and must not pass through a single forwarded value forged by the client unchanged. With multiple proxy levels, the directly connected trusted proxy should supply the final client address.

When Dispatch and GameServer run separately, both must set the same non-empty `ServerOption.ServerConfig.ServerExchangeSecret`. GameServer includes the actual player `clientIp` in the `/validate_account_token` request, which is authenticated with the shared secret, and Dispatch uses it to check IP blocks. Cross-server queries and token exchange without a configured shared secret return 401. Update both sides together in a split deployment.

## Admin API

All of the following endpoints require an administrator Bearer session or `X-Admin-Session`.

| Method and path | Request / result |
|---|---|
| `POST /admin/api/accounts/bulk-delete` | `{"uids":[10001,10002],"confirm":"DELETE"}`; returns `deleted_uids`, `missing_uids`, `failed` |
| `GET /admin/api/identity-blocks` | Returns the block list, including type, value, reason, expiry time and whether the rule is valid |
| `POST /admin/api/identity-blocks` | `{"kind":"ip","value":"192.0.2.1","reason":"abuse","permanent":true}`; the device type is `device` |
| `POST /admin/api/identity-blocks` | Timed example: `{"kind":"device","value":"device-id","permanent":false,"duration":"7d"}` |
| `DELETE /admin/api/identity-blocks/{id}` | Lifts the given block; `id` is the rule ID returned by the list |

To block an existing hash, send `identity_key` instead of `value`; the two cannot be given together. The API also validates the type, address format and duration; blocking the same normalized identifier again updates that rule.

For the isolated regression tests and browser test fixtures, see [Account security regression](../testing/auth-regression.md).

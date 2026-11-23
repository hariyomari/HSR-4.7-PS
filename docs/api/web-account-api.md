# Website Game Account API (WebAccountApi)

English | [简体中文](web-account-api.zh-CN.md)

This API lets a trusted website provision and manage game accounts on this server. It is for operators who run such a website next to the server and for developers who integrate one.

## Overview

The website handles sign-up, email verification and invitation codes; this API only turns a "verified website user" into an account that can log in to the game. The implementation is in `WebServer/Controllers/WebAccountRoutes.cs`; the regression is described in [Account security regression](../testing/auth-regression.md).

## Configuration

`Config.json` → `ServerOption.Auth`:

```json
"Auth": {
  "EnableRegistration": false,
  "ExternalRegistrationUrl": "https://example.com/register",
  "WebAccountApi": {
    "Enabled": true,
    "Secret": "<long random string, the same shared secret as configured on the website>",
    "AllowedAddresses": ["127.0.0.1", "::1"]
  }
}
```

- When `Enabled` is false or `Secret` is empty, every endpoint returns 404.
- `AllowedAddresses` is checked against the client address **as restored through trusted proxies**. A public request forwarded by a reverse proxy on the same machine (Caddy/nginx) carries `X-Forwarded-For`; after restoration its address is not 127.0.0.1, so it is refused even if the caller has the secret. An empty list means no address restriction; the secret is then the only line of defence.
- `EnableRegistration: false` disables the game's built-in registration (including AutoCreateUser); accounts provisioned by the website are not affected. When `ExternalRegistrationUrl` is configured, `/login-platform/register.html` redirects to that address with a 302.
- It is recommended that the reverse proxy additionally refuse public access to `/external/*`.

## Authentication

Every request carries the header `X-March7thHoney-Web-Secret: <Secret>` (compared in constant time). On failure the response is 401 `{"retcode":-1,"message":"Unauthorized","error":"unauthorized"}`.

## Responses

A successful response always has this form:

```json
{"retcode":0,"message":"OK","data":{"account":{
  "uid":10001,"username":"WebPlayer","email":"a@b.c","nickname":null,"level":null,
  "banned":false,"ban_reason":"","online":false,"created_at":1790000000,"last_seen_at":0}}}
```

An error response is `{"retcode":-1,"message":"...","error":"<code>"}`, where `error` is a stable error code.

## Endpoints

Prefix: `/external/web-account/v1`.

| Method and path | Request body | Description / error codes |
| --- | --- | --- |
| `GET /status` | — | `data: {server_name, version, accounts, online, ready}`, for health checks |
| `POST /accounts` | `{username, password, email?}` | Creates an account with the email marked as verified. `invalid_username` (3–32 characters of `A-Z a-z 0-9 _ . -`), `invalid_password` (server password policy), `invalid_email`, `username_taken` (409, **case-insensitive**), `email_taken` (409), `not_ready` (503, the database is still loading) |
| `GET /accounts/{uid}` | — | Looks up an account; `not_found` (404) |
| `PUT /accounts/{uid}/password` | `{password, revoke_tokens?=true}` | Changes the password and by default also revokes all login tokens; `invalid_password` |
| `POST /accounts/{uid}/ban` | `{reason?}` | Bans permanently and kicks the player offline |
| `DELETE /accounts/{uid}/ban` | — | Lifts the ban |

When `email` is omitted, the server generates the placeholder address `<username in lower case>@web-account.invalid` (the server requires unique email addresses; a second game account for the same website user can be created this way). The server assigns the UID by its existing rules, and the player data is created the first time the player enters the game.

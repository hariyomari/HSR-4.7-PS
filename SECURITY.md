# Security policy

English | [简体中文](SECURITY.zh-CN.md)

## Supported versions

Security fixes are made on `main`. The version branches (`4.7`, `4.8`, ...) are frozen snapshots and do not
receive fixes; run a current build of `main`.

## Reporting a vulnerability

Report it privately through GitHub: open the repository's **Security** tab and choose **Report a
vulnerability**. Do not open a public issue or pull request and do not discuss it in the community chat until a
fix is available.

Please include:

- the commit you tested and how the server was configured (database type, whether registration, MUIP and the
  web account API are enabled);
- what an attacker can do and what they need for it (network position, an account, a token);
- steps or a script that reproduce it against a test server.

The maintainers confirm the report and work with you on a fix. The reporter is credited in the commit or
release notes unless they prefer otherwise.

## Scope

In scope: this server and its tools, including authentication and sessions, the web admin and player console,
MUIP, the web account API, command permissions, the KCP transport and the deployment scripts.

Out of scope: the game client and its publisher's services, and problems that need access the operator already
has (the server's own configuration file, database or host).

## For operators

Hardening guidance for a deployment is in [account security](docs/operations/account-security.md). The checks
behind past fixes run in `scripts/verify-security.py`, described in
[the security gate](docs/testing/security-regression.md).

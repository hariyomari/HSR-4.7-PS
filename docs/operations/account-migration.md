# Merging Accounts and Save Data from Another Server

English | [简体中文](account-migration.zh-CN.md)

This guide is for operators who want to move the players of another March7thHoney server onto this one. It describes what the migration tool changes, how to rehearse and run it, and how to roll back.

## Overview

`scripts/AccountMigration` merges the SQLite database of another March7thHoney server (accounts plus all save data) into this server's database. On 2026-09-27 it was used to merge the main instances of two other servers into one production server.

## Migration rules

- Banned accounts are not migrated; `AuthAttempt`, `IdentityBlock` and `DeletedAccount` are not copied.
- New UID = old UID + the offset of that source. When it clashes with a UID already in the target database (including tombstones of deleted accounts), a free UID from 30000 upwards is used instead.
- When the username (case-insensitive) is already taken, the suffix `_<source name>` is appended. When the email is a duplicate, the account migrated later gets `uid<new UID>@web-account.invalid` instead (it can still log in with its username).
- UIDs of other players referenced inside the save blobs (friends, blacklist, requests, chat, support characters, challenge ownership, cake likes) are rewritten with the same mapping; references to players that were not migrated are deleted; the system profile card UID and the global chat UID stay unchanged.
- Game session tokens are cleared; players log in again with their original password.
- Dictionaries with duplicate keys in the source data (rows written incorrectly by an old version, which the server itself cannot read) are written back keeping the last value of each key, and the affected row is printed.
- All writes happen in one transaction. After writing, every MemoryPack column of every row is deserialized once with the server's own entity types, and the transaction is committed only if all of them succeed.

## Steps

1. On the source server, take a snapshot with the SQLite online backup (without stopping the server) and transfer it to the target server.
2. Rehearse on a copy of the target database:
   `dotnet AccountMigration.dll --target copy.db --source other=other.db:10000 --report report.json`
   Each source is given as `--source <name>=<db>:<UID offset>`.
3. Stop the service, back up `Config/Database`, run the same command against the live database, restore the file ownership, then start the service and check the startup log.
4. `report.json` contains the old and new UID, username, email and password hash of every account, for creating the accounts on the website. Delete it after use.

## Other options

- `--validate <db>` only checks whether a database can be read by the current server.
- `--check-password <salt> <hash> <iterations> <password>` verifies a password hash.

## Rollback

Stop the service, copy the contents of the backed-up `Config/Database` directory back, and start the service.

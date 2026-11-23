# Archived protocol versions

English | [简体中文](README.zh-CN.md)

The inactive reference files of 4.4, 4.5, 4.5v3 and 4.6 are kept here as lossless ZIP archives. A normal 4.7 build does not need them extracted, and the active `Proto/4.7` stays as it is. `Proto/4.5v3/translations.txt` is read directly by the login regression (`LoginSceneRegression`, its same-seed check), so it stays a plain tracked file and is not in the ZIP.

## When you need an old version's definitions

The migration tool's historical defaults point to 4.4, and old examples may need 4.4, 4.5 or 4.6 at the same time. Before running one, extract the versions it needs into **Proto/** without overwriting existing edits.

Linux/macOS (from the repository root):

```sh
unzip -n Proto/Archives/4.4.zip -d Proto
unzip -n Proto/Archives/4.5.zip -d Proto
unzip -n Proto/Archives/4.5v3.zip -d Proto
unzip -n Proto/Archives/4.6.zip -d Proto
```

Windows PowerShell:

```powershell
Expand-Archive -LiteralPath Proto/Archives/4.6.zip -DestinationPath Proto
```

Replace the ZIP file name as needed, and do not add `-Force`, which would overwrite local changes. The extracted directories are ignored by Git, while `4.5v3/translations.txt` stays tracked. When you edit historical reference material, update the matching archive explicitly and check the differences; do not assume that an ignored edit has been committed. Reverting the slimming commit that created these archives also restores the originally tracked files.

## Integrity

Each ZIP keeps the original version directory and the original file bytes, and every file was checked one by one against its original Git blob. Entries are sorted by path, their timestamps are fixed at 1980-01-01, Unix permission metadata is kept, and Deflate level 9 is used.

| Archive | Files | Original bytes | ZIP bytes |
|---|---:|---:|---:|
| `4.4.zip` | 176 | 3260769 | 944490 |
| `4.5.zip` | 158 | 2211561 | 591808 |
| `4.5v3.zip` | 10 | 2227157 | 609624 |
| `4.6.zip` | 161 | 5051550 | 1353489 |

SHA-256:

```text
85948b7b5a11c6faa56af3d4f1a937d62a36937436986d9d545aecd858b26284  4.4.zip
f1e0808624798f13f48d000cd12abc396e51fe6e7843201f8eb722ae5fe15799  4.5.zip
0c8146856475dff215ba44bf449b722a1e9b050671e2685acb1b927defc7da27  4.5v3.zip
da80e364b7dbdbf1c83b9f040e8e2b83b314f45f69c71505406899f2da3f1f0a  4.6.zip
```

The archives only reduce the size of the default working tree; they do not remove the original files from Git history.

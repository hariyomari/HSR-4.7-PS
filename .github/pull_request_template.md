## What this changes

<!-- The problem or gap, and what behaves differently after this change. -->

## Verification

<!-- What you ran and the result. A skipped suite is not a passed suite. -->

- [ ] `dotnet build March7thHoney.sln -c Release -warnaserror "-warnnotaserror:NU1901;NU1902;NU1903;NU1904"`
- [ ] `python3 scripts/verify-security.py --skip-build`
- [ ] The regression suite for the area I changed (name it):
- [ ] Tried with a real client (say which version), or not applicable:

## Notes for review

<!-- Anything not verified, compatibility of saved data, follow-ups left out on purpose. -->

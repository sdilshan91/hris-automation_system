---
name: esbuild-host-build-platform-mismatch
description: FE build/test on the host Linux session fails because node_modules holds Windows-native binaries (shared NTFS drive) — install the linux esbuild binary without disturbing node_modules
metadata:
  type: project
---

Running `npm run build` / `npx ng test` directly on the **host Linux** session (not in Docker)
fails with: `You installed esbuild for another platform ... "@esbuild/win32-x64" is present but
this platform needs "@esbuild/linux-x64"`. The repo lives on an NTFS drive shared with Windows,
so `node_modules` was populated by Windows and carries win32-native binaries.

**Fix (non-destructive, does not touch package.json or the rest of node_modules):**
match the esbuild version first (`node -e "console.log(require('./node_modules/esbuild/package.json').version)"`),
then `npm install --no-save @esbuild/linux-x64@<that-version>` from `src/frontend`. After that,
`npm run build` and `npx ng test --watch=false --browsers=ChromeHeadlessNoSandbox` both pass.
node_modules is gitignored so this never travels in a commit.

**Why:** the user's normal FE build/test path is Docker-on-Linux (see project-local-dev-setup memory);
host-run builds hit the Windows/Linux binary split. Other native deps (lmdb, @parcel/watcher,
msgpackr-extract) did NOT need re-installing for build+test in practice — only esbuild did.

**How to apply:** if a host-run `npm run build`/`ng test` errors on a native binary platform
mismatch, apply the targeted `--no-save` install for the offending package rather than a full
`npm ci` (slow on the NTFS mount). Use the [[karma-headless-nosandbox-hang]] launcher for tests.

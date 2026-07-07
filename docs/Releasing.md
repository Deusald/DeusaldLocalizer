# Releasing & Auto-Update

How Deusald Localizer ships updates, and the exact steps to cut a new release.

Updates are powered by [Velopack](https://velopack.io). Instead of downloading a zip and
replacing files by hand, users install once (Windows: `DeusaldLocalizer-win-Setup.exe`; macOS:
`DeusaldLocalizer-osx-arm64-*.pkg`); the app then checks GitHub Releases on launch and updates
**itself** in place.

Both platforms ship from a **single** GitHub release per version. Windows and macOS are built by
separate scripts on their own OS ([scripts/build-release.ps1](../scripts/build-release.ps1) on
Windows, [scripts/build-release-mac.sh](../scripts/build-release-mac.sh) on macOS) and their
artifacts coexist on the same release — see [Platforms & channels](#platforms--channels).

## How it works

- **Install once.** On Windows users run `DeusaldLocalizer-win-Setup.exe` (installs to
  `%LocalAppData%\DeusaldLocalizer`, creates Start-menu / desktop shortcuts, no admin rights).
  On macOS they run the `.pkg`, which installs *Deusald Localizer.app* into `/Applications`.
- **Per-OS update feeds.** Velopack keeps a separate feed per platform: Windows clients read
  `releases.win.json`, macOS clients read `releases.osx-arm64.json`. Each install only ever looks
  at the feed for the channel it was installed from (see [Platforms & channels](#platforms--channels)).
- **`VelopackApp.Build().Run()`** is the first line of `MauiProgram.CreateMauiApp()`
  ([App/MauiProgram.cs](../App/MauiProgram.cs)). It handles Velopack's install/update/uninstall
  hooks and exits early during those, so it never reaches the UI.
- **On the Home screen**, `UpdateService` ([App/Services/UpdateService.cs](../App/Services/UpdateService.cs))
  asks Velopack's `UpdateManager` (pointed at a `GithubSource` for this repo) whether a newer
  **published, non-prerelease** release exists. If so, the "Update" banner appears; clicking it
  downloads the update (delta if available) and relaunches into the new version.
- **In the IDE / a portable run** `UpdateManager.IsInstalled` is `false`, so the check is skipped
  and the banner never shows. In-app update only works for a real Velopack install.

## Platforms & channels

Each release carries artifacts for **both** platforms, kept apart by Velopack **channels**. A channel
is the update-continuity key: an install only ever updates from the channel it was installed from, and
Velopack writes a `releases.<channel>.json` feed per channel.

| Platform | Built on | Script | Channel | Feed |
|---|---|---|---|---|
| Windows x64 | Windows | `build-release.ps1` | `win` (the OS default — passed implicitly) | `releases.win.json` |
| macOS (Apple Silicon) | **a Mac** | `build-release-mac.sh` | `osx-arm64` (explicit) | `releases.osx-arm64.json` |

**Why macOS uses an explicit `osx-arm64` channel.** Velopack only adds the channel token to the
`-full.nupkg` filename when the channel is *not* the OS default. Windows uses its default `win`, giving
a plain `DeusaldLocalizer-<ver>-full.nupkg`. macOS's default channel is `osx`, which would produce the
*same* plain `DeusaldLocalizer-<ver>-full.nupkg` — a filename collision on the shared release. Choosing
the non-default `osx-arm64` tokenises **every** Mac artifact (`…-osx-arm64-full.nupkg`,
`releases.osx-arm64.json`, …) so the two platforms never clash and the Windows channel is untouched.
The Mac app auto-detects its channel from its own install, so
[UpdateService.cs](../App/Services/UpdateService.cs) needs no per-platform code.

> **Never rename an existing channel.** Every shipped install is pinned to the channel it came from.
> Renaming Windows off `win`, or macOS off `osx-arm64`, silently orphans every existing install (it
> keeps polling the old feed, which you stop updating). Only introduce a *new* channel when you add a
> *new* target (e.g. a future `win-arm64` or `osx-x64` build), never as a rename.

**macOS builds are unsigned.** `build-release-mac.sh` produces an unsigned app, so macOS Gatekeeper
quarantines it. After installing, clear the quarantine once:
```bash
xattr -dr com.apple.quarantine "/Applications/Deusald Localizer.app"
```
(or right-click the app → **Open** the first time). Fine for testing; sign + notarize before any
public macOS distribution.

## Version rule

The Velopack package version comes straight from `<Version>` in
[App/App.csproj](../App/App.csproj). The build script reads it, and the app's welcome screen shows
`v<Version> · <git hash>`. **Bump `<Version>` for every release** (SemVer, e.g. `1.1.2` → `1.1.3`).
Clients only update to a strictly higher version.

`<ApplicationDisplayVersion>` should match; `<ApplicationVersion>` is a monotonic integer — bump it too.

## One-time setup (per machine)

- **.NET / MAUI workload** — as for any build (`dotnet workload install maui`).
- **Velopack CLI** — the build script auto-installs it, or do it yourself:
  ```powershell
  dotnet tool install -g vpk
  ```
  (Update later with `dotnet tool update -g vpk`.) Make sure `%USERPROFILE%\.dotnet\tools` is on PATH.
- **macOS builds need a Mac.** MacCatalyst compilation requires macOS + Xcode, and `vpk pack` for
  the osx runtime builds a `.app` bundle that only runs on macOS — Mac artifacts **cannot** be
  cross-built from Windows. On the Mac: install Xcode + the maui workload, and ensure
  `~/.dotnet/tools` is on `PATH` (the script auto-installs `vpk` there). `gh` (optional) lets the
  script attach the checksums file.
- **GitHub token** — a fine-grained PAT (repo = DeusaldLocalizer, **Contents: Read and write**),
  used to create/upload the release and to pull the previous release for delta generation. The
  script resolves it in this order:
  1. `-Token <value>` parameter, then
  2. `$env:GITHUB_TOKEN`, then
  3. **1Password** — read via the `op` CLI from `op://Private/GitHub Deusald Localizer Token/credential`
     (the `$opTokenRef` constant in the script). Pulled only when `-Upload` is set, so a local pack
     never prompts the vault.

  With 1Password you don't set anything per shell — just be **signed in to the `op` CLI**
  (`op signin`, or enable *Developer → Integrate with 1Password CLI* in the desktop app). Running
  `-Upload` triggers a normal 1Password approval prompt when it reads the token.
  Verify access any time with:
  ```powershell
  op read "op://Private/GitHub Deusald Localizer Token/credential"
  ```
  The macOS script resolves the token more simply: `--token <value>` then `$GITHUB_TOKEN` (no
  1Password integration) — `export GITHUB_TOKEN=…` before running it.

## Steps for every release

1. **Bump the version** in [App/App.csproj](../App/App.csproj): `<Version>`,
   `<ApplicationDisplayVersion>` (match), and `<ApplicationVersion>` (+1).
2. **Commit** the bump (the app embeds the commit hash, so build from a clean commit):
   ```powershell
   git add App/App.csproj
   git commit -m "version(App): bump to 1.1.3"
   ```
3. **Build, package, and upload a draft**:
   ```powershell
   ./scripts/build-release.ps1 -Upload
   ```
   This publishes a self-contained win-x64 build, downloads the previous release (for a small
   delta), packs the Velopack artifacts into `dist\`, then creates a GitHub **draft** release
   `v1.1.3` with all artifacts attached. **The script never goes live on its own.**

   (Run with no switch to pack into `dist\` only and touch nothing on GitHub — useful for testing
   `Setup.exe` locally first.)
4. **Add the macOS build (on a Mac).** From a clone at the *same commit*, on an Apple Silicon Mac:
   ```bash
   export GITHUB_TOKEN=<PAT with Contents: read/write>
   ./scripts/build-release-mac.sh --upload --merge
   ```
   This publishes a MacCatalyst arm64 build, packs the Velopack `osx-arm64` artifacts, and
   **`--merge`s** them into the *same* `v1.1.3` release the Windows step just created (add them to
   the draft before you publish it). `--merge` is what lets a second platform attach to an existing
   release instead of failing — it also works to add a Mac build to an already-published release.
   (Run without `--upload` to pack into `dist\` only.)
5. **Edit the description and publish, on GitHub.** Open
   [the releases page](https://github.com/Deusald/DeusaldLocalizer/releases), open the `v1.1.3`
   draft (drafts are visible only to you), write the description, and click **Publish** — keeping
   it a normal (non-prerelease) release. Only now do clients see it.
6. **Verify** by launching an already-installed older copy on each platform — the "Update" banner
   should appear on the Home screen and update to `1.1.3`.

### Where the description comes from

The draft's description body starts **empty** — you type it in the GitHub UI before publishing.
If you'd rather generate it from a file, pass `-ReleaseNotes path\to\notes.md` and its markdown
pre-fills the body (you can still edit it in the draft afterwards).

### Do I need to create/push the tag first?

**No.** `vpk upload` registers the `v1.1.3` tag with the draft, and GitHub creates the actual git
tag **when you click Publish**, pointing at the exact commit you built from (the script pins it via
`--targetCommitish`). Don't `git tag` or push a tag by hand — that would collide.

### What the build scripts produce in `dist\`

**Windows** (`build-release.ps1`):

| Artifact | Purpose |
|---|---|
| `DeusaldLocalizer-win-Setup.exe` | The installer — this is what new users download. |
| `DeusaldLocalizer-<ver>-full.nupkg` | Full release package (also the delta base for next time). |
| `DeusaldLocalizer-<ver>-delta.nupkg` | Small diff vs the previous release (skipped on the first release). |
| `DeusaldLocalizer-win-Portable.zip` | Portable build (no installer / no auto-update). |
| `releases.win.json` | The update feed the app reads to discover new versions. |

**macOS** (`build-release-mac.sh`) — every name carries the `osx-arm64` channel token:

| Artifact | Purpose |
|---|---|
| `DeusaldLocalizer-osx-arm64-*.pkg` | The macOS installer — what new Mac users download. |
| `DeusaldLocalizer-<ver>-osx-arm64-full.nupkg` | Full release package (also the delta base for next time). |
| `DeusaldLocalizer-<ver>-osx-arm64-delta.nupkg` | Small diff vs the previous Mac release (skipped on the first). |
| `DeusaldLocalizer-osx-arm64-Portable.zip` | Portable build (no installer / no auto-update). |
| `releases.osx-arm64.json` | The update feed the Mac app reads. |

**Upload the whole set** (each script does this for you). The `releases.*.json` feed and the
`.nupkg` files are what the client actually reads and downloads — a release with only the installer
will **not** auto-update anyone.

## Testing the update flow locally (no GitHub)

You can exercise the full **install → detect → download → apply → restart** cycle on your machine.
`UpdateService` honours a `DEUSALD_UPDATE_SOURCE` environment variable: when set, it updates from
that **local folder** (or URL) instead of GitHub. Unset in production, so it's inert for real users.

> Auto-update only works from a *real* install (`UpdateManager.IsInstalled`). Running from the IDE
> won't apply updates — you must install via `Setup.exe`, which is what these steps do.

1. **Pack the current version** (say `1.1.2`) into `dist\`:
   ```powershell
   ./scripts/build-release.ps1                     # no -Upload → local only, no GitHub call
   ```
2. **Install it** — run `dist\DeusaldLocalizer-win-Setup.exe`. It installs to
   `%LocalAppData%\DeusaldLocalizer` and launches once.
3. **Make a newer version** and pack it into the **same** `dist\` (vpk builds a delta from `1.1.2`
   and updates `releases.win.json`). Bump `<Version>` to `1.1.3` in
   [App/App.csproj](../App/App.csproj) first — the script reads it automatically, so no `-Version`
   flag is needed. (Bump the csproj, don't just pass `-Version`, so the version shown on the
   welcome screen matches what Velopack installs.)
   ```powershell
   ./scripts/build-release.ps1
   ```
4. **Launch the installed app pointed at `dist\`.** Launching the exe from this shell makes it
   inherit the env var (no need to set it system-wide):
   ```powershell
   $env:DEUSALD_UPDATE_SOURCE = (Resolve-Path .\dist).Path
   & "$env:LOCALAPPDATA\DeusaldLocalizer\current\DeusaldLocalizer.exe"
   ```
   The running `1.1.2` finds `1.1.3` in `dist\`, shows the **Update** banner; clicking it downloads
   the delta, applies, and relaunches into `1.1.3` (the welcome screen should now read `v1.1.3`).
5. **Clean up** when done:
   ```powershell
   Remove-Item Env:\DEUSALD_UPDATE_SOURCE
   # uninstall the test install from Windows "Apps & features" (Deusald Localizer), or:
   & "$env:LOCALAPPDATA\DeusaldLocalizer\Update.exe" --uninstall
   ```

Tip: to iterate on just the update UI without reinstalling each time, keep the install from step 2
and repeat steps 3–4 with ever-higher `-Version` numbers.

The same `DEUSALD_UPDATE_SOURCE` trick works on **macOS**: pack two versions with
`./scripts/build-release-mac.sh` (no `--upload`) into `dist/`, install the older `.pkg`, then launch
the installed app's **inner executable directly** so it inherits the env var (macOS `open` strips it):
```bash
DEUSALD_UPDATE_SOURCE="$(pwd)/dist" "/Applications/Deusald Localizer.app/Contents/MacOS/DeusaldLocalizer"
```
(The binary name is whatever `ls "/Applications/Deusald Localizer.app/Contents/MacOS"` shows. Clear
quarantine first, per [Platforms & channels](#platforms--channels).)

## Gotchas

- **Never delete or re-tag a published release's assets** once clients have seen them — Velopack
  reads the `releases.*.json` feeds and the `.nupkg` files by name. Removing them breaks in-place updates.
- **Never rename a channel** (`win`, `osx-arm64`) — it strands every existing install on that
  channel. See [Platforms & channels](#platforms--channels).
- **macOS builds are unsigned** — Gatekeeper quarantines them; clear it once with
  `xattr -dr com.apple.quarantine "/Applications/Deusald Localizer.app"`. Sign + notarize before any
  public macOS distribution.
- **Build order per release:** Windows first (creates the draft), then the Mac with `--merge` (adds
  to it). Both must build from the *same commit* so the embedded git hash matches.
- **Publish, don't leave as draft** — draft and pre-release releases are invisible to the updater
  (the `GithubSource` is configured with `prerelease: false`).
- **Version must increase.** Re-publishing the same version does nothing for existing installs.
- **The very first Velopack release** has no previous package to diff against, so there's no delta
  and the "previous release" download step logs a harmless warning — that's expected.
- Users on the **old zip-based builds** must install `Setup.exe` once to move onto the auto-updating
  channel; there's no automatic migration from a hand-extracted zip.

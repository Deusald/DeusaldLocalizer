# Releasing & Auto-Update

How Deusald Localizer ships updates, and the exact steps to cut a new release.

Updates are powered by [Velopack](https://velopack.io). Instead of downloading a zip and
replacing files by hand, users install once with `DeusaldLocalizer-win-Setup.exe`; the app then
checks GitHub Releases on launch and updates **itself** in place.

## How it works

- **Install once.** Users run `DeusaldLocalizer-win-Setup.exe`. It installs to
  `%LocalAppData%\DeusaldLocalizer` and creates Start-menu / desktop shortcuts. No admin rights.
- **`VelopackApp.Build().Run()`** is the first line of `MauiProgram.CreateMauiApp()`
  ([App/MauiProgram.cs](../App/MauiProgram.cs)). It handles Velopack's install/update/uninstall
  hooks and exits early during those, so it never reaches the UI.
- **On the Home screen**, `UpdateService` ([App/Services/UpdateService.cs](../App/Services/UpdateService.cs))
  asks Velopack's `UpdateManager` (pointed at a `GithubSource` for this repo) whether a newer
  **published, non-prerelease** release exists. If so, the "Update" banner appears; clicking it
  downloads the update (delta if available) and relaunches into the new version.
- **In the IDE / a portable run** `UpdateManager.IsInstalled` is `false`, so the check is skipped
  and the banner never shows. In-app update only works for a real Velopack install.

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
4. **Edit the description and publish, on GitHub.** Open
   [the releases page](https://github.com/Deusald/DeusaldLocalizer/releases), open the `v1.1.3`
   draft (drafts are visible only to you), write the description, and click **Publish** — keeping
   it a normal (non-prerelease) release. Only now do clients see it.
5. **Verify** by launching an already-installed older copy — the "Update" banner should appear on
   the Home screen and update to `1.1.3`.

### Where the description comes from

The draft's description body starts **empty** — you type it in the GitHub UI before publishing.
If you'd rather generate it from a file, pass `-ReleaseNotes path\to\notes.md` and its markdown
pre-fills the body (you can still edit it in the draft afterwards).

### Do I need to create/push the tag first?

**No.** `vpk upload` registers the `v1.1.3` tag with the draft, and GitHub creates the actual git
tag **when you click Publish**, pointing at the exact commit you built from (the script pins it via
`--targetCommitish`). Don't `git tag` or push a tag by hand — that would collide.

### What `build-release.ps1` produces in `dist\`

| Artifact | Purpose |
|---|---|
| `DeusaldLocalizer-win-Setup.exe` | The installer — this is what new users download. |
| `DeusaldLocalizer-<ver>-full.nupkg` | Full release package (also the delta base for next time). |
| `DeusaldLocalizer-<ver>-delta.nupkg` | Small diff vs the previous release (skipped on the first release). |
| `DeusaldLocalizer-win-Portable.zip` | Portable build (no installer / no auto-update). |
| `releases.win.json` | The update feed the app reads to discover new versions. |

**Upload the whole set** (the script does this for you). `releases.win.json` and the `.nupkg`
files are what the client actually reads and downloads — a release with only the `Setup.exe` will
**not** auto-update anyone.

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

## Gotchas

- **Never delete or re-tag a published release's assets** once clients have seen them — Velopack
  reads `releases.win.json` and the `.nupkg` files by name. Removing them breaks in-place updates.
- **Publish, don't leave as draft** — draft and pre-release releases are invisible to the updater
  (the `GithubSource` is configured with `prerelease: false`).
- **Version must increase.** Re-publishing the same version does nothing for existing installs.
- **The very first Velopack release** has no previous package to diff against, so there's no delta
  and the "previous release" download step logs a harmless warning — that's expected.
- Users on the **old zip-based builds** must install `Setup.exe` once to move onto the auto-updating
  channel; there's no automatic migration from a hand-extracted zip.

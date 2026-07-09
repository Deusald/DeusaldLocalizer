# Releasing & Updates

How Deusald Localizer ships updates, and the exact steps to cut a new release.

Updates work **differently per platform**:

- **Windows — auto-update in place.** Powered by [Velopack](https://velopack.io). Users install once
  (`DeusaldLocalizer-win-Setup.exe`); the app then checks GitHub Releases on launch and updates
  **itself** in place.
- **macOS — notify + manual download.** Velopack's in-place update is unsupported under Mac Catalyst,
  so the Mac app does **not** update itself. It checks GitHub on launch and, when a newer release
  exists, shows a **Download** button that opens the releases page so the user grabs the new build by
  hand (download the `.zip`, replace the app).

Both platforms still ship from a **single** GitHub release per version, built by separate scripts on
their own OS ([scripts/build-release.ps1](../scripts/build-release.ps1) on Windows,
[scripts/build-release-mac.sh](../scripts/build-release-mac.sh) on macOS); their artifacts coexist on
the same release.

## How it works

### Windows (Velopack, in place)

- **Install once.** Users run `DeusaldLocalizer-win-Setup.exe` (installs to
  `%LocalAppData%\DeusaldLocalizer`, creates Start-menu / desktop shortcuts, no admin rights).
- **`VelopackApp.Build().Run()`** is the first line of `MauiProgram.CreateMauiApp()`
  ([App/MauiProgram.cs](../App/MauiProgram.cs)), compiled **Windows-only** (`#if WINDOWS`). It handles
  Velopack's install/update/uninstall hooks and exits early during those, so it never reaches the UI.
- **On the Home screen**, `UpdateService` ([App/Services/UpdateService.cs](../App/Services/UpdateService.cs))
  asks Velopack's `UpdateManager` (pointed at a `GithubSource` for this repo) whether a newer
  **published, non-prerelease** release exists. If so, the **Update** banner appears; clicking it
  downloads the update (delta if available) and relaunches into the new version.
- **In the IDE / a portable run** `UpdateManager.IsInstalled` is `false`, so the check is skipped and
  the banner never shows. In-app update only works for a real Velopack install.

### macOS (GitHub check, manual download)

- **No Velopack.** `VelopackApp.Run()` is not compiled into the Mac build, and there is no `.pkg`,
  `.nupkg`, or update feed.
- **On the Home screen**, the macOS branch of `UpdateService`
  ([App/Services/UpdateService.cs](../App/Services/UpdateService.cs)) calls the GitHub API
  (`/releases/latest`) and compares the release's `tag_name` to the running build version
  (`BuildInfo.Version`). If GitHub's latest is strictly newer, the **Download** banner appears;
  clicking it opens the release's page in the browser (`Launcher.OpenAsync`) so the user downloads the
  new `.zip` and replaces the app themselves.
- **Same-version / dev runs show nothing.** The banner only appears when a *newer* release than the
  installed build exists, so you won't see it running at the current version.

## Version rule

The version comes straight from `<Version>` in [App/App.csproj](../App/App.csproj). The build scripts
read it, and the app's welcome screen shows `v<Version> · <git hash>`. **Bump `<Version>` for every
release** (SemVer, e.g. `1.1.2` → `1.1.3`).

- Windows clients only update to a strictly higher version.
- The Mac app compares GitHub's latest tag (`v<Version>`) to its own `BuildInfo.Version`, so the tag
  you release under **must** match the `<Version>` the Mac build was compiled with.

`<ApplicationDisplayVersion>` should match; `<ApplicationVersion>` is a monotonic integer — bump it too.

## Windows channels

Windows uses Velopack's default **`win`** channel (passed implicitly), which names artifacts without a
channel token (`DeusaldLocalizer-<ver>-full.nupkg`, `releases.win.json`). macOS no longer uses Velopack
at all, so there is **no macOS channel** — its release asset is just a plain `.zip` (see
[artifacts](#what-the-build-scripts-produce-in-dist)), which can't collide with the Windows names.

> **Never rename the `win` channel.** Every shipped Windows install is pinned to the channel it came
> from; renaming it silently orphans every existing install (it keeps polling the old `releases.win.json`
> feed, which you stop updating). Only introduce a *new* channel when you add a *new* Windows target
> (e.g. a future `win-arm64`), never as a rename.

## macOS entitlements & signing

A direct-download desktop app should **not** be sandboxed, so [scripts/mac-entitlements.plist](../scripts/mac-entitlements.plist)
(hardened-runtime allowances, **no App Sandbox**) is the default for **every** build — local, Debug, and
release alike. `App.csproj` sets it via `CodesignEntitlements` for the `maccatalyst` target, overriding
the Mac Catalyst SDK convention that would otherwise auto-apply
[App/Platforms/MacCatalyst/Entitlements.plist](../App/Platforms/MacCatalyst/Entitlements.plist) and
enable the sandbox. `build-release-mac.sh` also passes it explicitly (`-p:CodesignEntitlements=...`).

The sandboxed `App/Platforms/MacCatalyst/Entitlements.plist` is kept **only** for a future Mac App Store
build (it enables the App Sandbox and grants the user-selected-files entitlement the folder picker needs
under the sandbox). To produce a sandboxed build, override the default:
`-p:CodesignEntitlements=Platforms/MacCatalyst/Entitlements.plist`. Do not sandbox local/Debug builds —
the sandbox reproduces bugs (security-scoped file-access denials from the folder picker) that never occur
in the shipped, non-sandboxed app.

By default the build is **unsigned** (ad-hoc), so macOS Gatekeeper quarantines it on other Macs. After
downloading + unzipping, the recipient clears it once:
```bash
xattr -dr com.apple.quarantine "/path/to/Deusald Localizer.app"
```
(or right-click the app → **Open** the first time). Fine for testing / small distribution. For public
distribution, sign + notarize the `.app` before zipping (Developer ID + `xcrun notarytool` +
`xcrun stapler staple`) — intentionally left out of the script.

### Self-signed signing (makes SecureStorage / sign-in work)

MAUI `SecureStorage` on Mac Catalyst uses the iOS-style **data-protection Keychain**, which needs the
app to carry a *stable code signature* **and** a `keychain-access-groups` entitlement. An **ad-hoc**
signature has neither, so every Keychain write fails with `errSecMissingEntitlement (-34018)`. In the app
that shows up as: connecting to a remote project works (the token is still in memory), but the next
**sync/push** reports *"you must be authenticated"* because the token was never persisted. (As a safety
net `MauiAuthTokenStore` falls back to plaintext `Preferences` when the Keychain is unavailable, so an
ad-hoc build still works — it just doesn't use the secure store.)

To build with the real Keychain, sign with a **self-signed code-signing certificate**:

1. **One-time, per machine** — create the identity (default name `Deusald Localizer Self-Signed`):
   ```bash
   ./scripts/create-mac-signing-cert.sh
   ```
   It generates a self-signed code-signing cert in your **login keychain**, authorizes `codesign` to use
   its private key, and trusts it for code signing (expect one admin prompt). Verify with
   `security find-identity -v -p codesigning`.
2. **Build signed** — pass the identity (or set `MAC_SIGN_IDENTITY`):
   ```bash
   ./scripts/build-release-mac.sh --sign-identity "Deusald Localizer Self-Signed"
   ```
   The script publishes, then re-signs the `.app` with that identity and
   [scripts/mac-entitlements-signed.plist](../scripts/mac-entitlements-signed.plist) (the usual
   hardened-runtime allowances **plus** the `com.deusald.localizer` keychain access group — no team-ID
   prefix, because a self-signed cert has none). It verifies the signature and that the keychain group is
   present.

A self-signed cert is **not** a Developer ID and is **not** notarized, so Gatekeeper still quarantines the
app on other Macs (recipients clear quarantine exactly as above). Reuse the **same** cert across rebuilds:
signing with a *different* key changes the code signature the Keychain binds items to, so previously
stored tokens become unreadable and users must sign in again (the `Preferences` fallback covers that).

## One-time setup (per machine)

- **.NET / MAUI workload** — as for any build (`dotnet workload install maui`).
- **Windows only — Velopack CLI (`vpk`)** — the Windows script auto-installs it, or do it yourself:
  ```powershell
  dotnet tool install -g vpk
  ```
  (Update later with `dotnet tool update -g vpk`.) Make sure `%USERPROFILE%\.dotnet\tools` is on PATH.
- **macOS builds need a Mac.** MacCatalyst compilation requires macOS + Xcode; Mac artifacts **cannot**
  be cross-built from Windows. On the Mac: install Xcode + the maui workload. The Mac script needs **no**
  `vpk`, no GitHub token, and no network access — it only builds locally.
- **Windows — GitHub token** — a fine-grained PAT (repo = DeusaldLocalizer, **Contents: Read and write**),
  used by `build-release.ps1` to create/upload the draft release and pull the previous release for delta
  generation. The script resolves it in this order:
  1. `-Token <value>` parameter, then
  2. `$env:GITHUB_TOKEN`, then
  3. **1Password** — read via the `op` CLI from `op://Private/GitHub Deusald Localizer Token/credential`
     (the `$opTokenRef` constant in the script). Pulled only when `-Upload` is set.

  With 1Password you don't set anything per shell — just be **signed in to the `op` CLI**
  (`op signin`, or enable *Developer → Integrate with 1Password CLI* in the desktop app). Verify with:
  ```powershell
  op read "op://Private/GitHub Deusald Localizer Token/credential"
  ```
  (The macOS script uploads nothing, so it needs no token.)

## Steps for every release

1. **Bump the version** in [App/App.csproj](../App/App.csproj): `<Version>`,
   `<ApplicationDisplayVersion>` (match), and `<ApplicationVersion>` (+1).
2. **Commit** the bump (the app embeds the commit hash, so build from a clean commit):
   ```powershell
   git add App/App.csproj
   git commit -m "version(App): bump to 1.1.3"
   ```
3. **Build, package, and upload a Windows draft**:
   ```powershell
   ./scripts/build-release.ps1 -Upload
   ```
   This publishes a self-contained win-x64 build, downloads the previous release (for a small delta),
   packs the Velopack artifacts into `dist\`, then creates a GitHub **draft** release `v1.1.3` with the
   Windows artifacts attached. **The script never goes live on its own.**

   (Run with no switch to pack into `dist\` only and touch nothing on GitHub — useful for testing
   `Setup.exe` locally first.)
4. **Build the macOS asset (on a Mac).** From a clone at the *same commit*, on an Apple Silicon Mac:
   ```bash
   ./scripts/build-release-mac.sh
   ```
   This publishes a MacCatalyst arm64 build, zips it, and writes a checksum into `dist/`:
   `DeusaldLocalizer-maccatalyst-arm64-1.1.3.zip` and `…-1.1.3.zip.sha256`. It does **not** touch
   GitHub.
5. **Attach the macOS files to the same draft, on GitHub.** Open the `v1.1.3` draft the Windows step
   created, and **drag both Mac files** (`.zip` + `.sha256`) onto it so Windows and macOS ship from one
   release.
6. **Edit the description and publish, on GitHub.** On
   [the releases page](https://github.com/Deusald/DeusaldLocalizer/releases), open the `v1.1.3` draft,
   write the description, and click **Publish** — keeping it a normal (non-prerelease) release. Only now
   do clients see it (Windows auto-update; macOS "Download" banner both key off a **published,
   non-prerelease** release).
7. **Verify** by launching an already-installed older copy on each platform:
   - **Windows:** the **Update** banner appears and updates in place to `1.1.3`.
   - **macOS:** the **Download** banner appears and clicking it opens the `v1.1.3` releases page.

### Where the Windows description comes from

The draft's description body starts **empty** — type it in the GitHub UI before publishing. To generate
it from a file, pass `-ReleaseNotes path\to\notes.md` to `build-release.ps1` and its markdown pre-fills
the body (still editable in the draft).

### Do I need to create/push the tag first?

**No.** `vpk upload` (Windows) registers the `v1.1.3` tag with the draft, and GitHub creates the actual
git tag **when you click Publish**, pointing at the commit you built from (pinned via `--targetCommitish`).
Don't `git tag` or push a tag by hand. Build the Mac asset from the *same commit* so its embedded git
hash matches.

### What the build scripts produce in `dist\`

**Windows** (`build-release.ps1`):

| Artifact | Purpose |
|---|---|
| `DeusaldLocalizer-win-Setup.exe` | The installer — this is what new users download. |
| `DeusaldLocalizer-<ver>-full.nupkg` | Full release package (also the delta base for next time). |
| `DeusaldLocalizer-<ver>-delta.nupkg` | Small diff vs the previous release (skipped on the first release). |
| `DeusaldLocalizer-win-Portable.zip` | Portable build (no installer / no auto-update). |
| `releases.win.json` | The update feed the app reads to discover new versions. |

Upload the whole Windows set (the script does this for you): the `releases.win.json` feed and the
`.nupkg` files are what the client reads and downloads — a release with only the installer will **not**
auto-update anyone.

**macOS** (`build-release-mac.sh`):

| Artifact | Purpose |
|---|---|
| `DeusaldLocalizer-maccatalyst-arm64-<ver>.zip` | The whole app, zipped — what Mac users download and run. |
| `DeusaldLocalizer-maccatalyst-arm64-<ver>.zip.sha256` | Checksum, verify with `shasum -a 256 -c …`. |

Upload **both** Mac files by hand (step 5). The Mac app finds a new version via the GitHub *release
tag*, not via any feed, so only the `.zip` needs to be attached for users to download.

## Gotchas

- **Never delete or re-tag a published Windows release's assets** once clients have seen them —
  Velopack reads `releases.win.json` and the `.nupkg` files by name. Removing them breaks in-place
  updates. (Removing the Mac `.zip` just makes that version un-downloadable.)
- **Never rename the `win` channel** — it strands every existing Windows install. See
  [Windows channels](#windows-channels).
- **macOS builds are unsigned** — Gatekeeper quarantines them; recipients clear it with
  `xattr -dr com.apple.quarantine "…/Deusald Localizer.app"`. Sign + notarize before any public macOS
  distribution. See [macOS entitlements & signing](#macos-entitlements--signing).
- **Build both from the same commit** so the embedded git hash matches. Order doesn't matter beyond
  that: Windows creates the draft; the Mac files are dragged onto it.
- **Publish, don't leave as draft** — draft and pre-release releases are invisible to both update
  checks (Windows `GithubSource` uses `prerelease: false`; the Mac check uses `/releases/latest`, which
  excludes drafts and prereleases).
- **Version must increase.** Re-publishing the same version does nothing — Windows won't update, and the
  Mac check compares strictly-greater.
- **The very first Windows Velopack release** has no previous package to diff against, so there's no
  delta and the "previous release" download step logs a harmless warning — that's expected.
- Users on **old zip-based Windows builds** must install `Setup.exe` once to move onto the auto-updating
  channel; there's no automatic migration from a hand-extracted zip.

## Testing the Windows update flow locally (no GitHub)

You can exercise the full **install → detect → download → apply → restart** cycle on Windows.
`UpdateService` honours a `DEUSALD_UPDATE_SOURCE` environment variable (Windows build only): when set,
it updates from that **local folder** (or URL) instead of GitHub. Unset in production, so it's inert for
real users.

> Auto-update only works from a *real* install (`UpdateManager.IsInstalled`). Running from the IDE won't
> apply updates — you must install via `Setup.exe`.

1. **Pack the current version** (say `1.1.2`) into `dist\`:
   ```powershell
   ./scripts/build-release.ps1                     # no -Upload → local only, no GitHub call
   ```
2. **Install it** — run `dist\DeusaldLocalizer-win-Setup.exe` (installs to `%LocalAppData%\DeusaldLocalizer`).
3. **Make a newer version** and pack it into the **same** `dist\` (vpk builds a delta from `1.1.2` and
   updates `releases.win.json`). Bump `<Version>` to `1.1.3` in [App/App.csproj](../App/App.csproj)
   first — the script reads it automatically.
   ```powershell
   ./scripts/build-release.ps1
   ```
4. **Launch the installed app pointed at `dist\`** (launching from this shell makes it inherit the env
   var):
   ```powershell
   $env:DEUSALD_UPDATE_SOURCE = (Resolve-Path .\dist).Path
   & "$env:LOCALAPPDATA\DeusaldLocalizer\current\DeusaldLocalizer.exe"
   ```
   The running `1.1.2` finds `1.1.3` in `dist\`, shows the **Update** banner; clicking it downloads the
   delta, applies, and relaunches into `1.1.3`.
5. **Clean up** when done:
   ```powershell
   Remove-Item Env:\DEUSALD_UPDATE_SOURCE
   & "$env:LOCALAPPDATA\DeusaldLocalizer\Update.exe" --uninstall
   ```

> There is **no equivalent local test on macOS** — the Mac app just opens the GitHub releases page in a
> browser, so the only thing to verify is that the **Download** banner appears when GitHub's latest
> release is newer than the running build.

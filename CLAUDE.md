# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Deusald Localizer is a community localization tool for games: translators edit strings, propose alternatives, and vote on the best translation per key. It ships as **two clients that share one UI** — a MAUI desktop app and a Blazor WebAssembly web app. Projects are stored as a "folder of files" (JSON, one per entity) and can be worked on offline or synced against a backend git bot.

## Solution layout

Five projects (`DeusaldLocalizer.sln`), all on **.NET 10**:

- **Common** — shared class library (`DeusaldLocalizerCommon` namespace): all domain models (`Loc*`), persistence, business services, and the sync/conflict logic. Multi-targets `netstandard2.1;net10.0`, pinned to **`LangVersion` 9** with `ImplicitUsings` **disabled** — files here need explicit `using`s and cannot use newer C# syntax. Consumed by everything, including any external reader (e.g. a Unity importer).
- **WebCommon** — Razor class library (assembly `DeusaldLocalizerWebCommon`, **`RootNamespace` `DeusaldLocalizerWeb`**): **all the Blazor editor UI**, `ProjectStateService`, `LocalizerApiClient`, and the **platform-abstraction interfaces**. Shared by both clients. References Common (+ DeusaldSharp).
- **App** — .NET MAUI Blazor Hybrid **desktop** client (Windows / MacCatalyst). Just a shell + MAUI implementations of the platform interfaces; the UI comes from WebCommon.
- **WebApp** — Blazor **WebAssembly PWA** web client (assembly `DeusaldLocalizerWeb`), deployable to GitHub Pages. Shell + browser implementations of the platform interfaces; UI from WebCommon.
- **Backend** — ASP.NET Core Web API (`DeusaldLocalizerBackend`), a git "bot" serving the online sync/push protocol (last section). Controllers in `Backend/Controllers/`, sync in `Backend/Sync/`, git in `Backend/Git/`.

Reference graph: `App → WebCommon → Common`, `WebApp → WebCommon → Common`, `Backend → Common`.

## Commands

```bash
# Build everything
dotnet build DeusaldLocalizer.sln

# Build just the shared library (fast, no MAUI/WASM workload needed)
dotnet build Common/Common.csproj

# Run the desktop app on Windows (needs the maui workload: `dotnet workload install maui`)
dotnet build App/App.csproj -t:Run -f net10.0-windows10.0.19041.0

# Run the web app (dev server; base href '/'). Also F5-able from the IDE via launchSettings.
dotnet run --project WebApp

# Publish the web app to static files (no wasm-tools workload required for a standard publish)
dotnet publish WebApp -c Release -o publish

# Run the backend API
dotnet run --project Backend
```

There are **no test projects**, so there is no test command. To verify web behaviour end-to-end, drive the dev server headlessly via Edge + the DevTools Protocol (Node 22 has global `fetch`/`WebSocket`; the private npm registry blocks puppeteer).

## Domain model

`LocProject` (`Common/Data/_LocProject.cs`) is the aggregate root: `Metadata`, `ProjectMembers`, `Categories`, `Enums`, `UncommitedChanges`, `Keys`. Each key has `Translations` (per language) plus tags/flags/variables. Data classes in `Common/Data/`, enums in `Common/Enums/`.

**Online vs offline** is derived, not stored: `LocProjectMetadata.IsOnline` is just `ApiUrl` being non-empty. This drives the save strategy.

**Users:** there is always a `CurrentUser`; with no real login it is `LocProjectMember.OfflineMember` (fixed GUID, `IsAdmin = true`). Check `IsOfflineUser` rather than comparing GUIDs.

## Persistence: the store abstraction (the big picture)

The whole design is "a folder of files keyed by a relative path", so persistence is abstracted at that level and the **same logic runs on disc and in the browser**:

- **`IProjectFileStore`** (`Common/Services/`) — a flat `path → content` map (`FileExists/ReadText/WriteText/DeleteFile/ListJsonFiles`, paths '/'-separated). Two implementations:
  - **`DiscProjectFileStore`** (Common) — a real disc folder; used by App and Backend.
  - **`IndexedDbProjectFileStore`** (WebApp) — IndexedDB records via `wwwroot/js/idb.js`; the browser analogue of a project folder.
- **`ProjectFileService`** (`Common/Services/`) holds all the folder/ordering/zero-padding logic and runs **entirely over `IProjectFileStore`**. Its `string folderPath` overloads just wrap a `DiscProjectFileStore`, so disc callers (App, Backend) are unchanged. Layout: `metadata.json`, `Members/ Categories/ Enums/ Keys/` (`{guid}.json`), `UncommittedChanges/` (zero-padded `0000.json…`, numeric order). Every save mints a new `SyncId` and bumps `UpdatedAt`.

Three save paths, chosen in `ProjectStateService.SaveAsync()` by online/offline state (all now take a store):
- `SaveAsync` — full rewrite, deletes files for removed entities.
- `SaveIncrementalAsync` — offline; writes only keys in `ChangedLocKeys`, prunes deleted keys.
- `SaveUncommittedOnlyAsync` — online; persists only pending changes, leaving key files untouched until the bot confirms (then `ClearUncommittedChangesAsync`).

**`EntryChangeExeService.ExecuteChange`** replays one `LocEntryChange` onto a project (a big switch over `EntryChangeType`) and yields a `commitString`. It runs over `UncommitedChanges` on load (so both clients reconstruct state as *committed base + replayed pending changes*) and drives the Backend's per-change commits.

## Session state & platform abstractions

- **`ProjectStateService`** (`WebCommon/Services/`) owns all session state — open project, current user, dirty flag, changed key IDs, sync conflicts. Registered **singleton** in MAUI (`MauiProgram.cs`), **scoped** in WASM (`Program.cs`, one-per-app). Components inject it and subscribe to three events:
  - `ProjectChanged` — project loaded/closed/created.
  - `DirtyStateChanged` — `IsDirty` flipped.
  - `ProjectDataChanged` — fires on **every** `MarkDirty()` even when already dirty; components showing derived data (progress bars) must use this, not `DirtyStateChanged`.
- `CurrentProjectPath` is an **opaque location handle**, not necessarily a disc path: a folder path on MAUI, a minted IndexedDB namespace GUID on the web. `ProjectStateService` turns it into a store via `IProjectStoreFactory.Create(location)`.
- The service depends only on abstractions, each implemented per host:

  | Interface (WebCommon) | MAUI impl (`App/Services/Platform`) | Web impl (`WebApp/Services`) |
  |---|---|---|
  | `IProjectStoreFactory` | `DiscProjectStoreFactory` | `IndexedDbProjectStoreFactory` |
  | `IAuthTokenStore` | `MauiAuthTokenStore` (SecureStorage) | `LocalStorageAuthTokenStore` |
  | `IPreferencesStore` | `MauiPreferencesStore` (Preferences) | `LocalStoragePreferencesStore` |
  | `IProjectLocationService` | `MauiProjectLocationService` (FolderPicker) | `WebProjectLocationService` (mints a GUID) |
  | `IExcelInterop` | `MauiExcelInterop` (FilePicker/FileSaver) | `WebExcelInterop` (file input + download) |

  `RecentProjectsStore` (WebCommon) is shared logic over `IPreferencesStore` + `IAuthTokenStore`.

## UI structure

- **Shared** (WebCommon/Components): the whole editor. Feature components live under `Components/Common/**` but all declare the **flat** namespace **`DeusaldLocalizerWeb.Components.Common`** (folder does not derive the namespace — a project convention). Layouts are `DeusaldLocalizerWeb.Components.Layout`, the `Translate` page (`/translate`, the three-column `LanguagesColumn | KeysColumn | KeyDetailColumn`, `EmptyLayout`) is `DeusaldLocalizerWeb.Components.Pages`. Each `.razor` has a co-located scoped `.razor.css`.
- **Host-specific**: `Routes`, `NotFound`, and the **`Home` project picker** live in each client, because they differ by platform — App's `Home` uses the native folder picker + Velopack updates; WebApp's `Home` lists IndexedDB projects and does new / open / import-zip / export / delete. Each host's `Router` sets `AdditionalAssemblies` to WebCommon so the shared `/translate` route is discovered.
- The only JS interop the shared components use is `copyToClipboard`; other browser features (IndexedDB, zip, file pick) sit behind the platform interfaces. Web zip export/import (`WebProjectArchive`) uses `System.IO.Compression` and round-trips the **exact desktop project-folder format**, so a project moves between clients (and the desktop app) as a `.zip`. `IndexedDbInterop` calls `navigator.storage.persist()` so offline projects and pending changes are not evicted.

> Web **online onboarding** (connect to a remote repo → login → FullResync bootstrap into IndexedDB) is **not built yet** — WebApp's `Home` covers offline/local projects; the sync/push plumbing underneath is shared and ready.

## Web deployment (GitHub Pages)

`.github/workflows/deploy-pages.yml` builds only WebApp (+ WebCommon + Common — no MAUI) and deploys to `https://deusald.github.io/DeusaldLocalizer/`. It rewrites `<base href="/">` → `/DeusaldLocalizer/` in the **source** `index.html` before publish (so the service-worker asset manifest hashes the final file); ships `.nojekyll` (keeps `_framework/`) and a `404.html` SPA fallback. One-time: repo **Settings → Pages → Source = GitHub Actions**. See `docs/WebApp-Plan.md`.

## Notable libraries

- **SmartFormat** — `VariablePreviewService` renders translation previews with named variables.
- **ClosedXML** — Excel `.xlsx` import/export (`LocalizationImportService` / `LocalizationExportService`). Heavy in WASM — treat web Excel as untested.
- **BCrypt.Net-Next** — hashing access tokens (`HashedAccessToken`).
- **Newtonsoft.Json** with `StringEnumConverter` — the project-file serializer (enums as strings). Note: Newtonsoft, not `System.Text.Json` (the API client `LocalizerApiClient` does use `System.Text.Json`).
- **Velopack** — desktop auto-update (Windows in-place; macOS manual download).

## Conventions

- C# style follows the user's global rules (column-aligned declarations/assignments; `_PascalCase` private members). The solution's `.DotSettings` (ReSharper/Rider) enforces alignment — keep new code visually aligned.
- When adding code to **Common**, remember the C# 9 / no-implicit-usings constraint.
- `graphify-out/` is generated knowledge-graph output (graphify skill), not source — don't edit by hand.

## How the Backend bot's online git sync/push protocol works (SyncId-in-commit, never-merge)
The `Backend` project is a git "bot": it holds a local clone per project and is the only thing that runs git. Online clients download a GitHub repo (no local git) and talk to it over HTTP.

**Protocol** (`Backend/Sync/`, DTOs in `Common/Data/LocSyncContracts.cs`):
- `SyncId` (in `LocProjectMetadata`) is the version token. Every commit of a push batch carries `SyncId: <guid>` in its message (`SyncTag.For`), mirrored into `metadata.json`. The bot locates "the version a client last saw" with `git log --grep`.
- **Sync** (`SyncService`): fetch+reset to `origin/<branch>`, find the commit for the client's SyncId, return `git diff --name-status` file contents (or `FullResync` if unknown, `UpToDate` if HEAD).
- **Push** (`PushService`): fetch+reset, validate each change against the freshly-pulled state via `EntryChangeConflictService` (defense-in-depth), then apply each change as **one commit** (`EntryChangeExeService.ExecuteChange` → `commitString`; author = member's `Username`, committer = bot), folding the metadata SyncId bump into the batch's **last** change commit (its own commit only if the batch has no changes), `git push`. A plain push (no force, no merge) is **rejected if the remote moved during processing** → `git reset --hard` discard, return `Failed`. The bot **never merges**.
- Per-project **serialization** via `ProjectSerializer` (SemaphoreSlim per projectId): linear per project, parallel across projects. Required for git work-tree safety too.

**Conflict detection** is shared (`Common/Services/EntryChangeConflictService.cs`), used by both client (after sync, blocks push) and server (defense-in-depth). Only `TranslationUpdated` conflicts: `SourceChanged` (source drifted) vs `PrevSourceHashData`, `DestChanged` (concurrent edit) vs `PrevDestHashData`. Validate against a pristine baseline, skipping the dest-check for a (key,lang) the batch already touched (multi-edit chains). See [[no-comment-block-under-separators]] for style.

Auth: headers `Authorization: Bearer <raw-token>` + `X-User-Id`, verified via `AccessTokenService.VerifyToken` (BCrypt) against the member's `HashedAccessToken`.

Config: `Bot` section (`BotOptions`) lists projects (`ProjectId`/`Slug`/`RemoteUrl`/`Branch`) + `ReposRoot`. GitHub push credential goes in the `RemoteUrl` (PAT over HTTPS). Runs in Docker (`Backend/Dockerfile` installs git; `docker-compose.yml`), target DigitalOcean Droplet with a persistent volume for `ReposRoot`.

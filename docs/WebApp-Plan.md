# Web App Plan — Blazor WebAssembly client

Goal: ship a **Blazor WebAssembly** client that edits **remote (online) localizations** the
same way the MAUI desktop app does, installable as an offline PWA and hostable on **GitHub Pages**.
The desktop app stays; the two clients share their entire UI and client logic.

## Why this is possible without touching the sync protocol

Online mode never needs the real filesystem. State is always reconstructed as
**downloaded base + replayed uncommitted changes** (`EntryChangeExeService.ExecuteChange` already
runs over `UncommitedChanges` on load). The web client does the same thing; only the *backing store*
differs — **IndexedDB instead of a disc folder**. Because the whole design is "a folder of JSON files
keyed by relative path," IndexedDB is a near drop-in: a `path → content` map.

## Target project graph

```
Common  (netstandard2.1;net10.0, C# 9, no implicit usings)   ← shared by ALL, incl. Unity/library readers
  Data/, Enums/, Loc* models
  Services/ business logic (EntryChange*, Conflict, VariablePreview, Culture, TextHash, AccessToken, Import/Export)
  IProjectFileStore              (NEW — path→content abstraction)
  ProjectFileService             (refactored: instance methods over IProjectFileStore)
  DiscProjectFileStore           (NEW — System.IO impl; used by MAUI, Backend, external readers)
  NO MAUI / NO Blazor / NO browser-only deps

WebCommon  (net10.0, Microsoft.NET.Sdk.Razor)                 ← shared by MAUI App + WebApp
  All Blazor components (moved from App/Components/**)
  ProjectStateService, LocalizerApiClient
  Platform abstractions (interfaces): IAuthTokenStore, IPreferencesStore, IProjectLocationService
  References Common

App  (net10.0-windows / net10.0-maccatalyst, MAUI)           ← desktop client (unchanged behaviour)
  MAUI shell (MainPage, App.xaml, MauiProgram)
  MAUI impls: DiscProjectFileStore wiring, MauiAuthTokenStore (SecureStorage),
              MauiPreferencesStore (Preferences), MauiProjectLocationService (FolderPicker),
              UpdateService (Velopack), native JS
  References WebCommon (+ Common)

WebApp  (net10.0, Microsoft.NET.Sdk.BlazorWebAssembly, PWA)   ← NEW web client
  wwwroot: index.html, manifest, service worker, IndexedDB JS
  Web impls: IndexedDbProjectFileStore, LocalStorageAuthTokenStore, LocalStoragePreferencesStore,
             WebProjectLocationService (no OS picker — project id namespaces in IndexedDB),
             zip export/import for "download a local copy"
  References WebCommon (+ Common)

Backend  (ASP.NET Core)                                       ← unchanged behaviour; updated to instance ProjectFileService
```

## Phases

1. **Scaffold** WebCommon + WebApp, wire the solution, verify empty build.
2. **Persistence abstraction** in Common: `IProjectFileStore` (path→content), refactor `ProjectFileService`
   to instance methods over it, add `DiscProjectFileStore`. Update App + Backend call sites. Build all.
3. **Share the client layer**: move all Blazor components + `ProjectStateService` + `LocalizerApiClient`
   into WebCommon; abstract the platform services (`IAuthTokenStore`, `IPreferencesStore`,
   `IProjectLocationService`); App references WebCommon and provides the MAUI implementations. Build App.
4. **WebApp host**: `index.html`, `_Imports`, `Program.cs`, DI wiring; IndexedDB JS + `IndexedDbProjectFileStore`;
   localStorage auth/prefs; zip export/import; PWA manifest + service worker. `navigator.storage.persist()`.
5. **GitHub Pages**: `.nojekyll`, correct `<base href>`, `404.html` = index.html, GitHub Action to publish.

## Key decisions / risks

- **Abstract persistence at the raw-store level** (`ExistsAsync/ReadTextAsync/WriteTextAsync/DeleteAsync/ListAsync`
  by relative path). Keeps all folder/ordering/zero-padding logic in `ProjectFileService` unchanged; the
  `.tmp`+rename dance becomes a no-op in the (atomic) IndexedDB impl.
- **`CurrentProjectPath` becomes an opaque location handle**: a disc path on MAUI, a project-id namespace on web.
  A store factory turns a location into an `IProjectFileStore`.
- **No OS folder picker on web.** Online projects are identified by project id; offline projects live in
  IndexedDB and are moved to/from disc via **zip export/import**, not a native picker.
- **Durability**: call `navigator.storage.persist()`. Uncommitted changes and offline projects are the only
  non-recoverable data — export button is their backup. Reuse the existing dirty-state to warn before close.
- **WASM dependency watch**: `ClosedXML` (Excel import/export) is heavy in the browser — consider excluding
  Excel from web v1. `BCrypt.Net-Next`, `SmartFormat`, `Newtonsoft.Json` are fine in WASM.
- **`wasm-tools` workload** is required for `publish` (not installed yet).
- **App updates**: PWA service worker downloads updates in the background; they apply on the next launch
  (or immediately with an update-available prompt).

## Deployment (GitHub Pages)

`.github/workflows/deploy-pages.yml` builds **only** WebApp (+ WebCommon + Common — no MAUI workload) and
publishes it to Pages on every push to `main` that touches those projects (or via manual dispatch).

- **Base path**: the site serves from `https://deusald.github.io/DeusaldLocalizer/`, so the workflow rewrites
  `<base href="/">` → `<base href="/DeusaldLocalizer/">` in the source `index.html` **before** `dotnet publish`
  (so the service-worker asset manifest hashes the final file). The `BASE_PATH` env var is the single knob —
  change it if the repo is renamed, or set it to `/` for a custom domain.
- **`.nojekyll`** ships in `wwwroot` and is re-touched in CI so Pages does not strip `_framework/`.
- **`404.html`** is a copy of the published `index.html` — GitHub Pages' SPA deep-link fallback.
- **No `wasm-tools` workload** is needed: a standard (non-AOT) publish only restores the wasm runtime pack.

**One-time manual step**: in the repo, **Settings → Pages → Source = GitHub Actions**. Until that is set the
`deploy-pages` job cannot publish.

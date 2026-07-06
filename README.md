# Deusald Localizer

A community localization tool for games. Translators edit strings, propose alternative
translations, and vote on the best one per key — working offline against local files or
online against a Git-backed backend, with no database and no lock-in: **a project is just a
folder of JSON files in a Git repository.**

<!-- TODO: hero screenshot of the three-column translation editor -->
<p align="center">
  <img src="docs/screenshots/editor.png" alt="Translation editor" width="800">
</p>

---

## Table of contents

- [What it does](#what-it-does)
- [Screenshots](#screenshots)
- [Tech stack](#tech-stack)
- [Solution layout](#solution-layout)
- [How a project is stored](#how-a-project-is-stored)
- [Getting started (desktop app)](#getting-started-desktop-app)
- [Online sync protocol](#online-sync-protocol)
- [Backend setup](#backend-setup)
  - [Run locally](#run-the-backend-locally)
  - [Run in Docker](#run-the-backend-in-docker)
  - [Configuration reference](#configuration-reference)
- [Building from source](#building-from-source)
- [License](#license)

---

## What it does

Deusald Localizer is a desktop app built for translating game text collaboratively. The whole
UI is a three-column editor: **languages** on the left, the **key list** in the middle, and the
**key detail** on the right.

- **Per-key translation editing** — every localization key holds one translation per language,
  each with a status (untranslated / suggested / approved) and a character-length limit that
  the editor enforces.
- **Suggestions & voting** — translators propose alternative wordings for a key/language pair.
  Each suggestion collects votes, so the community can converge on the best translation instead
  of one person overwriting another's work.
- **Source-drift detection** — a translation stores a SHA-256 hash of the source text it was
  written against. When the source language string changes, dependent translations are flagged
  as "source changed" so they can be revisited.
- **Categories** — keys are organized into categories (folders) and can be filtered by category.
- **Tags & flags** — keys carry free-form tags (e.g. `ui`, `button`) for search/filter, plus
  structured workflow flags (with notes and authorship) to mark keys that need attention. The
  key list can be filtered by both.
- **Variables & live preview** — keys declare typed variables (including project-level enums)
  and the editor renders a live [SmartFormat](https://github.com/axuno/SmartFormat) preview so
  translators see plural/gender/conditional output as they type.
- **Project enums** — define named integer→string enums once and reference them from variables,
  enabling SmartFormat `choose`/conditional formatting across translations.
- **Excel import/export** — round-trip translations through `.xlsx` for external translators,
  with per-language / tag / flag filters on export.
- **Members & access tokens** — projects have members with per-language review permissions and
  an admin role; online access is authenticated with hashed access tokens.
- **Works offline or online** — a project with no API URL is purely local files; add an API URL
  and the app syncs against the backend Git bot (see below). The distinction is derived from the
  project, not a manual toggle.

## Screenshots


| Home / project picker | Translation editor | Suggestions & voting |
|---|---|---|
| ![Home](docs/screenshots/home.png) | ![Editor](docs/screenshots/editor.png) | ![Voting](docs/screenshots/voting.png) |

| Excel export | Variables & preview | Members & tokens |
|---|---|---|
| ![Export](docs/screenshots/export.png) | ![Variables](docs/screenshots/variables.png) | ![Members](docs/screenshots/members.png) |

## Tech stack

Everything targets **.NET 10**.

| Project | Type | Key libraries |
|---|---|---|
| **App** | .NET MAUI Blazor Hybrid desktop app (Windows / MacCatalyst) — the UI is Blazor in a `BlazorWebView`; MAUI is just the shell. | `CommunityToolkit.Maui` (native file/folder pickers, drag-drop) |
| **Common** | Shared class library (`DeusaldLocalizerCommon`): all domain models, file persistence, and business services. Multi-targets `netstandard2.1;net10.0`, pinned to **C# 9** with implicit usings **disabled**. | [SmartFormat](https://github.com/axuno/SmartFormat) (previews), [ClosedXML](https://github.com/ClosedXML/ClosedXML) (`.xlsx`), [BCrypt.Net-Next](https://github.com/BcryptNet/bcrypt.net) (token hashing), [Newtonsoft.Json](https://www.newtonsoft.com/json) (project files, enums as strings) |
| **Backend** | ASP.NET Core Web API (`DeusaldLocalizerBackend`) — the Git "bot" that mediates online sync/push. | `Microsoft.AspNetCore.OpenApi` |

`App` and `Backend` both reference `Common`.

## Solution layout

```
DeusaldLocalizer.sln
├── App/        .NET MAUI Blazor Hybrid desktop app  → references Common
├── Common/     Shared domain models + services      (netstandard2.1 / net10.0, C# 9)
└── Backend/    ASP.NET Core Git-sync bot            → references Common
```

`LocProject` is the aggregate root for one project, holding `Metadata`, `ProjectMembers`,
`Categories`, `Enums`, `UncommitedChanges`, and `Keys`. Each key owns its `Translations`
(per language), plus tags, flags, and variables.

## How a project is stored

A project is a **folder of JSON files**, one file per entity — no database:

```
my-project/
├── metadata.json               project name, languages, main language, SyncId, API URL
├── Members/{guid}.json
├── Categories/{guid}.json
├── Enums/{guid}.json
├── Keys/{guid}.json            one key per file (translations, suggestions, tags, flags…)
└── UncommittedChanges/0000.json…   pending changes queue (online mode)
```

Writes go to a `.tmp` sibling and are then renamed, so a project survives a mid-write crash.
Because a project is plain files, it lives naturally in a Git repository — which is exactly
what the backend uses.

## Getting started (desktop app)

**Prerequisites**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The MAUI workload: `dotnet workload install maui`
- Windows 10.0.17763.0+ (or macOS 15+ for MacCatalyst)

**Run the app (Windows)**

```bash
dotnet build App/App.csproj -t:Run -f net10.0-windows10.0.19041.0
```

From the home screen you can create a new local project, open an existing project folder, or
log in to an online project by pointing it at a backend API URL.

> **Try the sample project:** open the [`ExampleLoc/`](ExampleLoc/) folder from the home screen
> — a small offline "Example RPG" project (English / German / French / Polish) that exercises
> most features: categories, tags, workflow flags, SmartFormat variables and enums, competing
> suggestions with votes, and a key whose source text has drifted out of sync.

## Online sync protocol

Online mode never merges and needs no database — the backend is a **Git bot** and `SyncId` (a
GUID in `metadata.json`) is the version token:

- Every commit of a push batch carries `SyncId: <guid>` in its message. The bot finds "the
  version a client last saw" with `git log --grep`.
- **Sync** — the bot fetches and resets to `origin/<branch>`, diffs from the client's `SyncId`
  commit to `HEAD`, and returns the changed/deleted files (or a full resync if the id is
  unknown, or "up to date" if already at `HEAD`).
- **Push** — the bot fetches/resets, validates each change against the freshly-pulled state,
  applies each change as **one commit** (author = the member, committer = the bot), bumps the
  `SyncId` in a final commit, and does a plain `git push`. If the remote moved during
  processing the push is **rejected** (`git reset --hard`, no merge) and the client re-syncs.
- Work is **serialized per project** (parallel across projects) for Git work-tree safety.

Clients (the App) only download files over HTTP — they never run Git. The backend is the only
component that touches Git.

## Backend setup

The backend holds one working-tree clone per managed project under a repos root, and exposes:

| Method & route | Purpose |
|---|---|
| `POST /projects/{projectId}/sync` | Pull changes since the client's `SyncId` |
| `POST /projects/{projectId}/push` | Apply a batch of changes and push |
| `GET /health` | Liveness check → `{ "status": "ok" }` |

Auth travels in headers on `sync`/`push`:

```
Authorization: Bearer <raw-access-token>
X-User-Id: <member-guid>
```

The token is verified with BCrypt against the member's `HashedAccessToken` stored in the
project's `Members/`.

### Run the backend locally

**Prerequisites:** .NET 10 SDK and `git` on your `PATH`.

```bash
dotnet run --project Backend
```

By default it listens on `http://localhost:5114` (and `https://localhost:7088` with the
`https` profile — see `Backend/Properties/launchSettings.json`). OpenAPI is mapped in the
Development environment.

Configure the managed projects before syncing — either edit `Backend/appsettings.json` or,
preferably for secrets, use `appsettings.Development.json` / environment variables / user
secrets. A minimal config:

```jsonc
{
  "Bot": {
    "ReposRoot": "repos",
    "CommitterName": "Deusald Localizer Bot",
    "CommitterEmail": "bot@localizer",
    "Projects": [
      {
        "ProjectId": "00000000-0000-0000-0000-000000000000",
        "Slug": "my-game",
        "RemoteUrl": "https://<user>:<github-pat>@github.com/you/my-game-loc.git",
        "Branch": "main"
      }
    ]
  }
}
```

> **The GitHub push credential lives in `RemoteUrl`** — use a Personal Access Token over HTTPS
> (`https://<user>:<pat>@github.com/...`). Keep it out of source control; prefer environment
> variables or user secrets in real deployments.

Any config key can be supplied as an environment variable using the `__` separator, e.g.:

```bash
export Bot__ReposRoot=/data/repos
export Bot__Projects__0__ProjectId=00000000-0000-0000-0000-000000000000
export Bot__Projects__0__Slug=my-game
export Bot__Projects__0__RemoteUrl="https://user:pat@github.com/you/my-game-loc.git"
export Bot__Projects__0__Branch=main
dotnet run --project Backend
```

### Run the backend in Docker

The `Backend/Dockerfile` builds only `Common` + `Backend` (no MAUI workload needed), installs
`git` in the runtime image, listens on port **8080**, and defaults `Bot__ReposRoot` to
`/data/repos` — **mount a persistent volume there** so the per-project clones survive restarts.

Build (from the repo root, so the Docker context includes both `Common/` and `Backend/`):

```bash
docker build -f Backend/Dockerfile -t deusald-localizer-backend .
```

Run:

```bash
docker run -d --name localizer-backend \
  -p 8080:8080 \
  -v localizer-repos:/data/repos \
  -e Bot__CommitterName="Deusald Localizer Bot" \
  -e Bot__CommitterEmail="bot@localizer" \
  -e Bot__Projects__0__ProjectId="00000000-0000-0000-0000-000000000000" \
  -e Bot__Projects__0__Slug="my-game" \
  -e Bot__Projects__0__RemoteUrl="https://user:pat@github.com/you/my-game-loc.git" \
  -e Bot__Projects__0__Branch="main" \
  deusald-localizer-backend
```

**Or use the bundled `docker-compose.yml`** (repo root). It builds from `Backend/Dockerfile`,
publishes on port `8080`, persists clones in the `bot-repos` named volume, and reads project
config + GitHub credentials from a `.env` file. Copy the template and fill it in:

```bash
cp .env.example .env
#   PROJECT_0_ID=00000000-0000-0000-0000-000000000000
#   PROJECT_0_SLUG=my-game
#   PROJECT_0_REMOTE_URL=https://<github-user>:<github-pat>@github.com/<owner>/<repo>.git
#   PROJECT_0_BRANCH=main

docker compose up -d --build
```

The compose file runs with `ASPNETCORE_ENVIRONMENT=Production` and `restart: unless-stopped`.
The target deployment is a DigitalOcean Droplet with the `bot-repos` volume mounted at the
repos root so clones survive restarts and redeploys.

### Configuration reference

All settings live under the top-level **`Bot`** section.

| Key | Default | Description |
|---|---|---|
| `Bot:ReposRoot` | `repos` (Docker: `/data/repos`) | Root directory holding one working-tree clone per project. |
| `Bot:CommitterName` | `Deusald Localizer Bot` | Git committer name on every bot commit (the author is the member). |
| `Bot:CommitterEmail` | `bot@localizer` | Git committer email. |
| `Bot:Projects[]` | `[]` | The managed projects (below). |
| `Bot:Projects[].ProjectId` | — | GUID identifying the project (matches the client's project id). |
| `Bot:Projects[].Slug` | `""` | Human-readable slug. |
| `Bot:Projects[].RemoteUrl` | `""` | Git remote to clone/push. **Embed the GitHub PAT here** for HTTPS push. |
| `Bot:Projects[].Branch` | `main` | Branch the bot tracks. |

## Building from source

```bash
# Build the whole solution
dotnet build DeusaldLocalizer.sln

# Build just the shared library (fast — no MAUI workload needed)
dotnet build Common/Common.csproj

# Run the desktop app (Windows)
dotnet build App/App.csproj -t:Run -f net10.0-windows10.0.19041.0

# Run the backend API
dotnet run --project Backend
```

When contributing to **Common**, remember it is pinned to **C# 9** with implicit usings
disabled — files there need explicit `using` directives and cannot use newer C# syntax.

## License

Released under the [MIT License](LICENSE) — © 2026 Deusald.

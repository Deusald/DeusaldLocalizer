# Local Remote Setup

How to run the whole online sync/push protocol on one machine, with **no GitHub**: a
`file://` git repo standing in for the remote, the `Backend` bot serving it, and two app
folders acting as two different signed-in users.

This is the fastest way to exercise the never-merge push flow, conflict detection, and the
first-sign-in token rotation end to end.

## How the pieces fit together

- The **bot** (`Backend`) is the only thing that runs git. It keeps one working-tree clone per
  project under `ReposRoot` and talks to clients over HTTP.
- The **remote** is an ordinary git repo the bot fetches from and pushes to. Locally it is just a
  folder addressed with a `file://` URL — no server needed.
- Each **app copy** is a plain folder of the project's JSON files (no `.git`). Online clients never
  run git; they sync/push over HTTP. Two separate folders = two independent users on one machine
  (credentials are cached per project-folder path).

```
D:\Projects\deusald-localizations-projects\
├─ bot-repos-root\          # ReposRoot — the bot auto-clones each project in here
└─ example-rpg\
   ├─ remote\               # git repo on branch `master`  → the "GitHub" for the bot
   ├─ app-adam\             # Adam's local project folder   → open this in the app
   └─ app-kasia\            # Kasia's local project folder
```

Nothing needs to be pre-created under `bot-repos-root` — the bot creates `ReposRoot` and clones on
the first request.

## 1. Configure the bot (`secrets.json`)

The `Bot` section is read from configuration. In local dev, put it in the Backend's
[user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) so paths and any real
credentials never land in the repo:

```bash
dotnet user-secrets --project Backend set "Bot:ReposRoot" "..."   # or edit secrets.json directly
```

Example `secrets.json` (edit paths for your machine):

```json
{
  "Bot": {
    "ReposRoot": "D:/Projects/deusald-localizations-projects/bot-repos-root",
    "Projects": [
      {
        "ProjectId": "a1a1a1a1-0000-4000-8000-000000000001",
        "Slug": "example-rpg",
        "RemoteUrl": "file:///D:/Projects/deusald-localizations-projects/example-rpg/remote/.git",
        "Branch": "master"
      }
    ]
  }
}
```

- `ProjectId` / `Slug` must match the project's `metadata.json` (`Id` / `Slug`).
- `RemoteUrl` points at the **`.git` of the non-bare `remote` repo** (`file:///…/remote/.git`).
- `Branch` must match the branch the seed commit lives on (`master` below).

## 2. Seed the `remote` repo from `ExampleLoc`

The bundled [`ExampleLoc/`](../ExampleLoc) demo project is **offline** (`ApiUrl` is empty) and its
members have no access-token hash, so it cannot be signed into as-is. Two changes turn it into a
working online project — set `ApiUrl`, and give every member their *initial* token (the BCrypt hash
of their own username, which is how first sign-in works). Do this with a throwaway helper that
references `Common`, so the JSON is written exactly the way the app reads it and the BCrypt hashes
are valid:

`seedgen/seedgen.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Common\Common.csproj" />
  </ItemGroup>
</Project>
```

`seedgen/Program.cs`:

```csharp
using DeusaldLocalizerCommon;

// Usage: seedgen <sourceProjectFolder> <destFolder> <apiUrl>
string source = args[0];
string dest   = args[1];
string apiUrl = args[2];

LocProject project = await ProjectFileService.OpenAsync(source);

project.Metadata.ApiUrl = apiUrl;

// Issue each member a random one-time sign-in token. Print it so you can hand it to them over a
// secure channel; MustResetAccessToken forces the app to rotate it to their own token on first sign-in.
foreach (LocProjectMember member in project.ProjectMembers)
{
    string token = AccessTokenService.GenerateToken();
    member.HashedAccessToken    = AccessTokenService.HashToken(token);
    member.MustResetAccessToken = true;
    Console.WriteLine($"{member.Username}: {token}");
}

await ProjectFileService.SaveAsync(project, dest);   // mints a fresh SyncId

Console.WriteLine(project.Metadata.SyncId);           // <-- tag the seed commit with this
```

Run it (adjust paths; `ApiUrl` matches the Backend's `http` launch profile):

```bash
dotnet run --project seedgen -- \
  "D:\Projects\deusald-localizer\ExampleLoc" \
  "D:\Projects\deusald-localizations-projects\example-rpg\remote" \
  "http://localhost:5114"
# prints the new SyncId, e.g. 4938ca2d-acc9-4c70-8826-23ac3f42a87e
```

## 3. Turn `remote` into a git repo

The **last commit of the seed must carry `SyncId: <guid>` in its message**, matching
`metadata.json`. That marker (`SyncTag.For`) is how the bot locates "the version a client last saw"
via `git log --grep` — without it every sync is a full resync.

```bash
cd D:/Projects/deusald-localizations-projects/example-rpg/remote

git init -b master
git config user.name  "Deusald Localizer Bot"
git config user.email "bot@localizer"
git config core.autocrlf false                       # avoid CRLF churn on Windows
git config receive.denyCurrentBranch updateInstead   # let the bot push to the checked-out branch

git add -A
git commit -m "Seed example-rpg project

SyncId: 4938ca2d-acc9-4c70-8826-23ac3f42a87e"          # <-- the SyncId printed above
```

`receive.denyCurrentBranch=updateInstead` is important: the `remote` repo has a checked-out
`master`, and a plain push to the checked-out branch is rejected by default. `updateInstead` accepts
the push and updates `remote`'s working tree too, so you can inspect the latest state directly.

## 4. Create the two app copies

The app folders are just the project files without `.git`. Copy them straight out of the seed
commit:

```bash
R=D:/Projects/deusald-localizations-projects/example-rpg
for u in app-adam app-kasia; do
  git -C "$R/remote" archive master | tar -x -C "$R/$u"
done
```

## 5. Run it

1. Start the bot: `dotnet run --project Backend` (serves `http://localhost:5114`).
2. In the app, **Open project…** → `…\example-rpg\app-adam`, sign in as **adam** / **adam**.
3. In another app instance (or later), **Open project…** → `…\example-rpg\app-kasia`, sign in as
   **kasia** / **kasia**.
4. Edit, **Push**, and **Sync** between the two folders to exercise conflicts and the never-merge
   push.

On first sign-in the app rotates the username-token to a fresh random token and shows it once — copy
it if you plan to sign out and back in, otherwise reset (see below).

### Seed users

| Username | UserId                                 | First-login token | Role                 |
|----------|----------------------------------------|-------------------|----------------------|
| adam     | `11111111-1111-4111-8111-111111111111` | `adam`            | Admin                |
| marie    | `22222222-2222-4222-8222-222222222222` | `marie`           | Reviewer (fr-FR)     |
| lukas    | `33333333-3333-4333-8333-333333333333` | `lukas`           | Reviewer (de-DE)     |
| kasia    | `44444444-4444-4444-8444-444444444444` | `kasia`           | Reviewer (pl-PL)     |

## Resetting

To start over from a clean seed, wipe the derived state and re-run steps 2–4. The bot re-fetches and
hard-resets its clone to the remote on the next request, so you don't clear it by hand — but if you
rewrote history on `remote`, deleting the clone forces a clean re-clone:

```bash
R=D:/Projects/deusald-localizations-projects
rm -rf "$R/example-rpg/remote"/* "$R/example-rpg/remote/.git"
rm -rf "$R/example-rpg/app-adam"/* "$R/example-rpg/app-kasia"/*
rm -rf "$R/bot-repos-root/example-rpg"     # optional: force a fresh clone
```

Note the app also caches the rotated access tokens per project-folder path; after a reset, sign in
again with the username tokens above.

## Sanity check without the app

With the bot running, a raw sync request confirms the chain (bot clones the `file://` remote, finds
the SyncId commit, authenticates the member):

```bash
PID=a1a1a1a1-0000-4000-8000-000000000001

# Current SyncId → UpToDate
curl -s -X POST "http://localhost:5114/projects/$PID/sync" \
  -H "Authorization: Bearer adam" -H "X-User-Id: 11111111-1111-4111-8111-111111111111" \
  -H "Content-Type: application/json" \
  -d '{"SyncId":"4938ca2d-acc9-4c70-8826-23ac3f42a87e"}'

# Unknown SyncId → FullResync (returns every file)
curl -s -X POST "http://localhost:5114/projects/$PID/sync" \
  -H "Authorization: Bearer kasia" -H "X-User-Id: 44444444-4444-4444-8444-444444444444" \
  -H "Content-Type: application/json" \
  -d '{"SyncId":"00000000-0000-0000-0000-000000000000"}'
```

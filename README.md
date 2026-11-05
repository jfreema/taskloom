# Taskloom

A task and project manager in the spirit of ClickUp: workspaces, spaces and lists of tasks with subtasks, shown
as a List, a Board or a Calendar, plus a personal Home page. It is a Vue 3 (TypeScript) web app with a C#
ASP.NET Core API and PostgreSQL. It ships as one container image and is built to run on Azure Container Apps.

## Features

- **Home (My Work):** your tasks across every list, grouped as Overdue, Today, Next 7 days, Later, No due date
  and Completed in the last 7 days. Complete a task from its circle, reschedule it, use "Move all to today" on
  overdue tasks, or add a task inline.
- **List view:** an editable table.
  - Group by status, priority, due date or nothing. Sort by manual order, due date, priority, name or created
    date. Choose the columns (status, assignees, due date, priority, tags) and show or hide completed tasks.
    These choices are saved per list.
  - Edit any field in the row. Add tasks inline at the end of a group or from its header. Expand subtasks in place.
  - With manual sort and grouping by status, drag rows to reorder them or to change their status.
  - Select rows with checkboxes, Shift-click, X or Mod+A. The bulk bar then sets the status, assignees, due date,
    priority or tags, moves the tasks to another list, or deletes them, with one undo for all of them.
- **Board view:** a column per status. Cards show tags, the due date, priority, subtask progress, comment and
  attachment counts, and assignees. Drag cards within or between columns, or move them from the keyboard. Each
  column has a menu to rename, recolour, collapse or delete it, and to edit the statuses.
- **Calendar view:** a month grid with tasks on their due dates (3 per day, then "+N more"). A "To schedule"
  tray lists overdue and undated tasks. Drag a chip onto a day to reschedule it, or onto "No due date" to clear
  its date. On narrow screens the month becomes an agenda list.
- **Task detail:** opens as a side panel, as a full page (`/t/<id>`) or as a sheet on phones. It holds:
  - the status, title and assignees (several per task), start and due dates, priority and tags
  - a rich-text description with a formatting toolbar and checklists. It saves as you type; if a save fails (for
    example offline), it keeps your text and sends it again when you're back online
  - subtasks
  - attachments: upload, drag and drop or paste a file; images show thumbnails and open in a lightbox
  - one activity feed of changes and comments; you can edit or delete your own comments
- **Command palette** (Mod+K): search task titles and descriptions, lists and actions, or create a task from what
  you typed.
- **Quick create** (N, or the "New task" button): every field in one dialog, prefilled from where you are.
  "Create more" keeps it open, and pasting several lines offers one task per line.
- **Undo:** completing, moving, rescheduling, bulk edits and deletes all show a toast with Undo (Mod+Z works too).
- **Settings:**
  - Profile: your display name.
  - Appearance: System, Light or Dark theme, and whether single-key shortcuts are on.
  - Workspace: rename or delete it.
  - Members: invite several people at once, change roles, remove people, copy the sign-in link.
  - Tags: create, rename, recolour or delete them.
  - Statuses are edited per list from the list menu or the board. Spaces and lists can be created, recoloured,
    moved and deleted from the sidebar.
- **Roles:** Owner, Admin, Member and Guest. Guests can read everything in the workspace and comment, but not
  edit. Every API call is checked against the caller's workspace membership.
- **Sign-in:** Microsoft Entra ID (one tenant) and Google, using OpenID Connect and a cookie session. A dev-only
  email sign-in is available for local testing. An email or domain allowlist limits who can sign in.
- **Design:** the "Loom" design system with light and dark themes. Phone layouts use a drawer, a task sheet, an
  agenda calendar and a "+" button, with 44px touch targets. The app is keyboard-first and targets WCAG 2.2 AA;
  the end-to-end tests run axe on the screens in both themes.

Not in this version: folders, custom fields, Gantt and timeline views, time tracking, recurring tasks,
notifications and @mentions, real-time or offline sync, and Planner import.

## Keyboard shortcuts

Mod is Ctrl on Windows and Linux and Cmd on a Mac. Press **?** in the app for the full list. Single-key
shortcuts never fire while you type, and you can turn them off in Settings › Appearance.

**Anywhere**

| Keys | Action |
| --- | --- |
| Mod+K | Command palette |
| / | Search this view (or open the palette where there is no search box) |
| N | New task |
| ? | Keyboard shortcuts |
| [ | Show or hide the sidebar |
| G then H | Go to Home |
| Mod+Z | Undo the latest action |
| Esc | Close the top menu or dialog, close the task panel, or clear the selection |

**List, Board and Home**

| Keys | Action |
| --- | --- |
| ↑ ↓ or J K | Move between rows or cards |
| ← → | Collapse or expand subtasks (List); move to the previous or next column (Board) |
| Home / End | First or last row (List, Home) |
| Enter | Open the focused task |
| X, or Space in the List | Select the row |
| Mod+A | Select every visible task (List) |
| Mod+Enter | Complete or reopen |
| S / A / D / P | Change the status, assignees, due date or priority (Home rows: D only) |
| F2 | Rename |
| Delete or Mod+Backspace | Delete, with undo |
| Shift+F10 | Open the row or card menu |
| Alt+↑ / Alt+↓ | Move the row or card up or down (List: manual sort, grouped by status) |
| Alt+← / Alt+→ | Move the card to the previous or next column (Board) |
| 1 2 3 4, 0 | In the priority menu: Urgent, High, Normal, Low, or clear |

**Calendar**

| Keys | Action |
| --- | --- |
| Arrow keys | Move between days |
| Home / End | Start or end of the week |
| PageUp / PageDown | Previous or next month |
| T | Today |
| Enter | Go into the day's tasks (Enter again opens one; Esc goes back to the day) |
| N | New task on the focused day |

**Task panel**

| Keys | Action |
| --- | --- |
| J / K | Next or previous task in the view |
| S / A / D / P | Change the status, assignees, due date or priority |
| Mod+Enter | Complete or reopen |
| Mod+Backspace | Delete the task, with undo |
| Enter / Shift+Enter | In a comment: send, or start a new line |

In quick create, Enter creates the task and Mod+Enter creates it and opens it.

## Upgrading existing data: back up first

The first start of this version upgrades the database by itself (`Database__MigrateOnStartup`). It copies each
task's assignee into the new multi-assignee table (when that person is still a member of the workspace), drops
the old assignee column, converts descriptions to rich text and updates the default colours. If your database
holds data you care about, **back it up before you start the new version**:

```bash
docker compose exec db pg_dump -U taskloom -Fc taskloom > taskloom-before-iter2.dump
```

`taskloom` is the database that `docker-compose.yml` creates; use your database's name if yours differs. Then
start the new version as usual (`docker compose up --build`). Attachment files live in blob storage (the
`azurite` volume locally), which the upgrade doesn't touch, so the database dump is all you need.

To go back to the backup, stop the app, recreate the database and restore it into the empty database:

```bash
docker compose stop app
docker compose exec db dropdb -U taskloom taskloom
docker compose exec db createdb -U taskloom taskloom
docker compose exec -T db pg_restore -U taskloom -d taskloom < taskloom-before-iter2.dump
```

Run the version that matches the backup afterwards: starting this version again upgrades the restored data again.

## Run it locally

You need **Docker**: Docker Desktop, or Colima with the Docker CLI on a Mac. That's all for option A.

### Option A: everything in Docker

```bash
docker compose up --build
```

Open http://localhost:8080 and sign in with any email (the local dev sign-in). Your first sign-in creates a
workspace with a "Personal" space and a "Tasks" list.

The compose file starts PostgreSQL 16, Azurite (a local Azure Blob Storage emulator, so attachments go through
the same Azure code path as production) and the Taskloom container. Every port listens on this machine only.

Reset all local data with `docker compose down -v`. If port 8080, 5432 or 10000 is already in use, set
`TASKLOOM_PORT`, `TASKLOOM_DB_PORT` or `TASKLOOM_BLOB_PORT` in a `.env` file next to `docker-compose.yml`. To
reach the app from a phone on the same network, set `TASKLOOM_BIND=0.0.0.0` (anyone who can reach it can sign
in, because the dev sign-in is on).

To try a second copy next to your own data, give it its own project name and ports, then remove it with its data:

```bash
TASKLOOM_DB_PORT=5433 TASKLOOM_PORT=8081 TASKLOOM_BLOB_PORT=10010 docker compose -p taskloom-smoke up --build -d
docker compose -p taskloom-smoke down -v
```

### Option B: hot-reload development

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Node 22.

```bash
docker compose up -d db                 # just PostgreSQL
dotnet run --project src/Taskloom.Api   # API on http://localhost:5080 (applies migrations)
cd web && npm ci && npm run dev         # web app on http://localhost:5173
```

Open http://localhost:5173. Vite forwards `/api` and `/auth` to the API (set `TASKLOOM_API` to use another
address), so the browser sees one origin. In this mode attachments are saved to `src/Taskloom.Api/data/files`.

### Sample data

With the dev sign-in on, `POST /api/dev/seed` with `{"workspaceId": "<id>"}` fills a workspace you own with
sample spaces, lists, tasks, tags and members, dated around today. Add `"fillers": 1000` (up to 2000) for a
large list to test performance. Calls that change data need the header `X-Taskloom: 1`.

### Optional: real Google or Microsoft sign-in locally

Create an OAuth client (in the Google Cloud console, or as an Entra ID app registration) with the redirect URI
`http://localhost:8080/auth/callback/google` or `.../auth/callback/microsoft` (use port 5173 for option B), then:

```bash
GOOGLE_CLIENT_ID=... GOOGLE_CLIENT_SECRET=... docker compose up --build
ENTRA_TENANT_ID=<tenant GUID> ENTRA_CLIENT_ID=... ENTRA_CLIENT_SECRET=... docker compose up --build
```

Microsoft sign-in accepts accounts from one Entra tenant only and stays off until `ENTRA_TENANT_ID` holds the
tenant id (a GUID, from Entra ID › Overview). For option B, set the `Auth__...` settings below instead. A button
for each configured provider appears on the sign-in page.

## Tests

### API tests

```bash
docker compose up -d db
dotnet test
```

The API tests run against a real PostgreSQL database; each run creates and drops its own databases. Set
`TASKLOOM_TEST_DB` to point them at another server.

### End-to-end tests (Playwright)

The suite in `web/e2e` drives the built app in Chromium. It has two projects: `desktop` (1440x900) and `phone`
(390x844, touch). It signs in with the dev sign-in and seeds its own data through the API, so run it against a
scratch database, never against real data.

```bash
cd web && npm ci && npm run build                 # writes the app into src/Taskloom.Api/wwwroot
npx playwright install chromium                   # once
cd .. && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  ConnectionStrings__Taskloom="Host=localhost;Port=5432;Database=taskloom_e2e;Username=taskloom;Password=taskloom" \
  Auth__DevLogin__Enabled=true dotnet run --project src/Taskloom.Api --no-launch-profile
cd web && npm run e2e                             # in a second terminal
```

The API creates `taskloom_e2e` on its first start. Start each run on a fresh database, because one test expects
the seeded invited people never to have signed in. Stop the API, drop the database, then start the API again:

```bash
docker compose exec db dropdb -U taskloom --if-exists --force taskloom_e2e
```

`TASKLOOM_E2E_URL` points the tests at another address (the default is http://localhost:5080). Every test fails
on a console error, and the screens run axe in both themes. The visual tour (scenario 30) saves screenshots of
every view in both themes to `web/e2e/screenshots`. `npm run e2e -- e2e/list.spec.ts` runs one file.

## Configuration

All settings come from `appsettings.json` and can be overridden with environment variables (`:` becomes `__`).
The middle column is the variable `docker-compose.yml` reads for it, where there is one.

| Setting | docker compose | Default | Notes |
| --- | --- | --- | --- |
| `ConnectionStrings__Taskloom` | | local database `taskloom` | PostgreSQL connection string |
| `Database__MigrateOnStartup` | | `true` | Apply database migrations when the app starts (back up first, see above) |
| `Auth__DevLogin__Enabled` | on | `true` in Development only | Type-any-email sign-in, plus `POST /api/dev/seed`. Never turn it on anywhere real |
| `Auth__Providers__google__ClientId` / `ClientSecret` | `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | empty | Google sign-in |
| `Auth__Microsoft__TenantId` | `ENTRA_TENANT_ID` | empty | The Entra tenant id, a GUID. Microsoft sign-in is limited to this tenant and is off without it |
| `Auth__Providers__microsoft__ClientId` / `ClientSecret` | `ENTRA_CLIENT_ID` / `ENTRA_CLIENT_SECRET` | empty | The Entra app registration |
| `Auth__Providers__<key>__Authority` / `ClientId` / `ClientSecret` | | | Any other OpenID Connect provider; its callback is `/auth/callback/<key>` |
| `Auth__AllowedEmails__0`, `Auth__AllowedEmailDomains__0` | | empty (anyone) | Limit who can sign in |
| `FileStore__Provider` | `Azure` (Azurite) | `Local` | `Local` (disk) or `Azure` (Blob Storage) |
| `FileStore__Local__Root` | | `data/files` in the app folder | Where `Local` keeps attachments |
| `FileStore__Azure__ConnectionString` or `FileStore__Azure__AccountUrl` | Azurite | empty | A connection string (Azurite), or an account URL plus managed identity in Azure |
| `FileStore__Azure__Container` | | `attachments` | The blob container |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | | empty | Sends OpenTelemetry data to Application Insights when set |

docker compose also reads `TASKLOOM_PORT` (8080), `TASKLOOM_DB_PORT` (5432), `TASKLOOM_BLOB_PORT` (10000) and
`TASKLOOM_BIND` (127.0.0.1).

## Project layout

```
src/
  Taskloom.Domain          entities: workspaces, spaces, lists, statuses, tasks, tags, comments, activity, attachments
  Taskloom.Application     interfaces (IFileStore, IEmailSender) and ordering helpers; no cloud or database code
  Taskloom.Infrastructure  EF Core + PostgreSQL, migrations, local-disk file store
  Taskloom.Cloud.Azure     Azure Blob Storage file store, Application Insights telemetry
  Taskloom.Api             ASP.NET Core host: sign-in, REST endpoints, serves the built web app
tests/Taskloom.Api.Tests   API tests against a real PostgreSQL
web/                       Vue 3 + TypeScript + Pinia + Vue Router (no cloud SDKs)
  src/components/ui        the Loom primitives (buttons, menus, popovers, dialogs, toasts...); /_ui shows them all
  e2e/                     Playwright end-to-end tests and the screenshots they save
deploy/azure/              Bicep for Azure Container Apps, PostgreSQL Flexible Server, Blob Storage
```

Cloud services sit behind interfaces, and a setting picks the implementation, so moving between local, Azure or
another cloud changes configuration, not code.

## Deploying to Azure

See [deploy/azure/README.md](deploy/azure/README.md); its `entraTenantId` parameter sets
`Auth__Microsoft__TenantId`. Nothing deploys automatically. Back up the database before the first deploy of this
version (see "Upgrading existing data" above).

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Taskloom.Infrastructure -s src/Taskloom.Infrastructure -o Migrations
```

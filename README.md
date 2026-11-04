# Taskloom

A task and project management app in the spirit of ClickUp: workspaces, spaces, lists, tasks with subtasks,
and List and Board views. Vue 3 (TypeScript) web app, C# ASP.NET Core API, PostgreSQL. It ships as one
container image and is built to run on Azure Container Apps.

## What's in this first draft

- **Hierarchy:** Workspace → Space → List → Task → Subtask. Each list has its own statuses
  (To do, In progress, Review, Done by default; add, rename or delete them).
- **List view:** tasks grouped by status, inline add, expandable subtasks, and quick edits for status, assignee,
  due date and priority.
- **Board view:** a column per status. Drag cards between and within columns; the order is saved.
- **Task panel:** title, description, status, assignee, start and due dates, priority, subtasks,
  comments and file attachments.
- **Filters:** search, "assigned to me" and "hide done".
- **Members and roles:** Owner, Admin, Member and Guest. Guests can read and comment but not edit.
  Every API call is checked against the caller's workspace membership.
- **Sign-in in the API:** a cookie session, with any OpenID Connect provider (Google now, Microsoft Entra ID
  for the org) plus a dev-only email sign-in for local testing. An email or domain allowlist limits who can sign in.

Not yet built: folders, custom fields, calendar and Gantt views, time tracking, notifications, offline sync
(PowerSync), and Planner import.

## Run it locally

You need **Docker Desktop**. That's all for option A.

### Option A: everything in Docker

```bash
docker compose up --build
```

Open http://localhost:8080 and sign in with any email (the local dev sign-in). Your first sign-in creates a
workspace with a "Personal" space and a "Tasks" list.

The compose file starts PostgreSQL 16, Azurite (a local Azure Blob Storage emulator, so attachments go through
the same Azure code path as production) and the Taskloom container.

Reset all local data with `docker compose down -v`. If port 8080, 5432 or 10000 is already in use, set
`TASKLOOM_PORT`, `TASKLOOM_DB_PORT` or `TASKLOOM_BLOB_PORT` in a `.env` file next to `docker-compose.yml`.

### Option B: hot-reload development

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Node 22.

```bash
docker compose up -d db                 # just PostgreSQL
dotnet run --project src/Taskloom.Api   # API on http://localhost:5080 (applies migrations)
cd web && npm install && npm run dev    # web app on http://localhost:5173
```

Open http://localhost:5173. Vite forwards `/api` and `/auth` to the API, so the browser sees one origin.
In this mode attachments are saved to `src/Taskloom.Api/data/files`.

### Tests

```bash
docker compose up -d db
dotnet test
cd web && npm run build    # type-checks and builds the web app
```

The API tests run against a real PostgreSQL database; each run creates and drops its own database.
Set `TASKLOOM_TEST_DB` to point them at another server.

### Optional: real Google or Microsoft sign-in locally

Create an OAuth client (Google Cloud console, or an Entra ID app registration) with the redirect URI
`http://localhost:8080/auth/callback/google` (or `.../microsoft`; use port 5173 for option B), then:

```bash
GOOGLE_CLIENT_ID=... GOOGLE_CLIENT_SECRET=... docker compose up --build
```

For option B, set `Auth__Providers__google__ClientId` and `Auth__Providers__google__ClientSecret` instead.
A "Continue with Google" button appears on the sign-in page once a provider is configured.

## Configuration

All settings come from `appsettings.json` and can be overridden with environment variables
(`:` becomes `__`).

| Setting | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings__Taskloom` | local compose database | PostgreSQL connection string |
| `Database__MigrateOnStartup` | `true` | Apply EF Core migrations when the app starts |
| `Auth__DevLogin__Enabled` | `true` in Development only | Type-any-email sign-in. Never enable in production |
| `Auth__Providers__<key>__Authority` / `ClientId` / `ClientSecret` | Google and Microsoft entries, no client ids | Any OpenID Connect provider; the callback is `/auth/callback/<key>` |
| `Auth__AllowedEmails__0`, `Auth__AllowedEmailDomains__0` | empty (anyone) | Limit who can sign in |
| `FileStore__Provider` | `Local` | `Local` (disk) or `Azure` (Blob Storage) |
| `FileStore__Azure__ConnectionString` or `FileStore__Azure__AccountUrl` | empty | Connection string for Azurite; account URL + managed identity in Azure |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | empty | Sends OpenTelemetry data to Application Insights when set |

## Project layout

```
src/
  Taskloom.Domain          entities: workspaces, spaces, lists, statuses, tasks, comments, attachments
  Taskloom.Application     interfaces (IFileStore, IEmailSender) and ordering helpers; no cloud or database code
  Taskloom.Infrastructure  EF Core + PostgreSQL, migrations, local-disk file store
  Taskloom.Cloud.Azure     Azure Blob Storage file store, Application Insights telemetry
  Taskloom.Api             ASP.NET Core host: sign-in, REST endpoints, serves the built web app
tests/Taskloom.Api.Tests   API tests against a real PostgreSQL
web/                       Vue 3 + TypeScript + Pinia + Vue Router (no cloud SDKs)
deploy/azure/              Bicep for Azure Container Apps, PostgreSQL Flexible Server, Blob Storage
```

Cloud services sit behind interfaces, and a setting picks the implementation, so moving between local,
Azure or another cloud changes configuration, not code.

## Deploying to Azure

See [deploy/azure/README.md](deploy/azure/README.md). Nothing deploys automatically.

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Taskloom.Infrastructure -s src/Taskloom.Infrastructure -o Migrations
```

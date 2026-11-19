# Deploying Taskloom to Azure

`main.bicep` creates everything Taskloom needs in one resource group:

| Resource | Purpose |
| --- | --- |
| Container Apps environment + app | Runs the single Taskloom image on port 8080, scales to zero |
| Azure Database for PostgreSQL Flexible Server (B1ms, PostgreSQL 16) | The database; migrations run on app start |
| Storage account + `attachments` container | Task attachments, reached with the app's managed identity (no keys) |
| Container Registry (Basic) | Holds the Taskloom image; the app pulls it with its managed identity |
| Log Analytics + Application Insights | Logs and OpenTelemetry traces |

Nothing deploys automatically. These steps are for when you are ready.

## 1. Create the resources

```bash
az group create -n taskloom-rg -l eastus
export TASKLOOM_PG_PASSWORD='<a strong password>'
az deployment group create -g taskloom-rg -f deploy/azure/main.bicep -p deploy/azure/main.bicepparam
```

The first run uses a placeholder image so the app exists before you push one. Note the
`registryLoginServer`, `appUrl` and `entraRedirectUri` outputs.

## 2. Build and push the image

```bash
az acr login -n <registry name>
docker build -t <registryLoginServer>/taskloom:v1 .
docker push <registryLoginServer>/taskloom:v1
az deployment group create -g taskloom-rg -f deploy/azure/main.bicep -p deploy/azure/main.bicepparam \
  -p image=<registryLoginServer>/taskloom:v1
```

## 3. Turn on Microsoft Entra ID sign-in

1. In Entra ID, register an app (single tenant). Add a **Web** redirect URI: the `entraRedirectUri` output
   (`https://<app>/auth/callback/microsoft`).
2. Create a client secret.
3. Redeploy with `-p entraTenantId=<tenant id> entraClientId=<client id> entraClientSecret=<secret> allowedEmailDomains='["yourorg.com"]'`.
   The tenant id is the directory's GUID (Entra ID › Overview). Sign-in is limited to that one tenant, and without
   it Microsoft sign-in stays off.

Dev sign-in is always off in this template.

## 4. Name the site admins

Site admins see everyone who has signed in or been invited (Settings › People), can turn an account off (the
person is signed out everywhere and can't sign in until it is turned back on) and can sign people out everywhere.
List their emails in `siteAdminEmails`, in `main.bicepparam` or next to the other `-p` values of your redeploy
(for example `siteAdminEmails='["it@yourorg.com"]'`). They become `Auth__SiteAdmins__0`, `Auth__SiteAdmins__1` and
so on. A redeploy uses the defaults for the parameters it doesn't pass, so pass the image and Entra values again too.

Sign-in sessions are checked against the database every minute, end after 14 days without use and 30 days after
sign-in, and sign-in and API calls are rate limited per address and per user. The client address comes from the
Container Apps ingress (`X-Forwarded-For`), so everyone behind one office NAT address shares the sign-in budget.
This template sets it to 120 sign-in requests a minute per address (`signInPerMinute`; each sign-in uses two) instead
of the app's default of 20. Raise it if a whole office signs in at once. The root README lists the other settings.

## 5. Email and reminders

The Inbox works as deployed. Email is off until the app has a mail server: add `Email__Provider=Smtp`,
`Email__From`, `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__Username`, `Email__Smtp__Password` (as a
secret) and `App__PublicUrl` (the `appUrl` output) to the container app's environment. The root README's
"Email and reminders" section explains each. This template doesn't set them yet.

The app scales to zero, and a stopped app runs no timer: morning reminders and waiting emails would go out only when
someone next opens Taskloom. Either keep one replica running (`minReplicas: 1`), or set `Jobs__TriggerKey` and have
a scheduled Container Apps job call `POST /jobs/run` every few minutes with the key in `X-Taskloom-Jobs-Key`.

## Still to do before the org rollout

- Email settings and the reminder schedule in this template (see step 5), with the SMTP password in Key Vault.

- Move the Postgres password and Entra secret into Key Vault references.
- VNet integration and a private endpoint for Postgres instead of the "Allow Azure services" firewall rule.
- Set `wal_level=logical` on Postgres when PowerSync (offline sync) is added.
- A GitHub Actions workflow that builds, pushes and deploys on merge.

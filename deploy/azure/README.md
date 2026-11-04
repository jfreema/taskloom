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

## Still to do before the org rollout

- Move the Postgres password and Entra secret into Key Vault references.
- VNet integration and a private endpoint for Postgres instead of the "Allow Azure services" firewall rule.
- Set `wal_level=logical` on Postgres when PowerSync (offline sync) is added.
- A GitHub Actions workflow that builds, pushes and deploys on merge.

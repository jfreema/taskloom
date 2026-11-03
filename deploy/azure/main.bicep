// Taskloom on Azure: Container Apps + PostgreSQL Flexible Server + Blob Storage + Application Insights.
// Deploy into an existing resource group:
//   az deployment group create -g <rg> -f deploy/azure/main.bicep -p deploy/azure/main.bicepparam
// Nothing here is deployed by CI; this is the target shape for the org rollout.

targetScope = 'resourceGroup'

@description('Short name used to derive resource names, e.g. "taskloom".')
@minLength(3)
@maxLength(12)
param appName string = 'taskloom'

param location string = resourceGroup().location

@description('Container image to run. Leave the default for a first deploy, then push to the registry this template creates.')
param image string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('PostgreSQL administrator login.')
param postgresAdminLogin string = 'taskloomadmin'

@secure()
@description('PostgreSQL administrator password.')
param postgresAdminPassword string

@description('Microsoft Entra ID tenant id for staff sign-in. Empty disables Microsoft sign-in.')
param entraTenantId string = ''
param entraClientId string = ''
@secure()
param entraClientSecret string = ''

@description('Only these email domains may sign in, e.g. ["contoso.com"]. Empty allows any account the identity provider accepts.')
param allowedEmailDomains array = []

var placeholderImage = 'mcr.microsoft.com/k8se/quickstart:latest'
// The placeholder image listens on 80; Taskloom listens on 8080.
var port = image == placeholderImage ? 80 : 8080
var suffix = uniqueString(resourceGroup().id)
var names = {
  logs: '${appName}-logs'
  insights: '${appName}-insights'
  identity: '${appName}-id'
  registry: toLower('${appName}${suffix}')
  storage: toLower(take('${appName}${suffix}', 24))
  postgres: '${appName}-pg-${suffix}'
  environment: '${appName}-env'
  app: appName
}

// ---- Observability ----
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logs
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.insights
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---- Identity the app runs as (pulls images, reads/writes blobs; no keys in config) ----
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity
  location: location
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: names.registry
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, acrPullRole)
  scope: registry
  properties: {
    roleDefinitionId: acrPullRole
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---- Attachments ----
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: names.storage
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource attachments 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'attachments'
}

var blobContributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, blobContributorRole)
  scope: storage
  properties: {
    roleDefinitionId: blobContributorRole
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---- Database ----
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: names.postgres
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32 }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'taskloom'
}

// Lets Azure services (the container app) reach the server. Swap for VNet integration when the org requires it.
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---- App ----
resource appEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: names.environment
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var connectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=taskloom;Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require'
var entraEnabled = !empty(entraTenantId) && !empty(entraClientId)

var baseEnv = [
  { name: 'ConnectionStrings__Taskloom', secretRef: 'db-connection' }
  { name: 'FileStore__Provider', value: 'Azure' }
  { name: 'FileStore__Azure__AccountUrl', value: storage.properties.primaryEndpoints.blob }
  { name: 'FileStore__Azure__Container', value: attachments.name }
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
  { name: 'Auth__DevLogin__Enabled', value: 'false' }
]
var entraEnv = entraEnabled ? [
  { name: 'Auth__Providers__microsoft__Authority', value: '${environment().authentication.loginEndpoint}${entraTenantId}/v2.0' }
  { name: 'Auth__Providers__microsoft__ClientId', value: entraClientId }
  { name: 'Auth__Providers__microsoft__ClientSecret', secretRef: 'entra-client-secret' }
  { name: 'Auth__Providers__microsoft__ValidateIssuer', value: 'true' }
] : []
var domainEnv = [for (d, i) in allowedEmailDomains: { name: 'Auth__AllowedEmailDomains__${i}', value: d }]

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.app
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: appEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: port
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identity.id
        }
      ]
      secrets: concat([
        { name: 'db-connection', value: connectionString }
      ], entraEnabled ? [
        { name: 'entra-client-secret', value: entraClientSecret }
      ] : [])
    }
    template: {
      containers: [
        {
          name: 'taskloom'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(baseEnv, entraEnv, domainEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: image == placeholderImage ? '/' : '/healthz', port: port }
              initialDelaySeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
  dependsOn: [acrPull, blobContributor, database, allowAzure]
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output registryLoginServer string = registry.properties.loginServer
output entraRedirectUri string = 'https://${app.properties.configuration.ingress.fqdn}/auth/callback/microsoft'

// =====================================================================
// The Sorting Hat — Azure infrastructure, reconciled with production.
//
// This template reflects the resources that ACTUALLY exist in resource
// group `chores-app` (westus3), verified via read-only `az` inspection on
// 2026-08-02 while diagnosing the outage in issue #12. Prior versions of
// this file described a fictional F1/Windows/eastus environment.
//
// SAFETY MODEL
// ------------
// * The APP tier (plan, web app, App Insights) is always managed. It is
//   idempotent against the live app and safe to `what-if` / apply.
// * The DATA tier (SQL server, serverless DB, private endpoint, private DNS)
//   is gated behind `deployDataTier` (default FALSE) because it is stateful
//   and its admin password cannot be read back from Azure. Leave it false for
//   routine app-config drift correction. Only set it true for an intentional
//   (re)build of the data tier, with `sqlAdministratorLoginPassword` supplied.
//   ALWAYS run `az deployment group what-if` first.
//
// RUNTIME SINGLE SOURCE OF TRUTH (issue #12)
// ------------------------------------------
// `dotnetVersion` sets `linuxFxVersion`. It is a Linux plan, so
// `netFrameworkVersion` (Windows-only, the property the old template used)
// is deliberately NOT set — that no-op is exactly what let a net10.0 build
// deploy onto a DOTNETCORE|9.0 runtime. The CI/CD pipeline additionally
// derives this value from <TargetFramework> in the .csproj on every deploy.
// =====================================================================

targetScope = 'resourceGroup'

// ---- General -------------------------------------------------------
@description('Location for all resources. Production is westus3.')
param location string = 'westus3'

@description('Runtime for the Linux App Service. MUST match <TargetFramework> in Zazzo.ChoresWizard2000.csproj (net10.0 -> DOTNETCORE|10.0).')
param dotnetVersion string = 'DOTNETCORE|10.0'

@description('ASPNETCORE_ENVIRONMENT value for the web app.')
param aspNetCoreEnvironment string = 'Production'

@description('Health-check path. Must be a DB-free endpoint (issue #2 /healthz) so the serverless DB can still auto-pause.')
param healthCheckPath string = '/healthz'

@description('Common resource tags.')
param tags object = {
  environment: 'prod'
  application: 'ChoresWizard2000'
}

// ---- Existing resource names (verified) ----------------------------
@description('App Service Plan name (existing).')
param appServicePlanName string = 'ASP-choresapp-be45'

@description('Web App name (existing).')
param webAppName string = 'zazzo-chores'

@description('VNet the app integrates with (existing).')
param vnetName string = 'zazzo-choresVnet'

@description('Delegated subnet used for regional VNet integration (existing).')
param appSubnetName string = 'zazzo-choresAppSubnet'

@description('Subnet that hosts the SQL private endpoint (existing).')
param privateEndpointSubnetName string = 'zazzo-choresSubnet'

@description('SQL logical server name (existing).')
param sqlServerName string = 'zazzo-chores-server'

@description('SQL database name (existing).')
param sqlDatabaseName string = 'zazzo-chores-database'

// ---- Observability -------------------------------------------------
@description('Log Analytics workspace backing Application Insights (created — none exists today).')
param logAnalyticsName string = 'zazzo-chores-logs'

@description('Application Insights component name (created — none exists today).')
param appInsightsName string = 'zazzo-chores-insights'

// ---- Data tier gate ------------------------------------------------
@description('Manage the stateful SQL data tier. Default false — see SAFETY MODEL above.')
param deployDataTier bool = false

@description('SQL administrator login (existing server admin). Only used when deployDataTier=true.')
param sqlAdministratorLogin string = 'zazzo-chores-server-admin'

@description('SQL administrator password. Required (and never stored in git) only when deployDataTier=true.')
@secure()
param sqlAdministratorLoginPassword string = ''

@description('SQL public network access when the data tier is managed. Secure default is Disabled (rely on the private endpoint).')
@allowed(['Enabled', 'Disabled'])
param sqlPublicNetworkAccess string = 'Disabled'

// ---- Derived -------------------------------------------------------
// Deterministic FQDN so the connection string does not depend on whether the
// data tier is managed in this deployment.
var sqlServerFqdn = '${sqlServerName}${environment().suffixes.sqlServerHostname}'

// Managed-identity (passwordless) connection string. Matches appsettings.json
// key `AzureSqlConnection`. No password is ever placed in an app setting.
var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;'

var privateDnsZoneName = 'privatelink${environment().suffixes.sqlServerHostname}'

// ---- Existing network references -----------------------------------
resource appSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: '${vnetName}/${appSubnetName}'
}

// =====================================================================
// Observability: Log Analytics + workspace-based Application Insights
// =====================================================================
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// =====================================================================
// App Service Plan — B1 Basic, Linux
// =====================================================================
resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
    size: 'B1'
    family: 'B'
    capacity: 1
  }
  kind: 'linux'
  properties: {
    reserved: true // Linux
  }
}

// =====================================================================
// Web App — Linux, .NET on DOTNETCORE|x, VNet-integrated, MI-authenticated
// =====================================================================
resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    // httpsOnly is deliberately FALSE — the owner has chosen to keep
    // chores.zazzo.com reachable over plain HTTP. This matches live reality
    // (the site is httpsOnly:false); the committed template previously said
    // `true`, which never matched the environment. This is an explicit owner
    // decision, NOT drift — do not "fix" it to true.
    httpsOnly: false
    virtualNetworkSubnetId: appSubnet.id // regional VNet integration
    siteConfig: {
      linuxFxVersion: dotnetVersion // single source of truth for runtime
      alwaysOn: true // matches live reality; B1 Basic supports Always On (issue #12)
      http20Enabled: true // intentional enhancement (issue #4 config hardening); enables HTTP/2
      minTlsVersion: '1.2'
      ftpsState: 'FtpsOnly'
      healthCheckPath: healthCheckPath // DB-free endpoint
      vnetRouteAllEnabled: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: aspNetCoreEnvironment
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
      ]
      connectionStrings: [
        {
          // Key matches Program.cs GetConnectionString("AzureSqlConnection").
          // Passwordless: uses the App Service system-assigned identity.
          name: 'AzureSqlConnection'
          connectionString: sqlConnectionString
          type: 'SQLAzure'
        }
      ]
    }
  }
}

// =====================================================================
// DATA TIER (gated) — SQL server, serverless DB, private endpoint + DNS
// =====================================================================
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = if (deployDataTier) {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdministratorLogin
    administratorLoginPassword: sqlAdministratorLoginPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    // Resolve the contradictory posture (issue #12): private endpoint only.
    publicNetworkAccess: sqlPublicNetworkAccess
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = if (deployDataTier) {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1 // max vCores; serverless scales down to minCapacity
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 34359738368 // 32 GB
    autoPauseDelay: 60 // minutes
    minCapacity: json('0.5')
    zoneRedundant: false
    readScale: 'Disabled'
    requestedBackupStorageRedundancy: 'Geo'
  }
}

// SQL private endpoint in the non-delegated subnet.
resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: '${vnetName}/${privateEndpointSubnetName}'
}

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = if (deployDataTier) {
  name: 'zazzo-choresDbEndpoint'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'zazzo-choresDbEndpoint'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource sqlPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (deployDataTier) {
  name: privateDnsZoneName
  location: 'global'
  tags: tags
}

resource sqlPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (deployDataTier) {
  parent: sqlPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: resourceId('Microsoft.Network/virtualNetworks', vnetName)
    }
  }
}

resource sqlPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = if (deployDataTier) {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sql'
        properties: {
          privateDnsZoneId: sqlPrivateDnsZone.id
        }
      }
    ]
  }
}

// =====================================================================
// Outputs
// =====================================================================
output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output webAppPrincipalId string = webApp.identity.principalId
output appInsightsName string = appInsights.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output sqlServerFqdn string = sqlServerFqdn
output sqlDatabaseName string = sqlDatabaseName
output resourceGroupName string = resourceGroup().name

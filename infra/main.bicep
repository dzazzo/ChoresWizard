// =====================================================
// The Sorting Hat - Azure Infrastructure
// Deploys: App Service (Free F1) + Azure SQL Database
// =====================================================

@description('Location for all resources')
param location string = resourceGroup().location

@description('Environment name (used for resource naming)')
param environmentName string = 'prod'

@description('The base name for all resources')
param appName string = 'choreswizard'

@description('SQL Server administrator username')
param sqlAdminUsername string

@description('SQL Server administrator password')
@secure()
param sqlAdminPassword string

// Generate unique suffix for globally unique names
var resourceToken = uniqueString(resourceGroup().id)
var appServicePlanName = '${appName}-plan-${resourceToken}'
var webAppName = '${appName}-${resourceToken}'
var sqlServerName = '${appName}-sql-${resourceToken}'
var sqlDatabaseName = '${appName}-db'

// =====================================================
// App Service Plan (Free F1 Tier)
// =====================================================
resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'F1'
    tier: 'Free'
    size: 'F1'
    family: 'F'
    capacity: 1
  }
  kind: 'app'
  properties: {
    reserved: false // Windows
  }
  tags: {
    environment: environmentName
    application: 'ChoresWizard2000'
  }
}

// =====================================================
// Web App (ASP.NET Core 10.0)
// =====================================================
resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app'
  identity: {
    // System-assigned managed identity — the explicit, pinned identity used for
    // Azure SQL auth (Authentication=Active Directory Managed Identity) instead of
    // the slow DefaultAzureCredential chain. Grant it access with:
    //   CREATE USER [<webAppName>] FROM EXTERNAL PROVIDER;
    //   ALTER ROLE db_datareader ADD MEMBER [<webAppName>];
    //   ALTER ROLE db_datawriter ADD MEMBER [<webAppName>];
    //   ALTER ROLE db_ddladmin  ADD MEMBER [<webAppName>]; -- needed for migrations
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      // App Service Health Check pings this path and recycles unhealthy instances.
      // NOTE: the Health Check feature requires Basic (B1) tier or higher — it is a
      // no-op on the Free F1 tier, but the property is harmless to set.
      healthCheckPath: '/healthz'
      // alwaysOn is NOT set: the Free F1 tier does not support Always On, so the app
      // still unloads after ~20 min idle. Mitigations for the cold start are in the
      // app itself (background migration + retry + health probes). Move to B1+ and set
      // alwaysOn: true to eliminate idle unloads entirely.
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
      ]
      connectionStrings: [
        {
          name: 'AzureSqlConnection'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Persist Security Info=False;User ID=${sqlAdminUsername};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
          type: 'SQLAzure'
        }
      ]
    }
  }
  tags: {
    environment: environmentName
    application: 'ChoresWizard2000'
  }
}

// =====================================================
// Azure SQL Server
// =====================================================
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminUsername
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
  tags: {
    environment: environmentName
    application: 'ChoresWizard2000'
  }
}

// =====================================================
// SQL Server Firewall Rule - Allow Azure Services
// =====================================================
resource sqlFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// =====================================================
// Azure SQL Database (Basic Tier - ~$5/month)
// =====================================================
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648 // 2 GB
    catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    readScale: 'Disabled'
    requestedBackupStorageRedundancy: 'Local'
  }
  tags: {
    environment: environmentName
    application: 'ChoresWizard2000'
  }
}

// =====================================================
// Log Analytics Workspace (backs Application Insights)
// =====================================================
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs-${resourceToken}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
  tags: {
    environment: environmentName
    application: 'ChoresWizard2000'
  }
}

// =====================================================
// Application Insights (workspace-based)
// Consumed by the app via APPLICATIONINSIGHTS_CONNECTION_STRING.
// =====================================================
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-ai-${resourceToken}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
  tags: {
    environment: environmentName
    application: 'ChoresWizard2000'
  }
}

// =====================================================
// Outputs
// =====================================================
@description('The URL of the deployed web app')
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'

@description('The name of the web app (for deployment)')
output webAppName string = webApp.name

@description('The name of the SQL Server')
output sqlServerName string = sqlServer.name

@description('The fully qualified domain name of the SQL Server')
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('The name of the SQL Database')
output sqlDatabaseName string = sqlDatabase.name

@description('The name of the resource group')
output resourceGroupName string = resourceGroup().name

@description('The principal ID of the web app system-assigned managed identity (grant this DB access)')
output webAppPrincipalId string = webApp.identity.principalId

@description('The name of the Application Insights resource')
output appInsightsName string = appInsights.name

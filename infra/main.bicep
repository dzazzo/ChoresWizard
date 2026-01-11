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
// Web App (ASP.NET Core 9.0)
// =====================================================
resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v9.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
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

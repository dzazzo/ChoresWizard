using './main.bicep'

// =====================================================================
// The Sorting Hat — deployment parameters (reconciled with production).
//
// Defaults in main.bicep already match the live `chores-app` environment,
// so this file only pins the values worth being explicit about. The data
// tier stays gated OFF; see docs/azure-setup.md before turning it on.
// =====================================================================

param location = 'westus3'
param dotnetVersion = 'DOTNETCORE|10.0'
param aspNetCoreEnvironment = 'Production'
param healthCheckPath = '/healthz'

// Data tier is stateful and its admin password is not in git. Keep false for
// routine app-config reconciliation. To (re)build the data tier, set true AND
// pass sqlAdministratorLoginPassword securely at deploy time, e.g.:
//   az deployment group create ... \
//     --parameters deployDataTier=true \
//     --parameters sqlAdministratorLoginPassword='<secret>'
param deployDataTier = false
param sqlPublicNetworkAccess = 'Disabled'

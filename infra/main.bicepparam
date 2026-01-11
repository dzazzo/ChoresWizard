using './main.bicep'

// =====================================================
// The Sorting Hat - Azure Deployment Parameters
// =====================================================

// Location - defaults to resource group location
param location = 'eastus'

// Environment name for tagging
param environmentName = 'prod'

// Application name (used as prefix for resources)
param appName = 'choreswizard'

// SQL Server credentials
// IMPORTANT: Replace these with secure values before deployment!
// For production, use Azure Key Vault or deployment-time secrets
param sqlAdminUsername = 'sqladmin'

// This password should be set securely during deployment
// Using az deployment command with --parameters sqlAdminPassword='YOUR_SECURE_PASSWORD'
param sqlAdminPassword = '' // Set via command line for security

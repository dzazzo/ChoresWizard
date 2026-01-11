# PowerShell script to deploy The Sorting Hat to Azure
# =====================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$false)]
    [string]$Location = "eastus",
    
    [Parameter(Mandatory=$true)]
    [string]$SqlAdminUsername,
    
    [Parameter(Mandatory=$true)]
    [SecureString]$SqlAdminPassword
)

$ErrorActionPreference = "Stop"

Write-Host "🧙 The Sorting Hat - Azure Deployment Script" -ForegroundColor Magenta
Write-Host "=============================================" -ForegroundColor Magenta

# Convert SecureString to plain text for Bicep
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword)
$PlainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)

try {
    # Check if logged in to Azure
    Write-Host "`n🔐 Checking Azure login status..." -ForegroundColor Yellow
    $account = az account show 2>$null | ConvertFrom-Json
    if (-not $account) {
        Write-Host "Please log in to Azure..." -ForegroundColor Yellow
        az login
    }
    Write-Host "✓ Logged in as: $($account.user.name)" -ForegroundColor Green

    # Create resource group if it doesn't exist
    Write-Host "`n📦 Creating resource group '$ResourceGroupName'..." -ForegroundColor Yellow
    az group create --name $ResourceGroupName --location $Location --output none
    Write-Host "✓ Resource group ready" -ForegroundColor Green

    # Deploy Bicep template
    Write-Host "`n🏗️ Deploying Azure infrastructure (this may take a few minutes)..." -ForegroundColor Yellow
    $deploymentOutput = az deployment group create `
        --resource-group $ResourceGroupName `
        --template-file "./infra/main.bicep" `
        --parameters sqlAdminUsername=$SqlAdminUsername `
        --parameters sqlAdminPassword=$PlainPassword `
        --query "properties.outputs" `
        --output json | ConvertFrom-Json

    $webAppName = $deploymentOutput.webAppName.value
    $webAppUrl = $deploymentOutput.webAppUrl.value
    
    Write-Host "✓ Infrastructure deployed successfully!" -ForegroundColor Green
    Write-Host "  Web App: $webAppName" -ForegroundColor Cyan
    Write-Host "  URL: $webAppUrl" -ForegroundColor Cyan

    # Build and publish the application
    Write-Host "`n📦 Building application..." -ForegroundColor Yellow
    dotnet publish -c Release -o ./publish
    Write-Host "✓ Application built" -ForegroundColor Green

    # Deploy to Azure App Service
    Write-Host "`n🚀 Deploying application to Azure App Service..." -ForegroundColor Yellow
    
    # Create zip file for deployment
    Compress-Archive -Path ./publish/* -DestinationPath ./publish.zip -Force
    
    # Deploy using Azure CLI
    az webapp deployment source config-zip `
        --resource-group $ResourceGroupName `
        --name $webAppName `
        --src ./publish.zip
    
    Write-Host "✓ Application deployed!" -ForegroundColor Green

    # Clean up
    Remove-Item ./publish -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item ./publish.zip -Force -ErrorAction SilentlyContinue

    Write-Host "`n✨ Deployment complete! ✨" -ForegroundColor Magenta
    Write-Host "=============================================" -ForegroundColor Magenta
    Write-Host "🌐 Your app is available at: $webAppUrl" -ForegroundColor Green
    Write-Host "`n⏳ Note: It may take a minute for the database to initialize on first load." -ForegroundColor Yellow

} catch {
    Write-Host "`n❌ Deployment failed: $_" -ForegroundColor Red
    throw
} finally {
    # Clear sensitive data
    $PlainPassword = $null
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
}

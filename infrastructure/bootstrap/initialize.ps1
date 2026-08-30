[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern("^[a-z][a-z0-9-]{4,28}[a-z0-9]$")]
    [string]$ProjectId,

    [Parameter(Mandatory)]
    [ValidatePattern("^[a-z0-9][a-z0-9._-]{1,220}[a-z0-9]$")]
    [string]$StateBucketName,

    [Parameter(Mandatory)]
    [ValidatePattern("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    [string]$GitHubRepository,

    [ValidatePattern("^[a-z]+-[a-z]+[0-9]+$")]
    [string]$Region = "us-east1",

    [ValidateSet("development", "production")]
    [string]$Environment = "development",

    [switch]$AutoApprove
)

$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Description,
        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Terraform {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Invoke-NativeCommand "Terraform $($Arguments[0]) in $Directory" {
        & terraform "-chdir=$Directory" @Arguments
    }
}

function Get-TerraformOutput {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $output = & terraform "-chdir=$Directory" output -raw $Name 2>$null
    if ($LASTEXITCODE -ne 0) {
        return ""
    }

    return [string]$output
}

function Test-GcloudAuthentication {
    & gcloud auth print-access-token *> $null
    $gcloudAuthenticated = $LASTEXITCODE -eq 0

    & gcloud auth application-default print-access-token *> $null
    $applicationDefaultAuthenticated = $LASTEXITCODE -eq 0

    return $gcloudAuthenticated -and $applicationDefaultAuthenticated
}

foreach ($command in "gcloud", "terraform") {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command CLI is required."
    }
}

if (-not (Test-GcloudAuthentication)) {
    if (-not [Environment]::UserInteractive) {
        throw "Google authentication is unavailable. Run this script from an interactive PowerShell session."
    }

    Write-Host "Opening Google sign-in to refresh gcloud and Terraform credentials."
    Invoke-NativeCommand "Google sign-in" {
        & gcloud auth login --update-adc --force
    }

    if (-not (Test-GcloudAuthentication)) {
        throw "Google sign-in did not establish both gcloud and Application Default Credentials."
    }
}

Invoke-NativeCommand "Setting Application Default Credentials quota project" {
    & gcloud auth application-default set-quota-project $ProjectId
}
$env:GOOGLE_CLOUD_QUOTA_PROJECT = $ProjectId

$scriptRoot = $PSScriptRoot
$infrastructureRoot = Split-Path -Parent $scriptRoot
$bootstrapRoot = $scriptRoot
$sharedRoot = Join-Path $infrastructureRoot "shared"
$developmentRoot = Join-Path $infrastructureRoot "environments/development"
$bucketUri = "gs://$StateBucketName"

$projectNumber = ([string](& gcloud projects describe $ProjectId --format="value(projectNumber)")).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($projectNumber)) {
    throw "Could not resolve project number for $ProjectId."
}

$bucketProjectNumber = ([string](& gcloud storage buckets describe $bucketUri --raw --format="value(projectNumber)" 2>$null)).Trim()
if ($LASTEXITCODE -ne 0) {
    Invoke-NativeCommand "Creating Terraform state bucket $StateBucketName" {
        & gcloud storage buckets create $bucketUri `
            "--project=$ProjectId" `
            "--location=$Region" `
            "--default-storage-class=STANDARD" `
            "--uniform-bucket-level-access" `
            "--public-access-prevention"
    }
}
elseif ([string]::IsNullOrWhiteSpace($bucketProjectNumber)) {
    throw "Could not determine the owning project for Terraform state bucket $StateBucketName."
}
elseif ($bucketProjectNumber -ne $projectNumber) {
    throw "Terraform state bucket $StateBucketName belongs to project number $bucketProjectNumber, not $ProjectId."
}

Invoke-NativeCommand "Hardening Terraform state bucket $StateBucketName" {
    & gcloud storage buckets update $bucketUri `
        "--project=$ProjectId" `
        "--uniform-bucket-level-access" `
        "--public-access-prevention" `
        "--versioning"
}

$bootstrapVariables = @(
    "-var=project_id=$ProjectId",
    "-var=region=$Region",
    "-var=state_bucket_name=$StateBucketName",
    "-var=github_repository=$GitHubRepository"
)
$sharedVariables = @(
    "-var=project_id=$ProjectId",
    "-var=region=$Region"
)
$applyArguments = @("apply")
if ($AutoApprove) {
    $applyArguments += "-auto-approve"
}

Invoke-Terraform -Directory $bootstrapRoot -Arguments @("init", "-backend-config=bucket=$StateBucketName", "-backend-config=backend.hcl")
Invoke-Terraform -Directory $bootstrapRoot -Arguments ($applyArguments + $bootstrapVariables)

Invoke-Terraform -Directory $sharedRoot -Arguments @("init", "-backend-config=bucket=$StateBucketName", "-backend-config=backend.hcl")
Invoke-Terraform -Directory $sharedRoot -Arguments ($applyArguments + $sharedVariables)

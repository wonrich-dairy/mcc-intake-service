# CI/CD Pipeline Documentation — MCC & Intake Service

## Overview

The CI/CD pipeline is implemented as a GitHub Actions workflow (`.github/workflows/ci-cd.yml`) that builds, tests, and deploys MCC & Intake Service independently of other services.

## Pipeline Architecture

```
PR to develop/main          Push to develop           Push to main
       │                          │                        │
       ▼                          ▼                        ▼
  ┌──────────┐             ┌──────────┐             ┌──────────┐
  │  Build & │             │  Build & │             │  Build & │
  │   Test   │             │   Test   │             │   Test   │
  └──────────┘             └────┬─────┘             └────┬─────┘
       │                        │                        │
   (status on PR)               ▼                        ▼
                         ┌────────────┐          ┌────────────────┐
                         │ Deploy to  │          │   Deploy to    │
                         │  Staging   │          │  Production    │
                         │  (auto)    │          │ (manual gate)  │
                         └────────────┘          └────────────────┘
```

## Triggers

| Event | Branch | Action |
|---|---|---|
| Pull Request | `develop`, `main` | Build & test only (status visible on PR) |
| Push | `develop` | Build, test, deploy to staging (automatic) |
| Push | `main` | Build, test, deploy to production (manual approval required) |

## Jobs

### 1. `build-and-test`
- Restores NuGet packages
- Builds in Release configuration
- Runs unit tests — **a test failure blocks deployment**
- Publishes build artifacts (on push only)

### 2. `deploy-staging`
- Runs only on `develop` push
- Downloads build artifact
- Deploys to `app-mcc-intake-staging` using Azure publish profile

### 3. `deploy-production`
- Runs only on `main` push
- Requires **manual approval** via GitHub Environments
- Downloads build artifact
- Deploys to `app-mcc-intake-prod` using Azure publish profile

## Required GitHub Secrets

| Secret Name | Description |
|---|---|
| `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING` | Publish profile XML for staging App Service |
| `AZURE_WEBAPP_PUBLISH_PROFILE_PROD` | Publish profile XML for production App Service |

### How to obtain a publish profile
```powershell
az webapp deployment list-publishing-profiles --name app-mcc-intake-staging --resource-group rg-mcc-intake-staging --xml
```
Copy the entire XML output and save it as the GitHub secret.

## GitHub Environments Setup

To enable the manual approval gate for production:
1. Go to `Settings` → `Environments` in the repo
2. Create environment `production`
3. Add a **required reviewer** (the DevOps role holder)
4. Create environment `staging` (no approval required)

## Rollback

To rollback to a previous deployment:
1. Go to the **Actions** tab in the repo
2. Find the last successful deployment run
3. Click **Re-run jobs** → **Re-run all jobs**

Alternatively, use Azure CLI:
```powershell
# List previous deployments
az webapp deployment list --name app-mcc-intake-staging --resource-group rg-mcc-intake-staging

# Rollback via redeployment of a specific commit
# Re-run the GitHub Actions workflow for the desired commit
```

## Verification

- [ ] Pipeline runs green on a clean push to `develop`
- [ ] A deliberate failing test blocks deployment
- [ ] Rollback executed successfully at least once

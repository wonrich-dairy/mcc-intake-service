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

To enable the manual approval gate and branch restriction for production:
1. Go to `Settings` → `Environments` in the repo
2. Create environment `production`
3. Add a **required reviewer** (the DevOps role holder)
4. Set **Deployment branches** to "Selected branches" → add `main`
5. Add `AZURE_WEBAPP_PUBLISH_PROFILE_PROD` as an **environment secret** (not a repository secret)
6. Create environment `staging` (no approval required)
7. Add `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING` as an environment secret on `staging`

> **Why the branch policy matters:** It prevents anyone from dispatching a production deployment from a feature branch. The workflow also validates this with a ref guard, but the environment policy is the authoritative control.

## Rollback

The pipeline supports rollback via `workflow_dispatch` — a manual trigger that rebuilds and redeploys a specific commit.

### How to rollback

1. Go to the **Actions** tab in the repo
2. Select the **"CI/CD — MCC & Intake Service"** workflow on the left
3. Click **"Run workflow"** (top right)
4. Fill in:
   - **Branch**: `develop` (for staging) or `main` (for production)
   - **Environment to deploy to**: `staging` or `production`
   - **Commit SHA to rollback to**: paste the full SHA of the known-good commit (e.g. `a25e359...`)
5. Click **"Run workflow"**

The pipeline will:
1. Checkout the specified commit
2. Build and test it
3. Deploy to the selected environment

### Finding a known-good commit SHA

```bash
# List recent commits on develop with their SHAs
git log develop --oneline -10

# Or find the commit from a previous successful deployment
# Go to Actions → click the green run → the SHA is shown at the top
```

### Important notes

- Rollback **rebuilds from source** at the specified commit — it does not reuse a previously uploaded artifact
- Tests must still pass at the rollback commit; if they don't, the deployment is blocked
- Production rollback still requires manual approval via the GitHub Environment gate
- GitHub artifact retention is 90 days by default; `workflow_dispatch` rollback is not bounded by this since it rebuilds from source
- Rollback redeploys application code only. EF Core migrations are applied forward-only, so rolling back past a migration leaves the older code running against a newer schema. Verify schema compatibility before rolling back across a migration boundary

## Verification

- [ ] Pipeline runs green on a clean push to `develop`
- [ ] A deliberate failing test blocks deployment
- [ ] Rollback via `workflow_dispatch` executed successfully at least once


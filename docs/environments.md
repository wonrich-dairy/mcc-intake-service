# Azure Environment Documentation — MCC & Intake Service

## Overview

MCC & Intake Service is deployed to Azure App Service with **two isolated environments**: staging and production. Each environment has its own resource group, App Service plan, and App Service instance. Both environments connect to a shared Azure MySQL Flexible Server.

## Environments

| Property | Staging | Production |
|---|---|---|
| **Resource Group** | `rg-mcc-intake-staging` | `rg-mcc-intake-prod` |
| **Region** | `southeastasia` | `southeastasia` |
| **App Service Plan** | `plan-mcc-intake-staging` (F1 Free) | `plan-mcc-intake-prod` (F1 Free) |
| **App Service** | `app-mcc-intake-staging` | `app-mcc-intake-prod` |
| **URL** | https://app-mcc-intake-staging.azurewebsites.net | https://app-mcc-intake-prod.azurewebsites.net |
| **Swagger UI** | Available at `/swagger` | Disabled |

## Database

A shared Azure MySQL Flexible Server is used for this sprint:

| Property | Value |
|---|---|
| **Host** | `mcc-db.mysql.database.azure.com` |
| **Port** | `3306` |
| **Database** | `mccdb` |
| **Username** | `mccadmin` |
| **SSL** | Required |

> **Note:** Database credentials are managed via App Service Application Settings and are never committed to source code.

## Configuration

All configuration is resolved at **runtime** via Azure App Service Application Settings (environment variables). No secrets are stored in source code or built into the application.

### Application Settings

| Setting | Description | Where Set |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` or `Production` | App Service → Configuration |
| `ConnectionStrings__DefaultConnection` | MySQL connection string | App Service → Configuration |

### Secrets

Deployment credentials and database passwords are stored as **GitHub repository secrets** and used only by the CI/CD pipeline:
- `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING` — Azure publish profile for staging
- `AZURE_WEBAPP_PUBLISH_PROFILE_PROD` — Azure publish profile for production

## Access Control

- **Staging**: Accessible to all team members for testing
- **Production config**: Restricted to the DevOps role holder (Azure RBAC)

## Deployment Flow

1. Code merged to `develop` → CI/CD auto-deploys to **staging**
2. Staging verified → PR to `main` → manual approval → deploy to **production**

## Notes

- Azure for Students subscription restricts deployments to specific regions. Allowed regions: `southeastasia`, `indonesiacentral`, `uaenorth`, `eastasia`, `malaysiawest`
- Both environments use the F1 (Free) App Service plan tier
- Local development uses a containerised MySQL via docker-compose (see README)

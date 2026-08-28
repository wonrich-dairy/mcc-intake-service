# MySQL Database Documentation — MCC & Intake Service

## Overview

MCC & Intake Service uses a **shared Azure MySQL Flexible Server** (`mcc-db`) for staging and production. Local development uses a containerised MySQL instance via docker-compose.

## Database Instances

| Property | Local (Docker) | Azure (Staging & Production) |
|---|---|---|
| **Host** | `localhost:3307` | `mcc-db.mysql.database.azure.com` |
| **Port** | `3306` (container) / `3307` (host) | `3306` |
| **Database** | `mcc_intake` | `mccdb` |
| **User** | `mcc_user` | `mccadmin` |
| **SSL** | Not required | Required |
| **Server** | MySQL 8.0.45 (Docker) | Azure MySQL Flexible Server |

## Connection Strings

### Local Development (docker-compose)
```
Server=localhost;Port=3307;Database=mcc_intake;User=mcc_user;Password=DevPassword123!
```
> The Docker service name `mcc-intake-db` resolves within the compose network. From the host, use `localhost:3307`.

### Staging / Production (Azure)
```
Server=mcc-db.mysql.database.azure.com;Port=3306;Database=mccdb;User=mccadmin;Password=<password>;SslMode=Required
```
> Connection strings are stored as Azure App Service Application Settings (`ConnectionStrings__DefaultConnection`). Never committed to source.

## Entity Framework Migrations

### Creating a new migration
```powershell
cd mcc-intake-service
dotnet ef migrations add <MigrationName> --project src/MccIntakeService/MccIntakeService.csproj
```

### Applying migrations manually
```powershell
dotnet ef database update --project src/MccIntakeService/MccIntakeService.csproj
```

### Auto-migration on startup
The application automatically applies pending migrations at startup when `ASPNETCORE_ENVIRONMENT` is `Development` or `Staging`.

## Backup Strategy

| Environment | Backup Method |
|---|---|
| Local | Disposable — recreated from migrations via `docker-compose up` |
| Azure | Azure automated backups (managed by the shared DB admin) |

## Security

- Local dev uses a scoped `mcc_user` with credentials only for `mcc_intake` database
- Azure uses `mccadmin` credentials managed via App Service settings
- SSL is required for all Azure connections
- Credentials are managed via environment variables (local) and Azure App Service settings (cloud)
- No database passwords are committed to source code

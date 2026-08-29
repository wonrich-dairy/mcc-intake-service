# MySQL Database Documentation — MCC & Intake Service

## Overview

MCC & Intake Service uses a **dedicated MySQL database** isolated from other services. Each environment (staging, production, local) has its own database instance with scoped credentials.

## Database Instances

| Property | Local (Docker) | Staging (Azure) | Production (Azure) |
|---|---|---|---|
| **Host** | `localhost:3307` | Azure MySQL Flexible Server | Azure MySQL Flexible Server |
| **Database** | `mcc_intake` | `mcc_intake` | `mcc_intake` |
| **User** | `mcc_user` | `mcc_user` | `mcc_user` |
| **Server** | MySQL 8.0 (Docker) | Azure MySQL Flexible Server | Azure MySQL Flexible Server |

## Connection Strings

### Local Development (docker-compose)
```
Server=mcc-intake-db;Port=3306;Database=mcc_intake;User=mcc_user;Password=DevPassword123!
```
> The Docker service name `mcc-intake-db` resolves within the compose network. From the host, use `localhost:3307`.

### Staging / Production
Connection strings are stored as Azure App Service Application Settings (`ConnectionStrings__DefaultConnection`). Never committed to source.

## Entity Framework Migrations

### Creating a new migration
```powershell
cd src/MccIntakeService
dotnet ef migrations add <MigrationName>
```

### Applying migrations
```powershell
dotnet ef database update
```

### Migrations run automatically on startup
The application is configured to apply pending migrations at startup when `ASPNETCORE_ENVIRONMENT` is `Development` or `Staging`.

## Backup Strategy

| Environment | Backup Method |
|---|---|
| Local | Disposable — recreated from migrations via `docker-compose up` |
| Staging | Azure automated backups (7-day retention by default) |
| Production | Azure automated backups (7-day retention by default) + manual snapshots before major releases |

## Security

- Each environment uses a **separate MySQL user** (`mcc_user`) with credentials scoped only to the `mcc_intake` database
- No cross-service database access is permitted
- Credentials are managed via environment variables (local) and Azure App Service settings (cloud)
- Root database passwords are never used by the application

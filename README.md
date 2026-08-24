# MCC & Intake Service

Handles raw milk quality metrics at the Milk Chilling Center, bowser dispatch notes, and factory-intake condition logging, as part of the Wonrich Dairy Quality Monitoring & Traceability System.

## Tech stack
- ASP.NET Core (.NET 10) + Entity Framework
- MySQL 8.0
- Swashbuckle / Swagger UI for API documentation
- Docker / docker-compose for local development
- Deployed to Azure App Service (staging and production)

## Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- Git

## Getting started

### Option 1 — Docker Compose (recommended)
```powershell
git clone https://github.com/wonrich-dairy/mcc-intake-service.git
cd mcc-intake-service
docker-compose up --build
```
The API will be available at `http://localhost:5000` and Swagger UI at `http://localhost:5000/swagger`.

### Option 2 — .NET CLI
```powershell
git clone https://github.com/wonrich-dairy/mcc-intake-service.git
cd mcc-intake-service
dotnet restore src/MccIntakeService/MccIntakeService.csproj
dotnet run --project src/MccIntakeService/MccIntakeService.csproj
```
> Requires a MySQL instance running locally or a connection string configured in `appsettings.Development.json`.

## Swagger / API Documentation
- **Route:** `/swagger` (Swagger UI) and `/swagger/v1/swagger.json` (OpenAPI spec)
- **Available in:** Development and Staging environments
- **Disabled in:** Production
- XML documentation comments on controllers/actions are picked up automatically

## Environment variables

| Variable | Description | Default (docker-compose) |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Development` |
| `ConnectionStrings__DefaultConnection` | MySQL connection string | Set in docker-compose.yml |
| `MYSQL_ROOT_PASSWORD` | MySQL root password | `RootDevPassword123!` |
| `MYSQL_PASSWORD` | MySQL app user password | `DevPassword123!` |

## Branching strategy
- `main`: protected, production-ready
- `develop`: protected integration branch
- `feature/SCRUM-<key>-<description>`: work branches, merged into `develop` via reviewed PR

## Contributing
1. Branch off `develop`: `git checkout -b feature/SCRUM-<key>-<description>`
2. Open a PR into `develop` using the PR template (Jira key, summary, testing notes)
3. At least one approving review is required before merge

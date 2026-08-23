# MCC & Intake Service

Handles raw milk quality metrics at the Milk Chilling Center, bowser dispatch notes, and factory-intake condition logging, as part of the Wonrich Dairy Quality Monitoring & Traceability System.

## Tech stack
- ASP.NET Core + Entity Framework
- MySQL
- Docker / docker-compose for local development
- Deployed to Azure App Service (staging and production)

## Prerequisites
- .NET 10 SDK
- Docker Desktop
- Git

## Getting started
```powershell
git clone https://github.com/wonrich-dairy/mcc-intake-service.git
cd mcc-intake-service
dotnet restore
dotnet tool restore

# Point the service at a database, then create the schema
copy src\MccIntakeService\appsettings.Development.template.json src\MccIntakeService\appsettings.Development.json
dotnet dotnet-ef database update --project src\MccIntakeService

dotnet run --project src\MccIntakeService
```
Swagger UI is served at `/swagger` in every environment except Production.

> A MySQL container and compose file arrive with SCRUM-36 and SCRUM-39. Until then, point
> `ConnectionStrings:DefaultConnection` at any reachable MySQL 8 instance.

## Tests
```powershell
dotnet test
dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings
```
The suite runs against SQLite in memory, so no MySQL server is needed. `coverage.runsettings`
excludes generated EF migrations from the coverage figure.

## Configuration
| Key | Default | Purpose |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | *(empty)* | MySQL connection string. When empty the data layer is not registered. |
| `Database:ServerVersion` | `8.0.36-mysql` | MySQL version the schema targets; configured rather than auto-detected so start-up does not depend on the server being reachable. |
| `Intake:DailyCutoff` | `16:00` | Local time after which milk is no longer accepted. |
| `Intake:TimeZone` | `Asia/Colombo` | Zone the centre's wall clock and intake dates run on. |

## API
| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/consignments` | Register an arriving society consignment (SCRUM-6). |
| `GET` | `/api/consignments/{reference}` | Fetch one consignment by its `MCC-YYYYMMDD-SOCIETY-NN` reference. |
| `GET` | `/api/consignments` | List consignments filtered by society, date, date range or reference. |
| `GET` | `/api/societies` | Societies available for selection at the gate. |
| `GET` | `/api/societies/{id}` | Fetch one society. |

Domain rule failures return `application/problem+json` carrying a stable `code`; a consignment
arriving after the cutoff returns `422` with `code`, `cutoff` and `arrivalTime`.

## Database migrations
```powershell
dotnet dotnet-ef migrations add <Name> --project src\MccIntakeService --output-dir Infrastructure/Persistence/Migrations
dotnet dotnet-ef migrations script --idempotent --project src\MccIntakeService --output schema.sql
```

## Branching strategy
- `main`: protected, production-ready
- `develop`: protected integration branch
- `feature/SCRUM-<key>-<description>`: work branches, merged into `develop` via reviewed PR

## Contributing
1. Branch off `develop`: `git checkout -b feature/SCRUM-<key>-<description>`
2. Open a PR into `develop` using the PR template (Jira key, summary, testing notes)
3. At least one approving review is required before merge


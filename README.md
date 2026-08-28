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
dotnet restore
dotnet tool restore

# Point the service at a database, then create the schema
copy src\MccIntakeService\appsettings.Development.template.json src\MccIntakeService\appsettings.Development.json
dotnet dotnet-ef database update --project src\MccIntakeService

dotnet run --project src\MccIntakeService
```
Swagger UI is served at `/swagger` in every environment except Production.

Or bring the whole stack up in containers (SCRUM-39):
```powershell
docker-compose up --build
```
The API is then at `http://localhost:5000`, with Swagger UI at `http://localhost:5000/swagger`.

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
| `Intake:DailyCutoff` | `16:00` | Local time after which milk is no longer accepted. |
| `Intake:TimeZone` | `Asia/Colombo` | Zone the centre's wall clock and intake dates run on. |
| `Intake:MilkDensityKgPerLitre` | `1.03` | Density used to derive litres from the weight recorded at the gate. |

## API
| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/consignments` | Register an arriving society consignment (SCRUM-6). |
| `GET` | `/api/consignments/{reference}` | Fetch one consignment by its `MCC-YYYYMMDD-SOCIETY-NN` reference. |
| `GET` | `/api/consignments` | List consignments filtered by society, date, date range or reference. |
| `GET` | `/api/societies` | Societies for gate selection; `search`, `sortBy`, `descending`, `includeInactive` (SCRUM-51). |
| `GET` | `/api/societies/{id}` | Fetch one society. |
| `POST` | `/api/societies` | Register a supplying society (SCRUM-51). |
| `PUT` | `/api/societies/{id}` | Amend a society (SCRUM-51). |
| `POST` | `/api/societies/{id}/deactivate` | Retire a society so it cannot be selected for new consignments. |
| `POST` | `/api/societies/{id}/reactivate` | Return a retired society to service. |

There is deliberately no `DELETE` for societies: historical consignments must keep resolving to
their source, so a society is retired rather than removed.

Domain rule failures return `application/problem+json` carrying a stable `code`:

| Status | When |
| --- | --- |
| `400` | Invalid input, or a rule the values break — e.g. moving a society code that consignments already depend on. |
| `404` | A record addressed by the route does not exist. |
| `409` | A society code is already in use; the body carries `conflictingCode`. |
| `422` | A well-formed request referencing something absent, or intake closed for the day (`cutoff`, `arrivalTime`). |

### Quantities
Cans are weighed at the gate, so `POST /api/consignments` takes `quantityKg` per can. Litres are
derived from that weight using `Intake:MilkDensityKgPerLitre` and returned alongside it; they are
never submitted. Both figures are stored rather than litres being recomputed on read, so retuning
the density later cannot restate quantities already recorded. Consignment totals are summed from
the can breakdown, so a total always equals the cans it lists.

### Authorization
`Api/Infrastructure/IntakeRoles.cs` declares the roles and policies the service recognises.
`IntakePolicies.ManageSocieties` — satisfied by `MccManager` or `SystemAdministrator` — is
enforced on the four society write endpoints, which answer `401` when unauthenticated and `403`
when the caller holds neither role. Reads stay open, because an intake officer has to list
societies to pick one at the gate.

The identity behind those roles currently comes from `IntakeRoleHeaderHandler`, which reads the
`X-Intake-Role` header and trusts it. **That is a placeholder, not a security control**, so
start-up refuses the Production environment outright. SCRUM-34 replaces it with real
authentication; only the two registrations in `Program.cs` change, because a JWT bearer handler
yields the same role claims. The policies and the `[Authorize]` attributes stay as they are.

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

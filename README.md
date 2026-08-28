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
| `Intake:MilkDensityKgPerLitre` | `1.03` | Density used to derive litres from the weight recorded at the gate. |
| `Auth:Issuer` | `wonrich-auth` | Issuer stamped on tokens and required by every validator. |
| `Auth:Audience` | `wonrich-services` | Audience all four services accept. |
| `Auth:SigningKey` | *(empty)* | Symmetric signing key, at least 32 characters. Supplied per environment; never committed. |
| `Auth:AccessTokenMinutes` | `60` | Access token lifetime. |
| `Auth:RefreshTokenDays` | `7` | Refresh token lifetime. |

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

### Authentication and authorization
Authentication is centralised (SCRUM-34). `src/Wonrich.AuthService` issues signed JWTs;
`src/Wonrich.Auth` is the shared library every service uses to validate them. Validation is local
to each service — signature, issuer, audience and expiry — so no service calls the auth service to
decide whether a request is authentic.

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/auth/login` | Exchange a username and password for an access and refresh token. |
| `POST` | `/api/auth/refresh` | Exchange a refresh token for a fresh pair. |

Seven roles are configured in `Wonrich.Auth/Authorization/WonrichRoles.cs`, and each user holds
exactly one. `Api/Infrastructure/IntakeRoles.cs` holds only this service's mapping of roles to
what they may do: `ManageSocieties` (`MccManager`, `SystemAdministrator`) and
`RegisterConsignments` (those two plus `IntakeOfficer`). Guarded endpoints answer `401` when
unauthenticated and `403` when the role is wrong. Society reads stay open, because an intake
officer has to list societies to pick one at the gate.

Refresh tokens are single use — exchanging one revokes it — and are stored only as a hash, as are
passwords (PBKDF2-SHA256, per-password salt). Failed sign-ins are logged with the username, the
timestamp and the source address; never the password.

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


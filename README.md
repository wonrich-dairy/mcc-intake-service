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
Swagger UI is served at `/swagger` in every environment except Production. Every route requires a
token, so paste one from `POST /api/auth/login` into **Authorize** before trying an endpoint.

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
| `Auth:Issuer` | `wonrich-auth` | Issuer stamped on tokens and required by every validator. |
| `Auth:Audience` | `wonrich-services` | Audience all four services accept. |
| `Auth:SigningKey` | *(empty)* | Symmetric signing key, at least 32 characters. Supplied per environment; never committed. |
| `Auth:AccessTokenMinutes` | `60` | Access token lifetime. |
| `Auth:RefreshTokenDays` | `7` | Refresh token lifetime. |
| `QualityThresholds:MinimumFatPercent` | `3.5` | Lowest acceptable fat percentage. |
| `QualityThresholds:MinimumSnf` | `8.5` | Lowest acceptable solids-not-fat. |
| `QualityThresholds:MinimumCorrectedClr` | `26.0` | Lowest acceptable corrected CLR. |
| `QualityThresholds:MaximumWaterPercent` | `0.5` | Highest acceptable added water. |
| `QualityThresholds:WorstAcceptableStability` | `MarginallyStable` | Weakest alcohol-cascade grade still accepted. |
| `QualityThresholds:WorstAcceptableKqColour` | `Purple` | Furthest-reduced KQ shade still accepted. |

## API
| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/consignments` | Register an arriving society consignment (SCRUM-6). |
| `GET` | `/api/consignments/{reference}` | Fetch one consignment by its `MCC-YYYYMMDD-SOCIETY-NN` reference. |
| `GET` | `/api/consignments` | List consignments filtered by society, date, date range or reference. |
| `POST` | `/api/consignments/{reference}/quality-test/preview` | Derive CLR, SNF and TS and highlight breaches before submitting (SCRUM-7). |
| `POST` | `/api/consignments/{reference}/quality-test` | Record the panel and settle the verdict. |
| `GET` | `/api/consignments/{reference}/quality-test` | Read back the recorded panel. |
| `GET` | `/api/tanks` | The three chilling tanks with their running totals (SCRUM-52). |
| `GET` | `/api/tanks/pourable` | Consignments accepted at the gate and not yet poured. |
| `POST` | `/api/tanks/{code}/pours` | Pour an accepted consignment into a tank. |
| `GET` | `/api/tanks/{code}/manifest` | The tank's manifest; `date` filters the entries. |
| `GET` | `/api/dispatch-notes` | Bowser dispatch notes; `date` filters by dispatch date (SCRUM-8). |
| `GET` | `/api/dispatch-notes/{reference}` | One note, resolved to its contributing consignments. |
| `POST` | `/api/dispatch-notes` | Record a note and close the tanks it drew from. |
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

### Quality test panel
`src/Wonrich.QualityPanel` holds the panel logic once (SCRUM-50) so the MCC gate and the lab
cannot drift apart on what the same readings mean. It is a packable, versioned library consumed
by reference, never copied.

- **CLR correction** — the lactometer is calibrated at 27 °C, so a reading is corrected by
  0.2 per °C: added above that temperature, subtracted below. SNF is always derived from the
  *corrected* CLR.
- **Composition** — `SNF = (FAT × 0.22) + (CLR × 0.25) + 0.72`, and `TS = SNF + FAT`.
- **Alcohol cascade** — a state machine running 80% → 75% → 68% → clot-on-boiling, halting at the
  first negative. A negative means no clotting, and since each rung is gentler than the last, the
  remaining stages would pass too.
- **KQ colour** — a fixed enumeration running best (`Blue`) to worst (`White`) across seven shades. The numeric values
  are stored contract; new shades go on the end.

Thresholds are configuration, not constants: they are a commercial and seasonal decision the
centre retunes without a release. The formulae stay in code, because they are properties of milk.

### Gate testing
A consignment is tested once (SCRUM-7), and the record never changes afterwards: it is the
evidence behind accepting or rejecting a delivery the society is paid for. `preview` evaluates
readings without storing anything, so the officer sees the derived values and any breach before
committing to a verdict; both paths share one evaluation, so the figures shown are the figures
stored.

A positive clot-on-boiling refuses acceptance outright rather than leaving it to judgement, and a
rejection must name the failed parameter and its recorded value. Only the cascade stages actually
run are stored — anything submitted past the first negative is discarded, because the cascade
defines those as never having happened.

### Chilling tanks
Only a consignment accepted at the gate can be poured, and it goes into exactly one tank
(SCRUM-52). `pourable` lists what is eligible, so rejected and untested milk is never offered.
Pour time and officer identity are recorded with each entry.

The three tanks are plant, not reference data — they ship with the schema and there is no endpoint
to add or remove one. Quantities are copied onto the pour rather than read back through the
consignment: a manifest records what physically went in, and must keep reading the same way even
if the consignment's own figures are later restated. Filtering a manifest by date narrows the
entries but never the tank totals, because what a tank holds does not change with how it is
being looked at. A pour is filed under the centre's day, not UTC: between midnight and 05:30 the
two disagree, and the officer's day is the one the rest of the centre runs on.

### Bowser dispatch
A dispatch note (SCRUM-8) records which tanks a bowser was loaded from, how much came from each,
and the panel taken at loading. The reference is `DN-YYYYMMDD-NN`, issued per dispatch date.

The total is summed from the per-tank quantities rather than supplied, and a tank cannot give up
more than it currently holds — everything poured in, less anything already dispatched out.
Submitting a note **closes** every tank it drew from: milk poured in after the bowser left would
corrupt the manifest the note resolves through. Closing happens as part of recording the note, not
as a separate step a caller could forget.

Each source resolves to the consignments that contributed to it through the tank manifest, which
is what lets the factory trace a failure back to a society. Notes are read-only once submitted.

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
| `GET` | `/api/users` | List accounts; `search`, `role`, `activeOnly` (SCRUM-45). |
| `GET` | `/api/users/roles` | The six assignable roles, for the role picker. |
| `GET` | `/api/users/{id}` | Fetch one account. |
| `POST` | `/api/users` | Create an account. |
| `PUT` | `/api/users/{id}` | Amend an account, optionally resetting its password. |
| `POST` | `/api/users/{id}/deactivate` | Deactivate an account and revoke its refresh tokens. |
| `POST` | `/api/users/{id}/reactivate` | Return a deactivated account to service. |

Account administration is restricted to `SystemAdministrator`. Accounts are deactivated, never
deleted, so sign-in history keeps resolving to the account that made it, and a username cannot be
changed once created for the same reason. There is deliberately no `DELETE`.

Six roles are configured in `Wonrich.Auth/Authorization/WonrichRoles.cs`, and each user holds
exactly one. `Api/Infrastructure/IntakeRoles.cs` holds only this service's mapping of roles to
what they may do. `MccManager` and `SystemAdministrator` satisfy every policy; the rest are:

| Policy | Also satisfied by |
| --- | --- |
| `ManageSocieties` | — |
| `RegisterConsignments` | `IntakeOfficer` |
| `RecordQualityTests` | `IntakeOfficer`, `QualityAnalyst` |
| `PourToTanks` | `IntakeOfficer` |
| `RecordDispatchNotes` | — |

A bowser operator drives; signing milk out to the factory is the manager's record, which is why
there is no operator role. Guarded endpoints answer `401` when
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

### Dates

The MySQL provider is Oracle's `MySql.EntityFrameworkCore`, which writes a `DateOnly` but cannot read
one back: `MySqlDataReader` has no `DateOnly` support, so loading any entity holding one threw
`InvalidCastException`. `MccIntakeDbContext.ConfigureConventions` therefore stores every `DateOnly`
through `DateOnlyToDateTimeConverter`, keeping the column `date`. A date added to a new entity is
covered by that convention automatically.

Pomelo materialises `DateOnly` natively, but its newest release (9.0.0) targets EF Core 9 while this
solution is on EF Core 10, so adopting it would mean downgrading EF Core across every project.

Because the suite runs on SQLite, which maps `DateOnly` happily, `DateOnlyMappingTests` builds the
model against the MySQL provider — no server needed — so a provider-specific mapping fault fails CI
rather than QA.


## Branching strategy
- `main`: protected, production-ready
- `develop`: protected integration branch
- `feature/SCRUM-<key>-<description>`: work branches, merged into `develop` via reviewed PR

## Contributing
1. Branch off `develop`: `git checkout -b feature/SCRUM-<key>-<description>`
2. Open a PR into `develop` using the PR template (Jira key, summary, testing notes)
3. At least one approving review is required before merge

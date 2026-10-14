# Wonrich.MccIntake.Contracts

Event contracts the MCC and Intake service publishes on Kafka (SCRUM-141).

| Type | Topic | `eventType` header / field | When |
|---|---|---|---|
| `ConsignmentTestedEvent` | `wonrich.intake.consignment-tested.v1` | `ConsignmentTested` | An officer records a gate verdict, accept or reject |

Namespace: `Wonrich.MccIntake.Contracts.Events`.

## Wire format

- JSON, camelCase property names. Deserialize with `JsonSerializerDefaults.Web`.
- Message key is the society code (`society.code`), so events for one society stay in order.
- Headers: `x-correlation-id`, `eventId`, `eventType`, `schemaVersion`, `timestamp`.
- Delivery is at-least-once. Deduplicate on `eventId`.
- `verdict` is `Accept` or `Reject`. `failedParameter` and `failedValue` are set on a rejection.
- `occurredAtUtc` is when the officer took the verdict. A record captured offline keeps its
  original time; it is not the time it was synced.
- `isMarginal` is true for an accepted consignment whose tightest measure sat within the
  configured margin of its limit (default 10% of the measure's scale). It is false on a rejection.

```csharp
using System.Text.Json;
using Wonrich.MccIntake.Contracts.Events;

var tested = JsonSerializer.Deserialize<ConsignmentTestedEvent>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
```

## Versioning

The package's major version follows the topic's `schemaVersion`: 1.x.y is v1. An added optional
field is a minor bump on the same topic; a rename or removal is 2.0.0 together with a new `.v2`
topic. Publish by pushing a tag `contracts-v<Version>` on a commit that is on develop or main.

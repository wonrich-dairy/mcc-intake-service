# Kafka events (SCRUM-141)

The MCC and Intake service publishes one event: **ConsignmentTested**, on
`wonrich.mcc.consignment-tested.v1`, each time an officer records a gate verdict, accepted or
rejected. Traceability consumes it into the daily society aggregate (consumer group
`traceability-consignment-tested`), so the QCO sees acceptance rates that include milk turned
away at the gate.

The contract is `ConsignmentTestedEvent` in the `Wonrich.MccIntake.Contracts` package
(`src/Wonrich.MccIntake.Contracts`, see its README for the wire format and versioning).

## How it is published

1. `QualityTestService.RecordAsync` adds the verdict and a row in `outbox_messages` in the **same
   SaveChanges**. If the save or an enclosing transaction rolls back (an offline sync record that
   fails, for example), the event goes with it.
2. `OutboxRelayService` polls the outbox every 5 seconds and publishes pending rows in order,
   keyed by society code, with the headers `eventId`, `eventType`, `schemaVersion`, `timestamp`
   and `x-correlation-id` (the request's `X-Correlation-ID`).
3. If the broker is down, the officer's submission still succeeds. The row stays `Pending` and is
   retried with backoff; after `Outbox:MaxRetries` (20) failures it is marked `Poisoned` and a
   copy is parked on `wonrich.dlq.mcc-consignment-tested.v1`.

Delivery is at-least-once: consumers deduplicate on `eventId`.

## Offline records

A panel captured offline (SCRUM-10) is published when it is synced, but `occurredAtUtc` is the
time the officer took it: the client sends `testedAtUtc` with the queued panel, and the record is
stored with that time. A time more than 5 minutes in the future is refused.

## Marginal

`isMarginal` is true for an accepted consignment whose tightest measure
(`MarginToThreshold.Tightest`) sat within `ConsignmentTested:MarginalMargin` of its limit, as a
fraction of that measure's scale. The default is 0.10 (10%). With the default thresholds that
makes, for example, any SNF below 9.35 marginal.

## Configuration

| Setting | Default | |
|---|---|---|
| `Kafka__BootstrapServers` | empty | Without it, events wait in the outbox (nothing is lost) |
| `Kafka__SecurityProtocol`, `Kafka__SaslMechanism`, `Kafka__SaslUsername`, `Kafka__SaslPassword` | Plaintext | Event Hubs on staging, as in processing-service |
| `ConsignmentTested__Topic` | `wonrich.mcc.consignment-tested.v1` | |
| `ConsignmentTested__MarginalMargin` | `0.10` | |
| `Outbox__RelayEnabled` | `true` | |
| `Outbox__MaxRetries` / `Outbox__BatchSize` | `20` / `20` | |
| `Outbox__DeadLetterTopic` | `wonrich.dlq.mcc-consignment-tested.v1` | |

The topics must exist on the broker (auto-creation is off): see wonrich-infra `kafka/topics.env`.

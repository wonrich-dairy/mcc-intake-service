# Kafka — connecting the intake service (SCRUM-88)

The broker, the topic names, partitions, retention and consumer groups are defined once, in
`processing-service`: [`docs/kafka.md`](https://github.com/wonrich-dairy/processing-service/blob/develop/docs/kafka.md)
and [`infra/kafka/topics.env`](https://github.com/wonrich-dairy/processing-service/blob/develop/infra/kafka/topics.env)
there are the source of truth. This page covers only what is specific to this service: which
topic it owns, and how it reaches the broker in each environment.

## What this service produces

| Topic | Partitions | Retention | Consumed by |
|---|---|---|---|
| `wonrich.intake.lab-results.v1` | 3 | 7 days | Processing Service, group `processing-lab-results` |

One producer per topic, so this service is the only writer of lab results. It consumes nothing,
and it has no dead-letter topic of its own: dead letters are named for the consumer group that
failed, and every consumer group belongs to the processing service.

## Connection settings

Supplied as configuration, never committed. The key names are identical to the processing
service's, and follow the ASP.NET Core configuration binder's double-underscore convention, so they
work unchanged as App Service settings.

| Setting | Local | Staging (Event Hubs) |
|---|---|---|
| `Kafka__BootstrapServers` | `kafka:9092` in compose, `localhost:29092` from the host | `<namespace>.servicebus.windows.net:9093` |
| `Kafka__SecurityProtocol` | `Plaintext` | `SaslSsl` |
| `Kafka__SaslMechanism` | — | `Plain` |
| `Kafka__SaslUsername` | — | `$ConnectionString` — the literal string, not a variable |
| `Kafka__SaslPassword` | — | the connection string (secret) |

`$ConnectionString` is Event Hubs' convention: the username is that literal token and the password
carries the real credential. In a shell, quote it — `'$ConnectionString'` — or it expands to
nothing and authentication fails with an unhelpful error.

## Local: one broker, shared across the repositories

Each repository has its own compose file so it can be worked on alone. The broker is in
`processing-service`'s, because that is the repository that owns the topic definitions and the
script that creates them. Running a second broker here would give this service its own copy of
the topics, and a lab result published here would never reach the processing service.

Instead, this service joins the broker's Docker network. `docker-compose up` on its own is
unchanged — no broker, no Kafka settings. Opting in is a second compose file:

```bash
# 1. In processing-service: the broker, and the topics
docker-compose up -d kafka kafka-init

# 2. Here: the service, attached to that broker
docker-compose -f docker-compose.yml -f docker-compose.kafka.yml up -d
```

[`docker-compose.kafka.yml`](../docker-compose.kafka.yml) adds the `Kafka__*` settings from
`.env` (defaulting to the local broker) and attaches the API container to the network
`processing-service_default`, which is where a checkout called `processing-service` puts its
broker. Compose names a project after its folder, so if that checkout has a different name, set
`KAFKA_NETWORK` in `.env` to match — `docker network ls` shows it.

The network is declared `external`. If the broker is not up, `docker-compose up` stops with
`network processing-service_default declared as external, but could not be found` rather than
creating an empty network of the same name, which would then collide with the broker's when it
does start.

To check the wiring from where the service sits, run Kafka's own tools inside the container's
network namespace:

```bash
api=$(docker-compose ps -q mcc-intake-api)
docker run --rm --network "container:$api" apache/kafka:3.9.1 \
  /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --list
```

> In Git Bash, wrap paths that start with `/` in `sh -c '...'` or set `MSYS_NO_PATHCONV=1`, or
> MSYS rewrites `/opt/kafka/...` into a Windows path before Docker sees it.

## Staging: the shared Event Hubs namespace

Staging uses Azure Event Hubs over its Kafka endpoint. There is one namespace for the whole
system, provisioned from `processing-service` with `./infra/azure/eventhubs.sh staging`; it is
`evhns-wonrich-processing-staging` in `rg-processing-staging`. Every service points at that one
namespace, or the topics are not shared and the events go nowhere.

This service only sends, and only to one hub, so it should not hold the namespace's root key. A
send-only rule scoped to its topic is the narrowest credential that works:

```bash
az eventhubs eventhub authorization-rule create \
  --name intake-send --rights Send \
  --eventhub-name wonrich.intake.lab-results.v1 \
  --namespace-name evhns-wonrich-processing-staging \
  --resource-group rg-processing-staging --output none

conn=$(az eventhubs eventhub authorization-rule keys list \
  --name intake-send \
  --eventhub-name wonrich.intake.lab-results.v1 \
  --namespace-name evhns-wonrich-processing-staging \
  --resource-group rg-processing-staging \
  --query primaryConnectionString -o tsv)

az webapp config appsettings set --name app-mcc-intake-staging \
  --resource-group rg-mcc-intake-staging --output none --settings \
  "Kafka__BootstrapServers=evhns-wonrich-processing-staging.servicebus.windows.net:9093" \
  "Kafka__SecurityProtocol=SaslSsl" \
  "Kafka__SaslMechanism=Plain" \
  "Kafka__SaslUsername=\$ConnectionString" \
  "Kafka__SaslPassword=$conn"
```

The connection string is a secret: read it when you need it, set it, do not save it to a file.
A hub-scoped rule includes `EntityPath=wonrich.intake.lab-results.v1` in the connection string,
which the Kafka endpoint accepts as-is.

## What this ticket does not do

Nothing in the service produces yet. This is the connection configuration and the local wiring,
so that the story which publishes lab results can read `Kafka__*` and find a broker on the other
end. The settings are read by no code until then.

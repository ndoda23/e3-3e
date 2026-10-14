# Tests

| Project | What it covers | Needs |
|---|---|---|
| CreditFlow.Domain.UnitTests | Aggregates, state machine, scoring, schedules, money | — |
| CreditFlow.Application.UnitTests | Application services and handlers | — |
| CreditFlow.ArchitectureTests | Clean Architecture layer rules (NetArchTest) | — |
| CreditFlow.IntegrationTests | API, EF Core, outbox, concurrency against a real SQL Server | Docker (Testcontainers) |

Run everything:

    dotnet test

Unit and architecture tests only (no Docker):

    dotnet test --filter "FullyQualifiedName!~IntegrationTests"
# AuthCraft API Constitution

## Core Principles

### I. Preserve Layered Responsibilities
Keep HTTP handling in the API controllers, application behavior in the
Application services and interfaces, business concepts in Domain, and
persistence concerns in Infrastructure and the database project. Reuse the
existing boundaries instead of introducing parallel patterns.

### II. Enforce Security at the API Boundary
Treat authentication, authorization, tenant isolation, and input validation as
server responsibilities. Never rely on browser-side guards or client-provided
claims as proof of authorization. Do not commit credentials; use development
user-secrets and the configured production secret store.

### III. Preserve API and Data Contracts
Preserve established routes, request/response shapes, and persisted data
semantics unless a change explicitly includes a compatible transition. Keep
database schema changes within the existing SQL project and account for their
deployment impact. SQL column names use lowercase snake_case.

### IV. Keep Changes Verifiable
Add or update focused tests when behavior changes. The deployment workflow
builds with `dotnet build authcraft-api/authcraft-api.csproj --configuration
Release`; changes to the SQL project must also build with MSBuild
(`authcraft-db/authcraft-db.sqlproj`).

### V. Keep Configuration Safe and Explicit
Follow the existing ASP.NET Core configuration patterns. Keep secrets out of
source control, and document any new configuration keys and their intended
development and production sources.

### VI. Readable Code
Use semantic, descriptive variable names; avoid single-letter variables except
trivial short loop counters.

## Technical Constraints

- The API targets .NET 10 and uses the Application, Domain, Infrastructure, and
  API projects.
- The SQL Server database project is maintained under `authcraft-db/`.
- The deployment workflow builds the API and database project before
  deployment; changes must not assume application code alone is the full
  deployment.

## Development Workflow

- For a new feature, use Spec Kit to define the specification, plan, and tasks
  before implementation; clarify material behavior and security questions
  before planning.
- Inspect the affected layer, related controllers/services, and database
  configuration before proposing a design.
- Run focused tests when available and the applicable build commands before
  considering a change complete.

## Governance

This constitution records established API constraints for Spec Kit planning.
If a change conflicts with a principle, describe the impact and include a
compatibility, security, or deployment plan. Amend these principles only when
repository practices or project decisions change.

**Version**: 1.0.0 | **Ratified**: 2026-11-02 | **Last Amended**: 2026-11-02

<!--
Sync Impact Report
Version change: 1.1.0 -> 2.0.0
Modified principles: I. Preserve Layered Responsibilities; VII. Use Table-Specific Infrastructure Repositories
Added sections: None
Removed sections: None
Follow-up TODOs: None
-->

# AuthCraft API Constitution

## Core Principles

### I. Preserve Layered Responsibilities
HTTP handling MUST remain in API controllers, business concepts in Domain, and
persistence concerns in Infrastructure and the database project. Application
MUST contain only services that implement business logic. All data access code
and its abstractions MUST reside in Infrastructure; Application MUST NOT
contain data access or data-access abstractions. Changes MUST reuse these
boundaries rather than introduce parallel patterns.

### II. Enforce Security at the API Boundary
Authentication, authorization, tenant isolation, and input validation MUST be
enforced by the server. Browser-side guards and client-provided claims MUST NOT
be treated as proof of authorization. Credentials MUST NOT be committed; use
development user-secrets and the configured production secret store.

### III. Preserve API and Data Contracts
Established routes, request and response shapes, and persisted data semantics
MUST remain compatible unless a change explicitly includes a compatible
transition. Database schema changes MUST remain within the existing SQL project
and account for deployment impact. SQL column names MUST use lowercase
snake_case.

### IV. Keep Changes Verifiable
Behavior changes MUST add or update focused tests. The deployment workflow MUST
build with `dotnet build authcraft-api/authcraft-api.csproj --configuration
Release`; changes to the SQL project MUST also build with MSBuild
(`authcraft-db/authcraft-db.sqlproj`).

### V. Keep Configuration Safe and Explicit
Changes MUST follow existing ASP.NET Core configuration patterns. Secrets MUST
remain out of source control. New configuration keys MUST be documented with
their intended development and production sources.

### VI. Write Readable Code
Variables MUST use semantic, descriptive names. Single-letter variable names
MUST be avoided except for trivial counters in short loops.

### VII. Use Table-Specific Infrastructure Repositories
For each database table accessed by application data, Infrastructure MUST
provide a dedicated repository class for that table. Every abstraction for
accessing that data, including its repository interface, MUST reside in
Infrastructure; the interface MUST be declared inside its repository class.
All reads and writes for the table MUST go through that repository. This keeps
table-specific data access explicit and its contract alongside its owning
repository.

## Technical Constraints

- The API targets .NET 10 and uses the Application, Domain, Infrastructure, and
  API projects.
- The SQL Server database project is maintained under `authcraft-db/`.
- The deployment workflow builds the API and database project before
  deployment; changes MUST NOT assume application code alone is the full
  deployment.

## Development Workflow

- For a new feature, use Spec Kit to define the specification, plan, and tasks
  before implementation; clarify material behavior and security questions
  before planning.
- Inspect the affected layer, related controllers and services, and database
  configuration before proposing a design.
- Run focused tests when available and applicable build commands before
  considering a change complete.

## Governance

This constitution records established API constraints for Spec Kit planning.
Proposed amendments MUST explain their rationale and impact, update the
constitution version and amendment date, and follow the repository's review
process. Version changes MUST follow semantic versioning: MAJOR for
backward-incompatible governance changes, MINOR for new principles or material
expansion, and PATCH for clarifications and non-semantic refinements.
Planning and code reviews MUST check changes against these principles. When a
change conflicts with a principle, its proposal MUST describe the impact and
include a compatibility, security, or deployment plan; amend the principle
when the project decision changes.

**Version**: 2.0.0 | **Ratified**: 2026-11-02 | **Last Amended**: 2026-11-05

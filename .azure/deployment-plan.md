# Academics component preparation

Status: Preparing

## Approval and ownership

This component implements the already approved six-repository ADLC plan.
The user approved implementation and normal short-lived demo costs.
`contoso-platform` is the only azd/Bicep stack owner: this repository must not
create a second Azure environment, shared-resource owner, or `azure.yaml`.
Its preparation artifact covers application code and the packaging interface.

## Confirmed decisions

- Independent Academics service and SQL database; .NET 10 LTS on App Service.
- Own courses, departments and instructors; publish `contracts/academics.openapi.yaml`.
- Entra JWT validation, Reader/Registrar authorization, managed-identity SQL.
- Application Insights with no sensitive payload logging.
- Explicit migrations/seed execution, not startup database creation.
- Unit/API/contract checks and pinned GitHub CI/governance controls.
- Preserve the original repository and source license.

## Azure context

The explicitly selected subscription and tenant are retained in the private
platform preparation context, not published in this application repository.
East US 2 is the provisional region, not a user-confirmed selection.
No Azure provisioning, role grants, or deployment is authorized by this
component artifact; platform preparation/validation owns the actual deployment.

## Preparation work

Inspect legacy Academics behavior and agree the platform packaging/configuration
interface. Implement the API, isolated persistence, telemetry, contract,
explicit data tools and focused checks. Report verified capabilities and gaps.
Do not claim production readiness, deployed telemetry, or SQL/Entra integration
from local substitutes.

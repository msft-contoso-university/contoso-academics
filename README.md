# Contoso Academics

.NET 10 catalog API for courses, departments and instructors in the six-repository
Contoso University ADLC demo. Academics owns its database and versioned DTO
contract; enrollment belongs elsewhere.

## Evidence and boundaries

Implemented locally: HTTP CRUD, DTO projection, bounded pagination, relational
SQLite tests and real RSA-signed JWT verification using test-only static OpenID
metadata (not a fake authentication handler). The latest owner-reported local
run passed 23 tests; format checks and publish also passed. SQLite
`EnsureCreated` is **not** SQL Server/Azure evidence.

Candidate CI is defined for Ubuntu 24.04 with a disposable SQL Server 2022
loopback container, actual EF migrations and actual HTTP response samples.
The workflow pins the official SQL Server image to
`mcr.microsoft.com/mssql/server@sha256:e10cd49443c94dbdcb7a050987db22ff4d8c87052ac53768abe1ed848d3fcef6`;
the integration owner verified its manifest/config hashes and linux/amd64
platform against public MCR metadata. The locally generated SQL migration
artifact contains four tables, but this is not evidence of SQL execution.
Local isolated Python dependency installation hit a `files.pythonhosted.org`
TLS handshake failure; no insecure workaround was used. Complete OpenAPI,
actual-response contract and real SQL validation remain pending hosted CI.

Real SQL CI success, Azure SQL/Entra/AppInsights integration and platform
deployment evidence remain pending. No claim of production or trace readiness
is made. There is no component-owned Azure stack or deployment workflow.

## API contract

[`contracts/academics.openapi.yaml`](contracts/academics.openapi.yaml) is OpenAPI
3.1 with JSON Schema 2020-12. Each resource at `/api/v1/courses`,
`/api/v1/departments`, `/api/v1/instructors` supports GET list, GET `/{id}`, POST,
PUT `/{id}` and DELETE `/{id}`. IDs are positive int32 values.

- Lists use `page` (int32, default 1, nonpositive normalizes to 1), fixed size 50,
  id ordering and `{courses|departments|instructors, count, pages, currentPage}`.
  `int.MaxValue` is safe and returns an empty page.
- POST returns 201 plus the view and Location; any supplied nonnull id is 400.
  PUT permits omitted/null id or the route id, returning 204. DELETE returns 204.
- Missing items return 404. Invalid input/reference/id mismatch is 400.
  Referenced instructors/departments cannot be deleted (409); SQL FK/unique and
  concurrency conflicts are 409 ProblemDetails. Other errors are generic 500.
  ProblemDetails allows extensions; 401/403 do not guarantee a response body.
- Unknown input properties are currently ignored. Views serialize all required
  fields, including nullable department instructor fields, without entity cycles.
- Budget persists as SQL `money` (four decimal places), within
  -922337203685477.5808 through 922337203685477.5807.

Intentional v1 differences from legacy: explicit FK IDs and DTOs, bounded lists
for all resources, date-only/camelCase payloads, 204 deletion and removal of the
no-op cancel-enrollment route. There is no cross-service enrollment FK.

### Authorization

Tenant, audience, issuer, signature and lifetime are validated, with HTTPS
metadata and a one-minute clock skew. Scope names are exact space-delimited
matches, not substring matches.

| Caller | Read | Write |
| --- | --- | --- |
| Delegated (`idtyp != app`) | `academics.read` AND `Reader` or `Registrar` role | `academics.write` AND `Registrar` |
| Application (`idtyp=app`) | `Academics.Lookup` role, **only GET `/api/v1/courses/{id}`** | Never |

`catalog-read` permits delegated readers only. The separate `course-lookup`
policy additionally permits the app-only course detail lookup; it grants no
course enumeration, department/budget read or instructor read access.
App registrations must emit `idtyp`. Spoofed delegated roles/scopes do not let an
app-only token bypass these restrictions, and a delegated Lookup-only token
cannot read. Tests cover restricted app lookup and rejection of unsigned JWTs.
They use real signature validation but do not prove live Entra integration.

## Configuration and operation

Provide settings through the environment/approved platform configuration, not
committed credentials:

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__Academics` | SQL connection; use managed identity authentication in Azure with encryption and certificate validation |
| `AzureAd__TenantId` | Configured Entra tenant GUID |
| `AzureAd__Audience` | Intended API audience |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Required outside Development/Testing for normal service startup |
| `AZURE_CLIENT_ID` | Optional user-assigned managed identity client id |

System-assigned MI is supported; `AZURE_CLIENT_ID` selects a UAMI for
`DefaultAzureCredential`. Production uses `UseAzureMonitor` with its
`TokenCredential` set to `new DefaultAzureCredential()` and OpenTelemetry
`service.name=contoso-academics`. With AppInsights `disableLocalAuth`, the platform
must first assign scoped **Monitoring Metrics Publisher** to the identity.
That configuration alone is not evidence that traces have arrived. Do not also
enable duplicate auto-instrumentation.

Anonymous `/health/live` checks process responsiveness. `/health/ready` requires
database connectivity, known migrations and no pending migrations. SQLite
`EnsureCreated` is intentionally not migration-ready.

No automatic startup migrations. Use separate bootstrap, migration and runtime
database identities; only the migration identity gets reviewed schema-change
permissions, and runtime is DML-only. From the published API directory:

```sh
dotnet Contoso.Academics.dll --migrate
dotnet Contoso.Academics.dll --migrate --seed-demo
dotnet Contoso.Academics.dll
```

Demo seed data is synthetic, version-marked, transactional and repeatable.
Seeding refuses existing unmarked data. Review migration compatibility before
promotion; reverting application code does not undo database changes.

## Local verification

Use SDK 10.0.404. All NuGet packages and EF tooling are pinned; restore uses
committed lockfiles. No solution file exists.

```sh
dotnet restore tests/Contoso.Academics.Tests/Contoso.Academics.Tests.csproj --locked-mode
dotnet format whitespace tests/Contoso.Academics.Tests/Contoso.Academics.Tests.csproj --verify-no-changes --no-restore
dotnet format style tests/Contoso.Academics.Tests/Contoso.Academics.Tests.csproj --verify-no-changes --no-restore
dotnet test tests/Contoso.Academics.Tests/Contoso.Academics.Tests.csproj --no-restore
dotnet tool restore --tool-manifest dotnet-tools.json
dotnet ef migrations script --idempotent --project src/Contoso.Academics/Contoso.Academics.csproj --output artifacts/sql/academics.sql
```

CI builds the migration sources already committed in the checkout before
generating its script with `--no-build`. When adding a migration locally, build
again after generation before using `script --no-build`: `migrations add` builds
before creating the new migration and otherwise leaves a stale assembly.

Without `ACADEMICS_TEST_SQL_CONNECTION`, tests use isolated SQLite. With it, tests
only accept `localhost,1433` or `127.0.0.1,1433`, replace InitialCatalog with random
`AcademicsTest_GUID` databases, run `MigrateAsync`, and drop only their databases.
CI creates a fresh masked random SA password, publishes SQL on loopback only,
bounds readiness retries and always removes the exact run-specific container.
`TrustServerCertificate=true`/sqlcmd `-C` is limited to this disposable self-signed
test container; never use it for production SQL or disable NuGet/cloud TLS.

Set `ACADEMICS_CONTRACT_SAMPLES_DIRECTORY` to `artifacts/contract-samples` before
tests to export actual successful responses. Then, in an isolated Python 3.12
venv (Windows executable is `.venv-contracts/Scripts/python.exe`):

```sh
python -m venv .venv-contracts
.venv-contracts/bin/python -m pip install -r requirements-contracts.txt
.venv-contracts/bin/python -m pip check
.venv-contracts/bin/python tools/verify_contracts.py
.venv-contracts/bin/python tools/verify_contracts.py --samples artifacts/contract-samples
```

Sample envelopes are `{"schema":"Instructor","body":{...}}`. The verifier rejects
remote/unresolved references, validates every sample with Draft 2020-12 and
format checking, and requires Instructor, Department, Course and InstructorPage
categories. Missing directories, empty samples or missing categories fail.
Omitting `--samples` checks the document only, not runtime conformance.

## Candidate CI and governance

Pushes (including bootstrap branches), specified PR events and manual dispatch
run all relevant jobs without path filters. Checks cover locked restore,
whitespace/style, NuGet audit (findings and errors fail), contract validation,
SQL HTTP/JWT/actual-response tests, build/publish and an idempotent SQL script.
The SHA-named candidate includes API files, contract, migration SQL, commit/run
metadata and SHA-256 checksums. On PRs the tested SHA can be a synthetic merge
commit; it must not be confused with approval of the contributor's head commit.

**Candidate != approved/trusted release.** Candidate packaging runs independently
to provide review material, even if another check fails. `Required checks`
runs always and rejects failed, cancelled or unexpected skipped prerequisites.
Only story intent is skipped on push/manual dispatch. Workflow permissions are
read-only contents/issues/pull-requests, with no Azure credentials, OIDC grant,
deployment or merging authority.

PR story intent uses the pinned central engineering composite action and its own
pinned governance tool, never candidate policy. It requires 1-4 closing issue
lines in the approved six-repo estate and structured story sections: User story,
Goal, Scope, Acceptance criteria (task list), Verification, Affected repositories,
Risk and rollback. The latest `ready-for-agent` addition must be audited to a
real User with current write+ access. No Academics story is accepted yet: PR
failure until human acceptance is intentional, not something to bypass.

Humans own acceptance, codeowner review and merge. Repository protection will be
configured separately after actual candidate checks are known. Platform owns
promotion/deployment using the **exact reviewed commit and immutable
package/image**, never floating `main`. Roll back only to a known compatible
immutable release after reviewing database/data compatibility.

See [AGENTS.md](AGENTS.md) and the
[engineering constitution](https://github.com/msft-contoso-university/contoso-engineering/blob/main/docs/constitution.md).

## License

MIT-derived original work. The original **Copyright (c) 2024 Gibs** notice is
preserved in [LICENSE](LICENSE).

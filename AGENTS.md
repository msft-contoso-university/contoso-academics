# Academics contributor boundaries

Read the [central engineering constitution](https://github.com/msft-contoso-university/contoso-engineering/blob/main/docs/constitution.md)
before starting work. This file adds component constraints; it does not replace
central governance.

- Work only from accepted intent within explicitly assigned paths. Stop for
  ambiguous authorization or conflicting concurrent edits.
- Link 1-4 closing issue lines to the approved six repositories. Linked stories
  need User story, Goal, Scope, Acceptance criteria (task list), Verification,
  Affected repositories, and Risk and rollback headings.
- Acceptance requires the `ready-for-agent` label's latest addition to be audited
  to a real User with current write+ permission. Agents must never grant their
  own acceptance, apply acceptance labels, approve reviews, merge or deploy.
  Do not bypass an expected story-gate failure.
- Require human review, security review and passing candidate CI. A candidate
  package is not a trusted release and successful automation grants no authority.
- Keep the OpenAPI DTO contract and actual HTTP response validation aligned.
  Preserve real JWT verification, explicit FK IDs, bounded pages and generic
  errors. Never add cross-service enrollment foreign keys.
- Keep app-only `idtyp=app` + `Academics.Lookup` limited to GET
  `/api/v1/courses/{id}` under `course-lookup`. Never extend it to enumerations,
  departments/budgets, instructors or writes; `catalog-read` is delegated-only.
- Pin dependencies and review lockfile changes. Do not disable TLS, weaken token
  validation or substitute fake authentication for security evidence.
- Never commit credentials or personal data; never log tokens, passwords,
  connection strings or PII. Use synthetic fixtures only. Do not export source
  or sensitive evidence to third-party services.
- Database changes require a reviewed migration and compatibility/rollback plan.
  Separate bootstrap, migration and runtime identities; runtime is DML-only.
  Startup must not automatically migrate or seed.
- Platform owns Azure azd/Bicep, lifecycle, deployment and promotion. Do not add a
  second infrastructure stack or deployment workflow. Promotion uses an exact
  reviewed commit and immutable package/image, never floating `main`.
- Do not hand-author gh-aw generated lock files or retry blocked inference
  without explicit authorization. Do not alter protections or repository/cloud
  settings as part of component implementation.
- Coordinate before running .NET build/test/format against a shared checkout.

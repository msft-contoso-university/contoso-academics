## Linked intent

<!-- Add 1-4 standalone Closes/Fixes/Resolves issue lines in the approved six-repo
     msft-contoso-university estate, e.g. Closes owner/repository#123.
     The linked issue, not this template, must contain the structured story.
     Do not invent acceptance or apply ready-for-agent yourself. -->

## Change

Describe the component change and intentional contract differences.

## Verification

- [ ] Exact candidate SHA and CI evidence linked
- [ ] SQL container/migration, HTTP/JWT and actual-response contract evidence reviewed
- [ ] Security and dependency results reviewed; no credentials or personal data exposed
- [ ] Breaking changes and consumer impact identified

## Risk and rollback

Describe operational/data risks and the last compatible immutable package.
Database rollback requires explicit compatibility review; reverting code does not undo data.

## Human review

- [ ] Linked story has audited human acceptance from a current write+ collaborator
- [ ] Human codeowner review requested

Agents may report evidence, but must not grant acceptance, approve, merge or deploy.
Candidate artifacts are not approved/trusted releases. Promotion is platform-owned.

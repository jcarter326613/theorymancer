# Follow-Up Work

## Production Security And Deployment

- Split GitHub Actions access into least-privilege service accounts for shared
  infrastructure, development deployment, production deployment, and Guild
  Wars 2 asset synchronization. Bind each Workload Identity principal to its
  intended environment and workflow. The current shared deployment identity no
  longer has permission to modify project IAM.
- Provision `theorymancer.com` DNS and edge routing before enabling production
  browser authentication. Route central API and Guild Wars 2 resource-server
  namespaces directly as defined in `architecture.md`.
- Support signing-key rotation by publishing both active and retiring JWKS keys
  until every access token signed by the retiring key has expired, including
  cache and clock-skew tolerance.
- Make refresh-token rotation retry-safe for a client that loses the token
  response after the server consumes the old token. Preserve reuse detection
  for genuinely stolen tokens.
- Split environment Terraform plan and apply jobs so production reviewers
  approve the generated environment plan rather than the workflow before
  planning. Shared Terraform already uses a reviewed plan.
- Replace mutable GitHub Action and container-base tags with pinned immutable
  revisions or digests.

## Deployment Prerequisites

- Configure the Google provider in each Identity Platform tenant using the
  required secret-bearing administrative process.
- Create and pin the IP-hash Secret Manager payload version in each
  environment.
- Bootstrap the first platform administrator after the operator's initial
  website sign-in, using the documented Firestore procedure.
- Complete the documented staged development-origin bootstrap: deploy a web
  service, add its generated `run.app` origin to Identity Platform through the
  shared root, then reapply the development environment with that origin.

## Integration Coverage

- Add deployed-development integration checks for Firebase sign-in, Firestore
  access, KMS JWKS publication, CORS/runtime web configuration, and
  authenticated Guild Wars 2 asset access.
- Test JWKS transport/malformed-response failures and key rollover against a
  deployed resource server.
- Decide whether multi-instance resource-server DPoP replay prevention needs a
  shared replay store. Asset GETs are immutable, but the current replay cache
  is process-local.

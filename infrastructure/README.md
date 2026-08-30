# Infrastructure

Terraform uses one GCP project initially to minimize fixed cost. State and
resources remain isolated by ownership:

- `bootstrap` enables APIs and creates deployment prerequisites.
- `shared` owns project-global Identity Platform/Firebase configuration and the
  separate development and production KMS signing keys.
- `environments/development` deploys the shared integration environment.
- `environments/production` deploys the production environment.
- `modules` contains reusable environment resources.

The environment roots provide public Cloud Run services, named Firestore
databases, Secret Manager containers, and Cloud Storage buckets. The manually
applied bootstrap root owns runtime service accounts and project IAM bindings.

## Prerequisites

Install Terraform 1.8 or newer and authenticate locally with a Google Cloud
identity that can administer the selected billing-enabled project. Create one
GCS bucket before initializing Terraform; Terraform cannot store state in a
bucket that it has not yet created.

```bash
gcloud storage buckets create gs://theorymancer-terraform-state \
  --project=theorymancer \
  --location=us-east1 \
  --default-storage-class=STANDARD \
  --uniform-bucket-level-access \
  --public-access-prevention
gcloud storage buckets update gs://theorymancer-terraform-state --versioning
```

The shared state bucket uses distinct `theorymancer/bootstrap`,
`theorymancer/shared`, `theorymancer/development`, and
`theorymancer/production` prefixes.

## Bootstrap

Apply `bootstrap` locally as a project administrator. It enables Artifact
Registry, Cloud Run, Firestore, Identity Toolkit, Firebase, Cloud KMS, Secret
Manager, API Keys, Secure Token, IAM, and supporting APIs. It also creates the Artifact Registry
repository, Terraform deployment service account, and GitHub Workload Identity
Federation trust.

```bash
gcloud auth application-default login
terraform -chdir=infrastructure/bootstrap init \
  -backend-config="bucket=YOUR_TF_STATE_BUCKET" \
  -backend-config=backend.hcl
terraform -chdir=infrastructure/bootstrap apply
```

Reapply bootstrap before using the corrected auth infrastructure so the new
APIs and deployment roles exist. The checked-in `terraform.tfvars` is loaded
automatically. Initialization is only required for a new checkout or after a
backend/provider change.

Bootstrap is the only root that may modify project IAM. It creates the runtime
service accounts and grants their project-level permissions. GitHub Actions is
explicitly prohibited from `roles/resourcemanager.projectIamAdmin` or any other
role that can alter project IAM policy. Workflow-applied roots may manage IAM
only on individual resources they deploy, such as Cloud Run services or buckets.

### Migrating Existing Runtime Service Accounts

The bootstrap ownership change preserves existing accounts without deleting
them. Before applying this revision, import the previously environment-owned
web and Guild Wars 2 service accounts into bootstrap state. Substitute a
different project ID only if needed.

```bash
terraform -chdir=infrastructure/bootstrap import \
  'google_service_account.runtime["development-web"]' \
  projects/theorymancer/serviceAccounts/tm-development-runtime@theorymancer.iam.gserviceaccount.com
terraform -chdir=infrastructure/bootstrap import \
  'google_service_account.runtime["development-guild_wars_2_api"]' \
  projects/theorymancer/serviceAccounts/tm-development-gw2-api@theorymancer.iam.gserviceaccount.com
terraform -chdir=infrastructure/bootstrap import \
  'google_service_account.runtime["production-web"]' \
  projects/theorymancer/serviceAccounts/tm-production-runtime@theorymancer.iam.gserviceaccount.com
terraform -chdir=infrastructure/bootstrap import \
  'google_service_account.runtime["production-guild_wars_2_api"]' \
  projects/theorymancer/serviceAccounts/tm-production-gw2-api@theorymancer.iam.gserviceaccount.com
terraform -chdir=infrastructure/bootstrap apply
```

The next environment applies use `removed` blocks with `destroy = false` to
relinquish the old state entries. They do not delete the service accounts.

## Shared Resources

Apply `shared` once before either environment. This root is separate because
Identity Platform configuration is project-global and must not be duplicated in
development and production state. It creates both tenants, both Firebase web
apps, and a distinct RS256 KMS signing key/version for each environment.

Every `Deploy` workflow first plans this root. If that plan has changes, the
`Apply shared infrastructure` job requires production approval and applies the
saved plan before the requested environment deployment begins. A no-op shared
plan proceeds directly to the environment deployment. The standalone `Shared
infrastructure` workflow is manual-only for operator recovery and uses the
same plan-before-approval flow.

```bash
terraform -chdir=infrastructure/shared init \
  -backend-config="bucket=YOUR_TF_STATE_BUCKET" \
  -backend-config=backend.hcl
terraform -chdir=infrastructure/shared apply \
  -var="project_id=YOUR_GCP_PROJECT" \
  -var="region=us-east1" \
  -var="development_web_origin=https://YOUR_DEVELOPMENT_WEB_RUN_APP_HOST"
```

If Firebase or Identity Platform was enabled previously, import the existing
project resources into this root instead of attempting to recreate them. Google
sign-in is not configured by Terraform because the Identity Platform provider
resource requires an OAuth client secret, which would be recorded in state.
Configure Google as an identity provider separately for each tenant through a
secret-bearing administrative process. Do not pass its client secret to
Terraform.

## Environment Setup

Configure `development` and `production` GitHub Environments with these
variables:

- `GCP_PROJECT_ID`: shared GCP project ID.
- `GCP_REGION`: Artifact Registry, Cloud Run, and KMS region.
- `GCP_WORKLOAD_IDENTITY_PROVIDER`: bootstrap output.
- `GCP_SERVICE_ACCOUNT`: bootstrap Terraform service-account email.
- `TF_STATE_BUCKET`: manually created state bucket.
- `WEB_ORIGIN`: exact allowed browser origin. Use `https://theorymancer.com` in
  production and the deployed web `run.app` origin in development.
- `IP_HASH_SECRET_VERSION`: pinned Secret Manager version, initially `1`.
- `DEVELOPMENT_WEB_ORIGIN`: repository variable containing the development
  `run.app` origin. The pre-approval shared plan must receive the same value as
  the protected shared apply, so do not define environment-specific overrides.

Protect the production Environment with required reviewers.

The bootstrap root currently creates one project-wide Terraform identity for
all environments. Restricting its OIDC trust to this repository's `main` branch
prevents branch and pull-request impersonation, but it does not make the GCP IAM
boundary independent of GitHub's production Environment gate. Before enabling
customer production, split shared, development, production, and asset-sync
deployments into least-privilege service accounts with environment-specific
Workload Identity bindings.

Production browser authentication also requires the `theorymancer.com` DNS and
edge routes described in `docs/architecture.md`; the generated production
`run.app` web URL is not an interim authenticated production origin.

The deployment workflow automatically creates each environment's IP-hash Secret
Manager container through Terraform, then creates its first enabled version when
none exists. The generated value is streamed directly to Secret Manager and
never enters Terraform variables, state, outputs, GitHub variables, or workflow
logs.

`IP_HASH_SECRET_VERSION` pins the version mounted by Cloud Run. To rotate it,
set the environment variable to the next numeric version; the next deployment
creates that version with a new random value before applying the runtime
configuration. A missing skipped version or a disabled configured version fails
the deployment rather than selecting a different value.

After the first operator signs in to the website, bootstrap that account as the
platform administrator with an operator credential. Replace the environment
database and Identity Platform UID as appropriate. Subsequent game grants are
managed from the central website; this one-time write is not performed by
Terraform.

```bash
curl --request PATCH \
  --header "Authorization: Bearer $(gcloud auth print-access-token)" \
  --header "Content-Type: application/json" \
  --data '{"fields":{"platformRole":{"stringValue":"admin"}}}' \
  "https://firestore.googleapis.com/v1/projects/YOUR_GCP_PROJECT/databases/theorymancer-development/documents/accounts/YOUR_IDENTITY_PLATFORM_UID?updateMask.fieldPaths=platformRole"
```

```bash
terraform -chdir=infrastructure/environments/development plan \
  -var="project_id=YOUR_GCP_PROJECT" \
  -var="terraform_state_bucket=YOUR_TF_STATE_BUCKET" \
  -var="web_origin=YOUR_WEB_ORIGIN" \
  -var="web_image=REGION-docker.pkg.dev/PROJECT/theorymancer/web:TAG" \
  -var="api_image=REGION-docker.pkg.dev/PROJECT/theorymancer/api:TAG" \
  -var="guild_wars_2_api_image=REGION-docker.pkg.dev/PROJECT/theorymancer/guild-wars-2-api:TAG"
```

## Runtime Boundaries

The central API alone has a database-conditioned Firestore user grant, Firebase
Authentication read access, KMS signer/public-key viewer, and IP hash secret
accessor permissions. Guild Wars 2 retains read-only game-assets access and can
invoke the central API with its service identity for internal failure reports.
It has no signing or Firestore permission. The central API has no Guild Wars 2
invoker grant.

All three Cloud Run services use public ingress and `allUsers` invoker grants.
Users authorize to the APIs with application tokens, not Cloud Run IAM. The
internal failure endpoint must validate the configured reporter service account
at application level even though the central service is network-public.

Firestore uses separate named development and production databases. Production
has deletion protection. Named databases do not receive Firestore free quota,
so both databases are billed for their usage. Browser direct database access is
prohibited; each database has a deny-all Firebase client ruleset and only server
IAM is granted.

## Web Configuration And Routing

Web API and Firebase settings are Cloud Run runtime environment variables. The
workflow does not bake a `VITE_API_URL` build argument into the image. The web
container must consume runtime configuration rather than assume Vite build-time
substitution.

Development uses separate generated `run.app` URLs. Production will eventually
route central auth/account paths and `/v1/games/guild-wars-2` directly to their
respective services at the edge. No load balancer is provisioned now, and the
central API is not an application proxy for Guild Wars 2.

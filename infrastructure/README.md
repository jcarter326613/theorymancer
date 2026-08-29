# Infrastructure

Terraform uses one GCP project initially to minimize fixed cost. State and
resources remain isolated by ownership:

- `bootstrap` enables APIs and creates deployment prerequisites.
- `shared` owns project-global Identity Platform/Firebase configuration and the
  separate development and production KMS signing keys.
- `environments/development` deploys the shared integration environment.
- `environments/production` deploys the production environment.
- `modules` contains reusable environment resources.

The environment roots provide public Cloud Run services, separate web, central
API, and Guild Wars 2 runtime identities, named Firestore databases, Secret
Manager containers, Cloud Storage buckets, and narrow runtime IAM grants.

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
terraform -chdir=infrastructure/bootstrap init \
  -backend-config="bucket=YOUR_TF_STATE_BUCKET" \
  -backend-config=backend.hcl
terraform -chdir=infrastructure/bootstrap apply -var-file=bootstrap.tfvars
```

Reapply bootstrap before using the corrected auth infrastructure so the new
APIs and deployment roles exist. The deployment identity has administrative
roles for only the resource families Terraform manages; runtime identities are
granted separately and narrowly in environment state.

## Shared Resources

Apply `shared` once before either environment. This root is separate because
Identity Platform configuration is project-global and must not be duplicated in
development and production state. It creates both tenants, both Firebase web
apps, and a distinct RS256 KMS signing key/version for each environment.
Environment deployments do not apply this state. Use the manually dispatched
`Shared infrastructure` workflow so development and production deployments
cannot overwrite or concurrently mutate project-global identity settings.

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
- `DEVELOPMENT_WEB_ORIGIN`: repository or production Environment variable used
  only by the shared-infrastructure workflow to retain the development
  `run.app` domain in Identity Platform.

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

Each environment creates only an empty Secret Manager container. Before the
first full deployment, target that container, then add version `1` out of band.
Supply the normal required Terraform variables to the targeted apply; placeholder
Cloud Run image strings are sufficient because no service is targeted.

```bash
terraform -chdir=infrastructure/environments/development init \
  -backend-config="bucket=YOUR_TF_STATE_BUCKET" \
  -backend-config=backend.hcl
terraform -chdir=infrastructure/environments/development apply \
  -target=google_secret_manager_secret.ip_hash \
  -var="project_id=YOUR_GCP_PROJECT" \
  -var="terraform_state_bucket=YOUR_TF_STATE_BUCKET" \
  -var="web_origin=YOUR_WEB_ORIGIN" \
  -var="web_image=unused" -var="api_image=unused" \
  -var="guild_wars_2_api_image=unused"
openssl rand -base64 32 | gcloud secrets versions add \
  theorymancer-development-ip-hash --project=YOUR_GCP_PROJECT --data-file=-
```

Repeat with `production` and `theorymancer-production-ip-hash`. Secret payloads
must never be placed in `.tfvars`, Terraform variables, outputs, or state. After
the version exists, run a normal apply without `-target`; the deployment
workflow does this automatically.

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
has deletion protection. Firestore's free quota applies to only one database in
the project, so the second database is billable. Browser direct database access
is prohibited; each database has a deny-all Firebase client ruleset and only
server IAM is granted.

## Web Configuration And Routing

Web API and Firebase settings are Cloud Run runtime environment variables. The
workflow does not bake a `VITE_API_URL` build argument into the image. The web
container must consume runtime configuration rather than assume Vite build-time
substitution.

Development uses separate generated `run.app` URLs. Production will eventually
route central auth/account paths and `/v1/games/guild-wars-2` directly to their
respective services at the edge. No load balancer is provisioned now, and the
central API is not an application proxy for Guild Wars 2.

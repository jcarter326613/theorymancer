# Infrastructure

Terraform uses one GCP project initially to minimize fixed cost. State and
resources remain isolated by environment:

- `bootstrap` creates the shared deployment prerequisites.
- `environments/development` deploys the shared integration environment.
- `environments/production` deploys the production environment.
- `modules` contains reusable environment resources.

The initial infrastructure provides Cloud Run services, Cloud Storage upload
buckets, and Artifact Registry. It deliberately does not provision a database.

## Prerequisites

Install Terraform 1.8 or newer and authenticate locally with a Google Cloud
identity that can administer the selected project. Enable billing for that
project. Create one GCS bucket before initializing Terraform; Terraform cannot
store state in a bucket that it has not yet created.

```bash
gcloud storage buckets create gs://theorymancer-terraform-state \
  --project=theorymancer \
  --location=us-east1 \
  --default-storage-class=STANDARD \
  --uniform-bucket-level-access \
  --public-access-prevention
gcloud storage buckets update gs://theorymancer-terraform-state --versioning
```

The shared state bucket is `theorymancer-terraform-state`. It has uniform
bucket-level access, public-access prevention, and object versioning enabled.
Use it for all roots with the distinct prefixes committed in their
`backend.hcl` files.

## Bootstrap

Copy `bootstrap/terraform.tfvars.example` to an ignored `.tfvars` file and set
the project, state bucket, GitHub repository, and region. Apply this root
locally once. It creates the Artifact Registry repository, Terraform deployment
service account, and GitHub Workload Identity Federation trust.

```bash
terraform -chdir=infrastructure/bootstrap init \
  -backend-config="bucket=YOUR_TF_STATE_BUCKET" \
  -backend-config=backend.hcl
terraform -chdir=infrastructure/bootstrap apply -var-file=bootstrap.tfvars
```

After bootstrap, configure `development` and `production` GitHub Environments.
Set these Environment variables in each:

- `GCP_PROJECT_ID`: the shared GCP project ID.
- `GCP_REGION`: the Artifact Registry and Cloud Run region, such as `us-east1`.
- `GCP_WORKLOAD_IDENTITY_PROVIDER`: the bootstrap output value.
- `GCP_SERVICE_ACCOUNT`: the bootstrap Terraform service-account email.
- `TF_STATE_BUCKET`: the manually-created GCS state bucket.

Protect the production Environment with required reviewers. The bootstrap
workflow is intentionally not automated because it establishes the trust used
by subsequent GitHub Actions deployments.

## Local environment commands

Provide container image references when planning or applying an environment.
The deployment workflow normally supplies immutable image references.

```bash
terraform -chdir=infrastructure/environments/development init \
  -backend-config="bucket=YOUR_TF_STATE_BUCKET" \
  -backend-config=backend.hcl
terraform -chdir=infrastructure/environments/development plan \
  -var="project_id=YOUR_GCP_PROJECT" \
  -var="web_image=REGION-docker.pkg.dev/PROJECT/theorymancer/web:TAG" \
  -var="api_image=REGION-docker.pkg.dev/PROJECT/theorymancer/api:TAG"
```

## Domain setup

Development uses Cloud Run's generated `run.app` URLs. Production is also
initially reachable using its generated URL. Configure `theorymancer.com` only
after the domain has been registered and verified. A custom-domain or load
balancer decision should be recorded here before infrastructure is added,
because the lowest-cost option depends on the Cloud Run domain-mapping support
available for the chosen region and domain at that time.

locals {
  required_services = toset([
    "artifactregistry.googleapis.com",
    "cloudkms.googleapis.com",
    "cloudresourcemanager.googleapis.com",
    "firebaserules.googleapis.com",
    "firestore.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "run.googleapis.com",
    "secretmanager.googleapis.com",
    "serviceusage.googleapis.com",
    "storage.googleapis.com",
    "sts.googleapis.com",
  ])

  default_terraform_roles = toset([
    "roles/artifactregistry.admin",
    "roles/cloudkms.admin",
    "roles/datastore.owner",
    "roles/firebaserules.admin",
    "roles/run.admin",
    "roles/secretmanager.admin",
    "roles/serviceusage.serviceUsageAdmin",
    "roles/storage.admin",
  ])

  environments = {
    development = {
      firestore_database = "theorymancer-development"
    }
    production = {
      firestore_database = "theorymancer-production"
    }
  }

  runtime_service_accounts = merge([
    for environment, configuration in local.environments : {
      for service, suffix in {
        web              = "runtime"
        api              = "api"
        guild_wars_2_api = "gw2-api"
        } : "${environment}-${service}" => {
        environment  = environment
        service      = service
        account_id   = "tm-${environment}-${suffix}"
        display_name = "Theorymancer ${environment} ${replace(service, "_", " ")}"
      }
    }
  ]...)
}

resource "google_project_service" "required" {
  for_each = local.required_services

  project            = var.project_id
  service            = each.value
  disable_on_destroy = false
}

resource "google_artifact_registry_repository" "containers" {
  location      = var.region
  repository_id = "theorymancer"
  description   = "Theorymancer deployable container images"
  format        = "DOCKER"

  depends_on = [google_project_service.required["artifactregistry.googleapis.com"]]
}

resource "google_service_account" "terraform" {
  account_id   = "theorymancer-terraform"
  display_name = "Theorymancer Terraform deployment"
  project      = var.project_id

  depends_on = [google_project_service.required["iam.googleapis.com"]]
}

resource "google_service_account" "runtime" {
  for_each = local.runtime_service_accounts

  account_id   = each.value.account_id
  display_name = each.value.display_name
  project      = var.project_id

  depends_on = [google_project_service.required["iam.googleapis.com"]]
}

resource "google_service_account_iam_member" "terraform_uses_runtime" {
  for_each = google_service_account.runtime

  service_account_id = each.value.name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${google_service_account.terraform.email}"
}

resource "google_project_iam_member" "api_firestore_user" {
  for_each = local.environments

  project = var.project_id
  role    = "roles/datastore.user"
  member  = "serviceAccount:${google_service_account.runtime["${each.key}-api"].email}"

  condition {
    title       = "${each.key}-firestore-only"
    description = "Restrict the central API to its named database."
    expression  = "resource.name == \"projects/${var.project_id}/databases/${each.value.firestore_database}\""
  }
}

resource "google_storage_bucket_iam_member" "terraform_state" {
  bucket = var.state_bucket_name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.terraform.email}"
}

resource "google_iam_workload_identity_pool" "github" {
  workload_identity_pool_id = "theorymancer-github"
  display_name              = "Theorymancer GitHub Actions"
  description               = "GitHub Actions identity pool for Terraform deployments."

  depends_on = [google_project_service.required["iam.googleapis.com"]]
}

resource "google_iam_workload_identity_pool_provider" "github" {
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "github"
  display_name                       = "Theorymancer GitHub Actions"
  description                        = "Restricts Terraform service-account impersonation to this repository."

  attribute_mapping = {
    "google.subject"       = "assertion.sub"
    "attribute.actor"      = "assertion.actor"
    "attribute.repository" = "assertion.repository"
  }
  attribute_condition = "assertion.repository == \"${var.github_repository}\" && assertion.ref == \"refs/heads/main\""

  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }

  depends_on = [google_project_service.required["sts.googleapis.com"]]
}

resource "google_service_account_iam_member" "github_workload_identity_user" {
  service_account_id = google_service_account.terraform.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.github.name}/attribute.repository/${var.github_repository}"

  depends_on = [google_project_service.required["iamcredentials.googleapis.com"]]
}

resource "google_project_iam_member" "terraform" {
  for_each = local.default_terraform_roles

  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.terraform.email}"
}

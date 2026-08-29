locals {
  environment             = "development"
  auth_issuer             = "https://auth.development.theorymancer.com"
  gw2_auth_audience       = "theorymancer:games:guild-wars-2:development"
  uploads_bucket_name     = "${var.project_id}-theorymancer-development-uploads"
  game_assets_bucket_name = "${var.project_id}-theorymancer-development-game-assets"
  firebase_web_app        = data.terraform_remote_state.shared.outputs.firebase_web_app_configs[local.environment]
  firebase_tenant_id      = data.terraform_remote_state.shared.outputs.firebase_tenant_ids[local.environment]
  signing_key_id          = data.terraform_remote_state.shared.outputs.auth_signing_key_ids[local.environment]
  signing_key_version     = data.terraform_remote_state.shared.outputs.auth_signing_key_versions[local.environment]
}

data "terraform_remote_state" "shared" {
  backend = "gcs"

  config = {
    bucket = var.terraform_state_bucket
    prefix = "theorymancer/shared"
  }
}

moved {
  from = google_service_account.runtime
  to   = google_service_account.web
}

resource "google_service_account" "web" {
  account_id   = "tm-development-runtime"
  display_name = "Theorymancer development web"
}

resource "google_service_account" "api" {
  account_id   = "tm-development-api"
  display_name = "Theorymancer development central API"
}

resource "google_service_account" "guild_wars_2_api" {
  account_id   = "tm-development-gw2-api"
  display_name = "Theorymancer development Guild Wars 2 API"
}

resource "google_firestore_database" "this" {
  project                           = var.project_id
  name                              = "theorymancer-development"
  location_id                       = var.firestore_location
  type                              = "FIRESTORE_NATIVE"
  database_edition                  = "STANDARD"
  app_engine_integration_mode       = "DISABLED"
  point_in_time_recovery_enablement = "POINT_IN_TIME_RECOVERY_DISABLED"
  delete_protection_state           = "DELETE_PROTECTION_DISABLED"
  deletion_policy                   = "DELETE"
}

resource "google_firestore_field" "dpop_proof_expiry" {
  project    = var.project_id
  database   = google_firestore_database.this.name
  collection = "dpopProofs"
  field      = "expiresAt"

  ttl_config {}
}

resource "google_firebaserules_ruleset" "firestore_deny_all" {
  project = var.project_id

  source {
    files {
      name    = "firestore.rules"
      content = "service cloud.firestore { match /databases/{database}/documents { match /{document=**} { allow read, write: if false; } } }"
    }
  }
}

resource "google_firebaserules_release" "firestore" {
  project      = var.project_id
  name         = "cloud.firestore/${google_firestore_database.this.name}"
  ruleset_name = "projects/${var.project_id}/rulesets/${google_firebaserules_ruleset.firestore_deny_all.name}"
}

resource "google_project_iam_member" "api_firestore_user" {
  project = var.project_id
  role    = "roles/datastore.user"
  member  = "serviceAccount:${google_service_account.api.email}"

  condition {
    title       = "development-firestore-only"
    description = "Restrict the development API to its named database."
    expression  = "resource.name == \"projects/${var.project_id}/databases/${google_firestore_database.this.name}\""
  }
}

resource "google_project_iam_member" "api_firebase_auth_viewer" {
  project = var.project_id
  role    = "roles/firebaseauth.viewer"
  member  = "serviceAccount:${google_service_account.api.email}"
}

resource "google_kms_crypto_key_iam_member" "api_signer" {
  crypto_key_id = local.signing_key_id
  role          = "roles/cloudkms.signerVerifier"
  member        = "serviceAccount:${google_service_account.api.email}"
}

resource "google_kms_crypto_key_iam_member" "api_public_key_viewer" {
  crypto_key_id = local.signing_key_id
  role          = "roles/cloudkms.publicKeyViewer"
  member        = "serviceAccount:${google_service_account.api.email}"
}

resource "google_secret_manager_secret" "ip_hash" {
  project   = var.project_id
  secret_id = "theorymancer-development-ip-hash"

  replication {
    auto {}
  }
}

resource "google_secret_manager_secret_iam_member" "api_ip_hash_accessor" {
  project   = var.project_id
  secret_id = google_secret_manager_secret.ip_hash.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api.email}"
}

resource "google_storage_bucket" "uploads" {
  name                        = local.uploads_bucket_name
  location                    = var.region
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = true

  labels = {
    environment = local.environment
    application = "theorymancer"
  }
}

resource "google_storage_bucket" "game_assets" {
  name                        = local.game_assets_bucket_name
  location                    = var.region
  storage_class               = "STANDARD"
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = true

  labels = {
    environment = local.environment
    application = "theorymancer"
    component   = "game-assets"
  }
}

resource "google_storage_bucket_iam_member" "guild_wars_2_api_game_assets" {
  bucket = google_storage_bucket.game_assets.name
  role   = "roles/storage.objectViewer"
  member = "serviceAccount:${google_service_account.guild_wars_2_api.email}"
}

module "api" {
  source = "../../modules/cloud-run-service"

  project_id            = var.project_id
  region                = var.region
  service_name          = "theorymancer-development-api"
  image                 = var.api_image
  service_account_email = google_service_account.api.email
  allow_unauthenticated = true
  ingress               = "INGRESS_TRAFFIC_ALL"
  max_instances         = 3
  environment_variables = {
    GCP_PROJECT_ID                             = var.project_id
    FIRESTORE_DATABASE_ID                      = google_firestore_database.this.name
    FIREBASE_TENANT_ID                         = local.firebase_tenant_id
    AUTH_ISSUER                                = local.auth_issuer
    GW2_AUTH_AUDIENCE                          = local.gw2_auth_audience
    AUTH_SIGNING_KEY_VERSION                   = local.signing_key_version
    AUTH_SIGNING_KEY_ID                        = local.signing_key_version
    WEB_ORIGIN                                 = var.web_origin
    INTERNAL_FAILURE_REPORTER_SERVICE_ACCOUNTS = google_service_account.guild_wars_2_api.email
    UPLOADS_BUCKET                             = google_storage_bucket.uploads.name
  }
  secret_environment_variables = {
    IP_HASH_SECRET = {
      secret  = google_secret_manager_secret.ip_hash.secret_id
      version = var.ip_hash_secret_version
    }
  }

  depends_on = [
    google_project_iam_member.api_firestore_user,
    google_project_iam_member.api_firebase_auth_viewer,
    google_kms_crypto_key_iam_member.api_public_key_viewer,
    google_kms_crypto_key_iam_member.api_signer,
    google_secret_manager_secret_iam_member.api_ip_hash_accessor,
  ]
}

module "guild_wars_2_api" {
  source = "../../modules/cloud-run-service"

  project_id            = var.project_id
  region                = var.region
  service_name          = "theorymancer-development-guild-wars-2-api"
  image                 = var.guild_wars_2_api_image
  service_account_email = google_service_account.guild_wars_2_api.email
  allow_unauthenticated = true
  ingress               = "INGRESS_TRAFFIC_ALL"
  max_instances         = 3
  environment_variables = {
    AUTH_ISSUER             = local.auth_issuer
    AUTH_AUDIENCE           = local.gw2_auth_audience
    AUTH_JWKS_URL           = "${module.api.uri}/.well-known/jwks.json"
    PARENT_AUTH_FAILURE_URL = "${module.api.uri}/v1/internal/auth-failures"
    PARENT_API_AUDIENCE     = local.auth_issuer
    GAME_ASSETS_BUCKET      = google_storage_bucket.game_assets.name
  }
}

resource "google_cloud_run_v2_service_iam_member" "guild_wars_2_invokes_api" {
  project  = var.project_id
  location = var.region
  name     = module.api.name
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.guild_wars_2_api.email}"
}

module "web" {
  source = "../../modules/cloud-run-service"

  project_id            = var.project_id
  region                = var.region
  service_name          = "theorymancer-development-web"
  image                 = var.web_image
  service_account_email = google_service_account.web.email
  allow_unauthenticated = true
  ingress               = "INGRESS_TRAFFIC_ALL"
  max_instances         = 3
  environment_variables = {
    API_URL                      = module.api.uri
    GUILD_WARS_2_API_URL         = module.guild_wars_2_api.uri
    FIREBASE_API_KEY             = local.firebase_web_app.api_key
    FIREBASE_APP_ID              = local.firebase_web_app.app_id
    FIREBASE_AUTH_DOMAIN         = local.firebase_web_app.auth_domain
    FIREBASE_MESSAGING_SENDER_ID = local.firebase_web_app.messaging_sender_id
    FIREBASE_PROJECT_ID          = local.firebase_web_app.project_id
    FIREBASE_TENANT_ID           = local.firebase_tenant_id
  }
}

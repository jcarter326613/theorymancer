locals {
  environment             = "production"
  uploads_bucket_name     = "${var.project_id}-theorymancer-production-uploads"
  game_assets_bucket_name = "${var.project_id}-theorymancer-production-game-assets"
}

resource "google_service_account" "runtime" {
  account_id   = "tm-production-runtime"
  display_name = "Theorymancer production runtime"
}

resource "google_service_account" "guild_wars_2_api" {
  account_id   = "tm-production-gw2-api"
  display_name = "Theorymancer production Guild Wars 2 API"
}

resource "google_storage_bucket" "uploads" {
  name                        = local.uploads_bucket_name
  location                    = var.region
  uniform_bucket_level_access = true
  force_destroy               = false

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
  force_destroy               = false

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

module "web" {
  source = "../../modules/cloud-run-service"

  project_id            = var.project_id
  region                = var.region
  service_name          = "theorymancer-production-web"
  image                 = var.web_image
  service_account_email = google_service_account.runtime.email
}

module "api" {
  source = "../../modules/cloud-run-service"

  project_id            = var.project_id
  region                = var.region
  service_name          = "theorymancer-production-api"
  image                 = var.api_image
  service_account_email = google_service_account.runtime.email
  environment_variables = {
    UPLOADS_BUCKET = google_storage_bucket.uploads.name
  }
}

module "guild_wars_2_api" {
  source = "../../modules/cloud-run-service"

  project_id            = var.project_id
  region                = var.region
  service_name          = "theorymancer-production-guild-wars-2-api"
  image                 = var.guild_wars_2_api_image
  service_account_email = google_service_account.guild_wars_2_api.email
  environment_variables = {
    GAME_ASSETS_BUCKET = google_storage_bucket.game_assets.name
  }
}

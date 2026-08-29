locals {
  environments = toset(["development", "production"])
  authorized_domains = distinct(concat(var.authorized_domains, [
    "${var.project_id}.firebaseapp.com",
    "${var.project_id}.web.app",
  ], var.development_web_origin == "" ? [] : [trimprefix(var.development_web_origin, "https://")]))
}

resource "google_firebase_project" "this" {
  provider = google-beta
  project  = var.project_id
}

resource "google_identity_platform_config" "this" {
  project                    = var.project_id
  autodelete_anonymous_users = true
  authorized_domains         = local.authorized_domains

  multi_tenant {
    allow_tenants = true
  }

  sign_in {
    allow_duplicate_emails = false

    anonymous {
      enabled = false
    }

    email {
      enabled           = false
      password_required = true
    }
  }

  depends_on = [google_firebase_project.this]
}

resource "google_identity_platform_tenant" "this" {
  for_each = local.environments

  project               = var.project_id
  display_name          = "Theorymancer ${each.key}"
  allow_password_signup = false
  disable_auth          = false

  client {
    permissions {
      disabled_user_deletion = true
      disabled_user_signup   = false
    }
  }

  lifecycle {
    prevent_destroy = true
  }

  depends_on = [google_identity_platform_config.this]
}

resource "google_firebase_web_app" "this" {
  provider = google-beta
  for_each = local.environments

  project         = var.project_id
  display_name    = "Theorymancer ${each.key} web"
  deletion_policy = "ABANDON"

  depends_on = [google_firebase_project.this]
}

data "google_firebase_web_app_config" "this" {
  provider = google-beta
  for_each = local.environments

  project    = var.project_id
  web_app_id = google_firebase_web_app.this[each.key].app_id
}

resource "google_kms_key_ring" "auth" {
  project  = var.project_id
  name     = "theorymancer-auth"
  location = var.region
}

resource "google_kms_crypto_key" "auth_signing" {
  for_each = local.environments

  name                          = "${each.key}-access-token-signing"
  key_ring                      = google_kms_key_ring.auth.id
  purpose                       = "ASYMMETRIC_SIGN"
  skip_initial_version_creation = true

  version_template {
    algorithm        = "RSA_SIGN_PKCS1_2048_SHA256"
    protection_level = "SOFTWARE"
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "google_kms_crypto_key_version" "auth_signing" {
  for_each = local.environments

  crypto_key = google_kms_crypto_key.auth_signing[each.key].id

  lifecycle {
    prevent_destroy = true
  }
}

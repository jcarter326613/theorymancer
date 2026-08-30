locals {
  environments = toset(["development", "production"])
  api_service_accounts = {
    for environment in local.environments : environment => "tm-${environment}-api@${var.project_id}.iam.gserviceaccount.com"
  }
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

resource "google_kms_crypto_key_iam_member" "api_signer" {
  for_each = local.environments

  crypto_key_id = google_kms_crypto_key.auth_signing[each.key].id
  role          = "roles/cloudkms.signerVerifier"
  member        = "serviceAccount:${local.api_service_accounts[each.key]}"
}

resource "google_kms_crypto_key_iam_member" "api_public_key_viewer" {
  for_each = local.environments

  crypto_key_id = google_kms_crypto_key.auth_signing[each.key].id
  role          = "roles/cloudkms.publicKeyViewer"
  member        = "serviceAccount:${local.api_service_accounts[each.key]}"
}

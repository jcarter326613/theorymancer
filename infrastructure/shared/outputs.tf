output "firebase_tenant_ids" {
  description = "Identity Platform tenant IDs keyed by environment."
  value       = { for environment, tenant in google_identity_platform_tenant.this : environment => tenant.name }
}

output "firebase_web_app_configs" {
  description = "Public Firebase web application configuration keyed by environment."
  value = {
    for environment, app in google_firebase_web_app.this : environment => {
      api_key             = data.google_firebase_web_app_config.this[environment].api_key
      app_id              = app.app_id
      auth_domain         = data.google_firebase_web_app_config.this[environment].auth_domain
      messaging_sender_id = data.google_firebase_web_app_config.this[environment].messaging_sender_id
      project_id          = var.project_id
    }
  }
}

output "auth_signing_key_ids" {
  description = "KMS asymmetric signing key resource IDs keyed by environment."
  value       = { for environment, key in google_kms_crypto_key.auth_signing : environment => key.id }
}

output "auth_signing_key_versions" {
  description = "Pinned KMS signing key-version resource names keyed by environment."
  value       = { for environment, version in google_kms_crypto_key_version.auth_signing : environment => version.name }
}

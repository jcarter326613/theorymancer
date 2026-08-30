output "auth_signing_key_ids" {
  description = "KMS asymmetric signing key resource IDs keyed by environment."
  value       = { for environment, key in google_kms_crypto_key.auth_signing : environment => key.id }
}

output "auth_signing_key_versions" {
  description = "Pinned KMS signing key-version resource names keyed by environment."
  value       = { for environment, version in google_kms_crypto_key_version.auth_signing : environment => version.name }
}

output "api_url" {
  description = "Development API URL."
  value       = module.api.uri
}

output "uploads_bucket" {
  description = "Development uploads bucket."
  value       = google_storage_bucket.uploads.name
}

output "game_assets_bucket" {
  description = "Development immutable game-assets bucket."
  value       = google_storage_bucket.game_assets.name
}

output "guild_wars_2_api_url" {
  description = "Development Guild Wars 2 API URL."
  value       = module.guild_wars_2_api.uri
}

output "web_url" {
  description = "Development website URL."
  value       = module.web.uri
}

output "firestore_database_id" {
  description = "Development named Firestore database ID."
  value       = google_firestore_database.this.name
}

output "ip_hash_secret_id" {
  description = "Secret Manager container with a workflow-managed payload version."
  value       = google_secret_manager_secret.ip_hash.secret_id
}

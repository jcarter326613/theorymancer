output "api_url" {
  description = "Production API URL."
  value       = module.api.uri
}

output "uploads_bucket" {
  description = "Production uploads bucket."
  value       = google_storage_bucket.uploads.name
}

output "game_assets_bucket" {
  description = "Production immutable game-assets bucket."
  value       = google_storage_bucket.game_assets.name
}

output "web_url" {
  description = "Production website URL before custom-domain configuration."
  value       = module.web.uri
}

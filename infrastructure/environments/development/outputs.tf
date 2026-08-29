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

output "web_url" {
  description = "Development website URL."
  value       = module.web.uri
}

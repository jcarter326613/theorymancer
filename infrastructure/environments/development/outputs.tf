output "api_url" {
  description = "Development API URL."
  value       = module.api.uri
}

output "uploads_bucket" {
  description = "Development uploads bucket."
  value       = google_storage_bucket.uploads.name
}

output "web_url" {
  description = "Development website URL."
  value       = module.web.uri
}

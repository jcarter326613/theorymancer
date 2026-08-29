output "uri" {
  description = "Generated public service URL."
  value       = google_cloud_run_v2_service.this.uri
}

output "name" {
  description = "Cloud Run service name."
  value       = google_cloud_run_v2_service.this.name
}

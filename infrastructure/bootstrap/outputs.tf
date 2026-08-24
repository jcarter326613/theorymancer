output "artifact_registry_repository" {
  description = "Artifact Registry repository for deployable container images."
  value       = google_artifact_registry_repository.containers.name
}

output "terraform_service_account" {
  description = "Service account impersonated by GitHub Actions."
  value       = google_service_account.terraform.email
}

output "workload_identity_provider" {
  description = "Full Workload Identity Provider resource name for GitHub Actions."
  value       = google_iam_workload_identity_pool_provider.github.name
}

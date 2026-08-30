variable "project_id" {
  description = "GCP project that contains Theorymancer infrastructure."
  type        = string
}

variable "region" {
  description = "Region for Artifact Registry and default Google Cloud resources."
  type        = string
  default     = "us-east1"
}

variable "state_bucket_name" {
  description = "Existing GCS bucket used for Terraform state."
  type        = string
}

variable "github_repository" {
  description = "GitHub repository allowed to deploy, in OWNER/REPOSITORY form."
  type        = string
}

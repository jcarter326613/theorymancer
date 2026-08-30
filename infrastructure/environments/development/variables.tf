variable "project_id" {
  description = "Shared GCP project ID."
  type        = string
}

variable "region" {
  description = "Google Cloud region."
  type        = string
  default     = "us-east1"
}

variable "web_image" {
  description = "Immutable container image for the website."
  type        = string
}

variable "api_image" {
  description = "Immutable container image for the API."
  type        = string
}

variable "guild_wars_2_api_image" {
  description = "Immutable container image for the Guild Wars 2 API."
  type        = string
}

variable "terraform_state_bucket" {
  description = "GCS bucket containing the shared Terraform state."
  type        = string
}

variable "firestore_location" {
  description = "Firestore database location."
  type        = string
  default     = "us-east1"
}

variable "web_origin" {
  description = "Browser origin allowed by the central API."
  type        = string

  validation {
    condition     = can(regex("^https://[^/?#]+$", var.web_origin))
    error_message = "web_origin must be an HTTPS origin without a path, query, fragment, or trailing slash."
  }
}

variable "ip_hash_secret_version" {
  description = "Pinned workflow-managed Secret Manager version containing the IP hash secret."
  type        = string
  default     = "1"
}

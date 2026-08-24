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

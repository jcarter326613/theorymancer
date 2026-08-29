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

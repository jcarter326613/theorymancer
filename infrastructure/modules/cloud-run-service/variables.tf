variable "project_id" {
  description = "GCP project containing the service."
  type        = string
}

variable "region" {
  description = "Cloud Run region."
  type        = string
}

variable "service_name" {
  description = "Cloud Run service name."
  type        = string
}

variable "image" {
  description = "Immutable container image reference."
  type        = string
}

variable "service_account_email" {
  description = "Runtime service account email."
  type        = string
}

variable "environment_variables" {
  description = "Non-secret environment variables for the container."
  type        = map(string)
  default     = {}
}

variable "secret_environment_variables" {
  description = "Environment variables backed by pinned Secret Manager versions."
  type = map(object({
    secret  = string
    version = string
  }))
  default = {}
}

variable "allow_unauthenticated" {
  description = "Whether allUsers may invoke the service."
  type        = bool
  default     = false
}

variable "ingress" {
  description = "Cloud Run ingress setting."
  type        = string
  default     = "INGRESS_TRAFFIC_ALL"
}

variable "max_instances" {
  description = "Maximum number of serving instances."
  type        = number
  default     = 3
}

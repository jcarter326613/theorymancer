variable "project_id" {
  description = "Shared GCP project ID."
  type        = string
}

variable "region" {
  description = "Google Cloud region for the signing key ring."
  type        = string
  default     = "us-east1"
}

variable "authorized_domains" {
  description = "Additional domains allowed to complete Identity Platform redirects. Firebase hosting domains are added automatically."
  type        = list(string)
  default = [
    "localhost",
    "theorymancer.com",
  ]
}

variable "development_web_origin" {
  description = "Development Cloud Run web origin authorized for Identity Platform redirects."
  type        = string
  default     = ""

  validation {
    condition     = var.development_web_origin == "" || can(regex("^https://[^/]+$", var.development_web_origin))
    error_message = "development_web_origin must be empty or an HTTPS origin without a path."
  }
}

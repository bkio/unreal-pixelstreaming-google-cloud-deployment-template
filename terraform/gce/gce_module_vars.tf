### Module variables

variable "GOOGLE_CLOUD_PROJECT_ID" {
  type = string
}

variable "GOOGLE_CREDENTIALS" {
  type = string
}

variable "VM_ZONES" {
  type = list(string)
}

variable "VM_NAME_PREFIX" {
  type = string
}

variable "ORCHESTRATOR_VM_INSTALL_SH_FILE_CONTENT" {
  type = string
}

variable "GPU_VM_INSTALL_SH_FILE_CONTENT" {
  type = string
}

variable "ORCHESTRATOR_CONTAINER_NAME" {
  type = string
}

variable "PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME" {
  type = string
}

variable "GPU_INSTANCES_PER_ZONE" {
  type = string
}

variable "MAX_USER_SESSION_PER_INSTANCE" {
  type = string
}

variable "DOMAIN_NAME" {
  type = string
}

variable "ACME_OWNER_EMAIL" {
  type = string
}

variable "FILE_API_BUCKET_NAME" {
  type = string
}

variable "CLOUD_API_SECRET_KEYS_BASE64" {
  type = string
}
### Input variables

variable "GOOGLE_CLOUD_PROJECT_ID" {
  type = string
  sensitive = false
}

variable "GOOGLE_CREDENTIALS" {
  type = string
  sensitive = false
}

variable "VM_ZONES" {
  type = string
  sensitive = false
}

variable "VM_NAME_PREFIX" {
  type = string
  sensitive = false
}

variable "ORCHESTRATOR_VM_INSTALL_SH_FILE_PATH" {
  type = string
  sensitive = false
}

variable "GPU_VM_INSTALL_SH_FILE_PATH" {
  type = string
  sensitive = false
}

variable "ORCHESTRATOR_CONTAINER_NAME" {
  type = string
  sensitive = false
}

variable "PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME" {
  type = string
  sensitive = false
}

variable "GPU_INSTANCES_PER_ZONE" {
  type = string
  sensitive = false
}

variable "MAX_USER_SESSION_PER_INSTANCE" {
  type = string
  sensitive = false
}

variable "DOMAIN_NAME" {
  type = string
  sensitive = false
}
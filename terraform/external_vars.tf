### Input variables

variable "GOOGLE_CLOUD_PROJECT_ID" {
  type = string
}

variable "GOOGLE_CREDENTIALS" {
  type = string
}

variable "VM_ZONES" {
  type = string
}

variable "VM_NAME_PREFIX" {
  type = string
}

variable "ORCHESTRATOR_SERVICE_DIRECTORY_FULL_PATH" {
  type = string
}

variable "ORCHESTRATOR_VM_INSTALL_SH_FILE_PATH" {
  type = string
}

variable "GPU_VM_INSTALL_SH_FILE_PATH" {
  type = string
}

variable "ORCHESTRATOR_CONTAINER_NAME" {
  type = string
}

variable "PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME" {
  type = string
}

variable "MAX_USER_SESSION_PER_INSTANCE" {
  type = string
}

variable "DOMAIN_NAME" {
  type = string
}
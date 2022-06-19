### Input variables

variable "GITHUB_UNIQUE_BUILD_NUMBER" {
  type = string
}

variable "TERRAFORM_STATE_BUCKET" {
  type = string
}

variable "GOOGLE_CLOUD_PROJECT_ID" {
  type = string
}

variable "VM_ZONES" {
  type = string
}

variable "VM_NAME_PREFIX" {
  type = string
}

variable "ORCHESTRATOR_VM_INSTALL_SH_FILE_PATH" {
  type = string
}

variable "GPU_VM_INSTALL_SH_FILE_PATH" {
  type = string
}

variable "DOMAIN_NAME" {
  type = string
}
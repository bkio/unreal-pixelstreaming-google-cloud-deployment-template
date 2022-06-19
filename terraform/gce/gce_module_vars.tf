### Module variables

variable "GITHUB_UNIQUE_BUILD_NUMBER" {
  type = string
}

variable "GOOGLE_CLOUD_PROJECT_ID" {
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

variable "DOMAIN_NAME" {
  type = string
}
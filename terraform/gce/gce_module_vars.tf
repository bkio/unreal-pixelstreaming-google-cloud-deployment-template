### Module variables

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
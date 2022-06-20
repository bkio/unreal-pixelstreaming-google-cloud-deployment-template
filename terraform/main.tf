### Google Cloud related variables
locals {
  DECODED_VM_ZONES = split(",", var.VM_ZONES)
}

### Adding remote state storage
terraform {
  required_version = ">=0.14.3"
  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "4.25.0"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "3.4.0"
    }
  }
  backend "gcs" {}
}

module "gce" {
  source = "./gce"

  GOOGLE_CLOUD_PROJECT_ID = var.GOOGLE_CLOUD_PROJECT_ID

  VM_ZONES       = local.DECODED_VM_ZONES
  VM_NAME_PREFIX = var.VM_NAME_PREFIX

  ORCHESTRATOR_VM_INSTALL_SH_FILE_CONTENT = file("${path.module}${var.ORCHESTRATOR_VM_INSTALL_SH_FILE_PATH}")
  GPU_VM_INSTALL_SH_FILE_CONTENT          = file("${path.module}${var.GPU_VM_INSTALL_SH_FILE_PATH}")

  DOMAIN_NAME = var.DOMAIN_NAME
}

#Outputs to second pass

output "INSTANCES_PRIVATE_SSH_KEY" {
  value = module.gce.INSTANCES_PRIVATE_SSH_KEY
  sensitive = true
}

output "GPU_INSTANCES_PUBLIC_IP_ADDRESSES" {
  value = module.gce.GPU_INSTANCES_PUBLIC_IP_ADDRESSES
}
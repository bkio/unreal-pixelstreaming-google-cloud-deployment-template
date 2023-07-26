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
  GOOGLE_CREDENTIALS = var.GOOGLE_CREDENTIALS

  VM_ZONES       = local.DECODED_VM_ZONES
  VM_NAME_PREFIX = var.VM_NAME_PREFIX

  ORCHESTRATOR_VM_INSTALL_SH_FILE_CONTENT = file("${path.module}${var.ORCHESTRATOR_VM_INSTALL_SH_FILE_PATH}")
  GPU_VM_INSTALL_SH_FILE_CONTENT          = file("${path.module}${var.GPU_VM_INSTALL_SH_FILE_PATH}")

  ORCHESTRATOR_CONTAINER_NAME = var.ORCHESTRATOR_CONTAINER_NAME
  PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME = var.PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME
  GPU_INSTANCES_PER_ZONE = var.GPU_INSTANCES_PER_ZONE
  MAX_USER_SESSION_PER_INSTANCE = var.MAX_USER_SESSION_PER_INSTANCE

  DOMAIN_NAME = var.DOMAIN_NAME
  ACME_OWNER_EMAIL = var.ACME_OWNER_EMAIL
}
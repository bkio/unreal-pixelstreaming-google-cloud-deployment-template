locals {
  GCE_GPU_VMS_RESOURCE_NAME_PREFIX = "${var.VM_NAME_PREFIX}-gpu-vm"
}

data "google_compute_image" "gpu_vm_image" {
  family  = "debian-11"
  project = "debian-cloud"
}

resource "google_compute_instance" "gpu_vms" {
  count                         = length(var.VM_ZONES)
  name                          = "${local.GCE_GPU_VMS_RESOURCE_NAME_PREFIX}-${count.index}"
  machine_type                  = "custom-6-23040"
  zone                          = element(var.VM_ZONES, count.index)
  allow_stopping_for_update     = true

  tags = ["webrtc"]

  boot_disk {
    initialize_params {
      image = data.google_compute_image.gpu_vm_image.self_link
      size = 10
      type = "pd-ssd"
    }
  }

  scratch_disk {
    interface = "SCSI"
  }

  network_interface {
    subnetwork = google_compute_subnetwork.subnets[count.index].name
    access_config {
      # Ephemeral
      network_tier = "STANDARD"
    }
  }

  metadata = {
    ssh-keys = "orchestrator:${local.SSH_PUBLIC_KEY}"
  }

  enable_display = true

  guest_accelerator {
    type = "nvidia-tesla-t4"
    count = 1
  }

  scheduling {
    preemptible = true
    automatic_restart = false
    provisioning_model = "SPOT"
  }

  # Ensure firewall rule is provisioned before server, so that SSH doesn't fail.
  depends_on = [ google_compute_firewall.allow_ssh ]
}

resource "google_storage_bucket_object" "gpu_vm_install_sh" {
  name   = "scripts-${var.VM_NAME_PREFIX}/gpu_vm_install.sh"
  content = var.GPU_VM_INSTALL_SH_FILE_CONTENT
  bucket = var.TERRAFORM_STATE_BUCKET
  cache_control = "no-cache,max-age=0"
  content_type  = "application/x-shellscript"
}
data "google_storage_object_signed_url" "gpu_vm_install_sh_signed_url" {
  bucket = var.TERRAFORM_STATE_BUCKET
  path   = google_storage_bucket_object.gpu_vm_install_sh.name
  http_method = "GET"
  duration = "5m"
}

resource "null_resource" "post_gpu_vms_creation" {
  provisioner "local-exec" {
    command = "echo '${local.SSH_PRIVATE_KEY}' | ssh -i /dev/stdin -o StrictHostKeyChecking=accept-new -o ConnectTimeout=120 orchestrator@${google_compute_instance.gpu_vms[count.index].network_interface.0.access_config.0.nat_ip} 'gcloud auth configure-docker gcr.io --quiet --project=${var.GOOGLE_CLOUD_PROJECT_ID} && curl -o install_script.sh ${data.google_storage_object_signed_url.gpu_vm_install_sh_signed_url.signed_url} && chmod +x install_script.sh && sudo bash install_script.sh'"
  }
  count = length(google_compute_instance.gpu_vms)
  depends_on = [ google_compute_instance.gpu_vms, google_storage_bucket_object.gpu_vm_install_sh ]
}
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

resource "null_resource" "post_gpu_vms_creation" {
  provisioner "local-exec" {
    command = <<Settings
      echo "${local.SSH_PRIVATE_KEY}" | ssh -i /dev/stdin -o StrictHostKeyChecking=accept-new -o ConnectTimeout=120 orchestrator@${google_compute_instance.gpu_vms[count.index].network_interface.0.access_config.0.nat_ip} 'gcloud auth configure-docker gcr.io --quiet --project="${var.GOOGLE_CLOUD_PROJECT_ID}"'
      ${var.GPU_VM_INSTALL_SH_FILE_CONTENT}
    Settings
  }
  count = length(google_compute_instance.gpu_vms)
  depends_on = [ google_compute_instance.gpu_vms ]
}
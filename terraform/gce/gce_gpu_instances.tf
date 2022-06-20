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

resource "null_resource" "post_gpu_vm_creation_create_local_file" {
  triggers = {
    instance_list = join(",", google_compute_instance.gpu_vms.*.id)
  }
  
  provisioner "local-exec" {
    command = "echo '${var.GPU_VM_INSTALL_SH_FILE_CONTENT}' > install_script_gpu_vm.sh"
  }
}

resource "null_resource" "post_gpu_vm_creation_copy_and_execute_script" {
  triggers = {
    instance_list = join(",", google_compute_instance.gpu_vms.*.id)
  }

  connection {
    type = "ssh"
    user = "orchestrator"
    host = google_compute_instance.gpu_vms[count.index].network_interface.0.access_config.0.nat_ip
    private_key = local.SSH_PRIVATE_KEY
  }

  provisioner "file" {
    source      = "install_script_gpu_vm.sh"
    destination = "/tmp/install_script_gpu_vm.sh"
  }

  provisioner "remote-exec" {
    inline = [
      "gcloud auth configure-docker gcr.io --quiet --project=${var.GOOGLE_CLOUD_PROJECT_ID}",
      "chmod +x /tmp/install_script_gpu_vm.sh",
      "sudo bash /tmp/install_script_gpu_vm.sh"
    ]
  }

  count = length(google_compute_instance.gpu_vms)

  depends_on = [ null_resource.post_gpu_vm_creation_create_local_file ]
}
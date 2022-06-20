locals {
  GCE_ORCHESTRATOR_PUBLIC_IP_RESOURCE_NAME = "${var.VM_NAME_PREFIX}-orchestrator-public-ip"
  GCE_ORCHESTRATOR_VM_RESOURCE_NAME = "${var.VM_NAME_PREFIX}-orchestrator-vm"
  GCE_ORCHESTRATOR_VM_ZONE = element(var.VM_ZONES, 0)
}

resource "google_compute_address" "orchestrator_static_ip_address" {
  name = local.GCE_ORCHESTRATOR_PUBLIC_IP_RESOURCE_NAME
}

data "google_compute_image" "orchestrator_vm_image" {
  family  = "debian-10"
  project = "debian-cloud"
}

resource "google_compute_instance" "orchestrator" {
  name                          = local.GCE_ORCHESTRATOR_VM_RESOURCE_NAME
  machine_type                  = "f1-micro"
  zone                          = local.GCE_ORCHESTRATOR_VM_ZONE
  allow_stopping_for_update     = true

  tags = ["http-server", "https-server"]

  boot_disk {
    initialize_params {
      image = data.google_compute_image.orchestrator_vm_image.self_link
      size = 10
      type = "pd-standard"
    }
  }

  scratch_disk {
    interface = "SCSI"
  }

  network_interface {
    subnetwork = google_compute_subnetwork.subnets[0].name
    access_config {
      nat_ip = google_compute_address.orchestrator_static_ip_address.address
      network_tier = "STANDARD"
    }
  }

  scheduling {
    preemptible = false
    automatic_restart = true
    provisioning_model = "STANDARD"
  }

  # Ensure firewall rule is provisioned before server, so that SSH doesn't fail.
  depends_on = [ google_compute_firewall.allow_ssh ]
}

resource "null_resource" "post_orchestrator_vm_creation" {
  provisioner "local-exec" {
    command = replace(var.ORCHESTRATOR_VM_INSTALL_SH_FILE_CONTENT, "[[EXTERNAL_VAR_DOMAIN_NAME]]", var.DOMAIN_NAME)
  }
  depends_on = [ google_compute_instance.orchestrator ]
}
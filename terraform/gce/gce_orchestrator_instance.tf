locals {
  GCE_ORCHESTRATOR_PUBLIC_IP_RESOURCE_NAME = "${var.VM_NAME_PREFIX}-orchestrator-public-ip"
  GCE_ORCHESTRATOR_VM_RESOURCE_NAME = "${var.VM_NAME_PREFIX}-orchestrator-vm"
  GCE_ORCHESTRATOR_VM_ZONE = element(var.VM_ZONES, 0)
  GCE_ORCHESTRATOR_VM_REGION = join("-", slice(split("-", local.GCE_ORCHESTRATOR_VM_ZONE), 0, length(split("-", local.GCE_ORCHESTRATOR_VM_ZONE)) - 1))
}

resource "google_compute_address" "orchestrator_static_ip_address" {
  name = local.GCE_ORCHESTRATOR_PUBLIC_IP_RESOURCE_NAME
  region = local.GCE_ORCHESTRATOR_VM_REGION
  network_tier = "STANDARD"
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

  metadata = {
    ssh-keys = "orchestrator:${local.SSH_PUBLIC_KEY}"
  }

  # Ensure firewall rule is provisioned before server, so that SSH doesn't fail.
  depends_on = [ google_compute_firewall.allow_ssh ]
}

resource "null_resource" "post_orchestrator_vm_creation_local_operations" {
  triggers = {
    always_run = "${timestamp()}"
  }

  provisioner "local-exec" {
    command = <<-EOT
      echo '${replace(var.ORCHESTRATOR_VM_INSTALL_SH_FILE_CONTENT, "[[EXTERNAL_VAR_DOMAIN_NAME]]", var.DOMAIN_NAME)}' > install_script_orchestrator_vm.sh

      cd ${var.ORCHESTRATOR_SERVICE_DIRECTORY_FULL_PATH}
      mkdir output
      dotnet restore ServicePixelStreamingOrchestrator.csproj
      dotnet publish ServicePixelStreamingOrchestrator.csproj --runtime alpine-x64 --configuration Release --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o output/app/out

      cp Dockerfile output
      cp -r Views output/public

      cd output

      gcloud config set project ${var.GOOGLE_CLOUD_PROJECT_ID}
      gcloud builds submit --tag gcr.io/${var.GOOGLE_CLOUD_PROJECT_ID}/${var.ORCHESTRATOR_CONTAINER_NAME}:latest
    EOT
  }
}

resource "null_resource" "post_orchestrator_vm_creation_copy_and_execute_script" {
  triggers = {
    instance = google_compute_instance.orchestrator.id
  }
  
  connection {
    type = "ssh"
    user = "orchestrator"
    host = google_compute_instance.orchestrator.network_interface.0.access_config.0.nat_ip
    private_key = local.SSH_PRIVATE_KEY
  }

  provisioner "file" {
    source      = "install_script_orchestrator_vm.sh"
    destination = "/tmp/install_script_orchestrator_vm.sh"
  }

  provisioner "remote-exec" {
    inline = [
      "gcloud auth configure-docker gcr.io --quiet --project=${var.GOOGLE_CLOUD_PROJECT_ID}",
      "chmod +x /tmp/install_script_orchestrator_vm.sh",
      "sudo bash /tmp/install_script_orchestrator_vm.sh"
    ]
  }

  depends_on = [ null_resource.post_orchestrator_vm_creation_local_operations ]
}

resource "null_resource" "deploy_orchestrator_app_to_vm" {
  triggers = {
    always_run = "${timestamp()}"
  }
  
  connection {
    type = "ssh"
    user = "orchestrator"
    host = google_compute_instance.orchestrator.network_interface.0.access_config.0.nat_ip
    private_key = local.SSH_PRIVATE_KEY
  }

  provisioner "remote-exec" {
    inline = [
      "bash /opt/scripts/docker_update.sh 8080 ${var.GOOGLE_CLOUD_PROJECT_ID} ${var.ORCHESTRATOR_CONTAINER_NAME} ServicePixelStreamingOrchestrator ${join(",", var.VM_ZONES)} ${var.VM_NAME_PREFIX} ${var.PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME} ${var.MAX_USER_SESSION_PER_INSTANCE} ${base64encode(local.SSH_PRIVATE_KEY)} ${base64encode(var.GOOGLE_CREDENTIALS)}"
    ]
  }

  depends_on = [ null_resource.post_orchestrator_vm_creation_copy_and_execute_script ]
}
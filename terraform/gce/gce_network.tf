locals {
    GCE_VPC_RESOURCE_NAME = "${var.VM_NAME_PREFIX}-vpc"
    GCE_SUBNET_RESOURCE_NAME_PREFIX = "${var.VM_NAME_PREFIX}-subnet"
    GCE_FIREWALL_RESOURCE_NAME_PREFIX = "${var.VM_NAME_PREFIX}-firewall"

    REGION_TO_IP_RANGE = { # Generated from default network in GC
        "us-central1" = "10.128.0.0/20"
        "europe-west1" = "10.132.0.0/20"
        "us-west1" = "10.138.0.0/20"
        "asia-east1" = "10.140.0.0/20"
        "us-east1" = "10.142.0.0/20"
        "asia-northeast1" = "10.146.0.0/20"
        "asia-southeast1" = "10.148.0.0/20"
        "us-east4" = "10.150.0.0/20"
        "australia-southeast1" = "10.152.0.0/20"
        "europe-west2" = "10.154.0.0/20"
        "europe-west3" = "10.156.0.0/20"
        "southamerica-east1" = "10.158.0.0/20"
        "asia-south1" = "10.160.0.0/20"
        "northamerica-northeast1" = "10.162.0.0/20"
        "europe-west4" = "10.164.0.0/20"
        "europe-north1" = "10.166.0.0/20"
        "us-west2" = "10.168.0.0/20"
        "asia-east2" = "10.170.0.0/20"
        "europe-west6" = "10.172.0.0/20"
        "asia-northeast2" = "10.174.0.0/20"
        "asia-northeast3" = "10.178.0.0/20"
        "us-west3" = "10.180.0.0/20"
        "us-west4" = "10.182.0.0/20"
        "asia-southeast2" = "10.184.0.0/20"
        "europe-central2" = "10.186.0.0/20"
        "northamerica-northeast2" = "10.188.0.0/20"
        "asia-south2" = "10.190.0.0/20"
        "australia-southeast2" = "10.192.0.0/20"
        "southamerica-west1" = "10.194.0.0/20"
        "us-east7" = "10.196.0.0/20"
        "europe-west8" = "10.198.0.0/20"
        "europe-west9" = "10.200.0.0/20"
        "us-east5" = "10.202.0.0/20"
        "europe-southwest1" = "10.204.0.0/20"
        "us-south1" = "10.206.0.0/20"
    }
}

resource "google_compute_network" "vpc" {
    name                          =  local.GCE_VPC_RESOURCE_NAME
    auto_create_subnetworks       = "false"
    routing_mode                  = "REGIONAL"
}

resource "google_compute_firewall" "allow_http" {
    name    = "${local.GCE_FIREWALL_RESOURCE_NAME_PREFIX}-allow-http"
    network = google_compute_network.vpc.name
    allow {
        protocol = "tcp"
        ports    = ["80"]
    }
    source_ranges = [
        "0.0.0.0/0"
    ]
    target_tags = ["http-server"] 
    priority = 1000
}
resource "google_compute_firewall" "allow_https" {
    name    = "${local.GCE_FIREWALL_RESOURCE_NAME_PREFIX}-allow-https"
    network = google_compute_network.vpc.name
    allow {
        protocol = "tcp"
        ports    = ["443"]
    }
    source_ranges = [
        "0.0.0.0/0"
    ]
    target_tags = ["https-server"] 
    priority = 1000
}
resource "google_compute_firewall" "allow_webrtc" {
    name    = "${local.GCE_FIREWALL_RESOURCE_NAME_PREFIX}-allow-webrtc"
    network = google_compute_network.vpc.name
    allow {
        protocol = "tcp"
        ports    = ["10000-65535"]
    }
    allow {
        protocol = "udp"
        ports    = ["10000-65535"]
    }
    source_ranges = [
        "0.0.0.0/0"
    ]
    target_tags = ["webrtc"] 
    priority = 1000
}
resource "google_compute_firewall" "allow_icmp" {
    name    = "${local.GCE_FIREWALL_RESOURCE_NAME_PREFIX}-allow-icmp"
    network = google_compute_network.vpc.name
    allow {
        protocol = "icmp"
    }
    source_ranges = [
        "0.0.0.0/0"
    ]
    priority = 65534
}
resource "google_compute_firewall" "allow_ssh" {
    name    = "${local.GCE_FIREWALL_RESOURCE_NAME_PREFIX}-allow-ssh"
    network = google_compute_network.vpc.name
    allow {
        protocol = "tcp"
        ports    = ["22"]
    }
    source_ranges = [
        "0.0.0.0/0"
    ]
    priority = 65534
}

# This statement:
# join("-", slice(split("-", element(var.VM_ZONES, count.index)), 0, length(split("-", element(var.VM_ZONES, count.index))) - 1))
# is zone to region converter.
# Example: europe-west1-b to europe-west1

resource "google_compute_firewall" "allow_internal" {
    name    = "${local.GCE_FIREWALL_RESOURCE_NAME_PREFIX}-allow-internal"
    network = google_compute_network.vpc.name
    allow {
        protocol = "icmp"
    }
    allow {
        protocol = "tcp"
        ports    = ["0-65535"]
    }
    allow {
        protocol = "udp"
        ports    = ["0-65535"]
    }
    source_ranges = [for zone in var.VM_ZONES : local.REGION_TO_IP_RANGE[join("-", slice(split("-", zone), 0, length(split("-", zone)) - 1))]]
    priority = 65534
}

resource "google_compute_subnetwork" "subnets" {
    count         = length(var.VM_ZONES)
    name          = "${local.GCE_SUBNET_RESOURCE_NAME_PREFIX}-subnet-${count.index}"
    ip_cidr_range = local.REGION_TO_IP_RANGE[join("-", slice(split("-", element(var.VM_ZONES, count.index)), 0, length(split("-", element(var.VM_ZONES, count.index))) - 1))]
    network       = google_compute_network.vpc.id
    region        = join("-", slice(split("-", element(var.VM_ZONES, count.index)), 0, length(split("-", element(var.VM_ZONES, count.index))) - 1))
}
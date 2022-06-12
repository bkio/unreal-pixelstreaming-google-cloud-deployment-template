#!/bin/bash

sudo apt update -y
sudo apt install -y \
    	ca-certificates \
    	curl \
    	gnupg \
    	lsb-release \
	build-essential \
	software-properties-common \
	wget
sudo mkdir -p /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian \
  $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update -y
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo groupadd docker
sudo adduser orchestrator
sudo usermod -aG docker orchestrator
sudo systemctl enable docker.service
sudo systemctl enable containerd.service
sudo wget -O- https://developer.download.nvidia.com/compute/cuda/repos/debian11/x86_64/3bf863cc.pub | gpg --dearmor | sudo tee /usr/share/keyrings/nvidia-drivers.gpg
sudo echo 'deb [signed-by=/usr/share/keyrings/nvidia-drivers.gpg] https://developer.download.nvidia.com/compute/cuda/repos/debian11/x86_64/ /' | sudo tee /etc/apt/sources.list.d/nvidia-drivers.list
sudo add-apt-repository -y contrib
sudo apt update -y && sudo apt upgrade -y
curl https://raw.githubusercontent.com/GoogleCloudPlatform/compute-gpu-installation/main/linux/install_gpu_driver.py --output install_gpu_driver.py
sudo python3 install_gpu_driver.py
sudo nvidia-installer --uninstall -s
sudo apt install -y -f nvidia-driver
sudo apt install -y \
	linux-headers-amd64 \
	linux-image-amd64 \
	nvidia-smi \
	libvulkan1 \
	mesa-vulkan-drivers
curl -O http://ftp.no.debian.org/debian/pool/main/v/vulkan-tools/vulkan-tools_1.2.162.0+dfsg1-1_amd64.deb
sudo apt install -y ./*.deb
rm -rf ./*.deb
distribution=$(. /etc/os-release;echo $ID$VERSION_ID) \
      && curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg \
      && curl -s -L https://nvidia.github.io/libnvidia-container/$distribution/libnvidia-container.list | \
            sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | \
            sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
sudo apt-get update
sudo apt-get install -y nvidia-docker2
sudo systemctl restart docker
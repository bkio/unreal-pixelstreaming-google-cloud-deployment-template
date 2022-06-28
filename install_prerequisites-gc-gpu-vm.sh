#!/bin/bash

if sudo test -f "/opt/installation_complete"; then
    exit 0
else
    echo "done" | sudo tee /opt/installation_complete
fi

sudo apt update -y

# Necessary prequisites for docker installation
sudo apt install -y \
    	curl \
		wget \
    	ca-certificates \
    	gnupg \
    	lsb-release \
		build-essential \
		software-properties-common \
		linux-headers-$(uname -r) \
		linux-image-$(uname -r) \
		pciutils \ 
		gcc \
		make

# Docker installation
sudo mkdir -p /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian \
  $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update -y
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo groupadd docker

# orchestrator is the name of user needs access to docker without sudo in my applications, change if necessary.
sudo adduser --disabled-password --gecos "" orchestrator
sudo usermod -aG docker orchestrator
sudo systemctl enable docker.service
sudo systemctl enable containerd.service

sudo usermod -aG sudo orchestrator

curl -fSsl -O https://developer.download.nvidia.com/compute/cuda/11.7.0/local_installers/cuda_11.7.0_515.43.04_linux.run
curl -fSsl -O https://us.download.nvidia.com/XFree86/Linux-x86_64/515.57/NVIDIA-Linux-x86_64-515.57.run
sudo sh cuda_11.7.0_515.43.04_linux.run --silent --toolkit \
	&& rm -rf cuda_11.7.0_515.43.04_linux.run
sudo sh NVIDIA-Linux-x86_64-515.57.run --no-x-check --silent --no-questions --no-nouveau-check --disable-nouveau --run-nvidia-xconfig --install-libglvnd -k `uname -r` \
	&& rm -rf NVIDIA-Linux-x86_64-515.57.run

# Add apt repository for NVIDIA related packages
sudo wget -O- https://developer.download.nvidia.com/compute/cuda/repos/debian11/x86_64/3bf863cc.pub | gpg --dearmor | sudo tee /usr/share/keyrings/nvidia-drivers.gpg
echo "deb [signed-by=/usr/share/keyrings/nvidia-drivers.gpg] https://developer.download.nvidia.com/compute/cuda/repos/debian11/x86_64/ /" | sudo tee /etc/apt/sources.list.d/nvidia-drivers.list

sudo apt install -y libvulkan1

# Vulkan tools and utils do not exists in the default repos
TEMP_DEB="$(mktemp)" && wget -O "$TEMP_DEB" 'http://ftp.no.debian.org/debian/pool/main/v/vulkan-tools/vulkan-tools_1.2.162.0+dfsg1-1_amd64.deb' && sudo dpkg -i "$TEMP_DEB" && rm -rf "$TEMP_DEB"

# Lastly, for being able to use gpus in dockers, we must install nvidia-container-toolkit
distribution=$(. /etc/os-release;echo $ID$VERSION_ID) \
      && curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg \
      && curl -s -L https://nvidia.github.io/libnvidia-container/$distribution/libnvidia-container.list | \
            sed "s#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g" | \
            sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
sudo apt-get update
sudo apt-get install -y nvidia-docker2

# And restart docker
sudo systemctl restart docker
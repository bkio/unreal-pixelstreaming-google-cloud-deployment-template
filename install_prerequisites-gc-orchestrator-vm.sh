#!/bin/bash

if sudo test -f "/opt/installation_complete"; then
    exit 0
else
    echo "done" | sudo tee /opt/installation_complete
fi

# Initial update
sudo apt update -y
sudo apt upgrade -y

# Necessary prequisites for docker installation
sudo apt install -y \
    	ca-certificates \
    	curl \
    	gnupg \
    	lsb-release \
	build-essential \
	software-properties-common \
	wget
	
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
sudo adduser orchestrator
sudo usermod -aG docker orchestrator
sudo systemctl enable docker.service
sudo systemctl enable containerd.service

sudo apt install -y nginx snapd

sudo snap install core; sudo snap refresh core

cd /etc/nginx/sites-available
sudo tee [[EXTERNAL_VAR_DOMAIN_NAME]] > /dev/null <<EOT
server {
    server_name [[EXTERNAL_VAR_DOMAIN_NAME]];

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "Upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
EOT

sudo systemctl reload nginx

sudo snap install --classic certbot

sudo ln -s /snap/bin/certbot /usr/bin/certbot

sudo tee /opt/scripts/docker_update.sh > /dev/null <<EOT
#!/bin/bash

# Arg1: Port
# Arg2: Google Project ID
# Arg3: Container Name
# Arg4: Service Unique Name
# Arg5: Comma separated GPU instances zones
# Arg6: Pixel Streaming GPU instances name prefix
# Arg7: Pixel Streaming Unreal Container Image Name
# Arg8: Max User Session per instance
# Arg9: Compute Engine SSH Private Key Hexed
# Arg10: Plain Credentials (Hex coded json cred.)

gcloud auth configure-docker gcr.io --quiet --project=$2

docker stop $3 || true && docker rm $3 || true

docker system prune --volumes --force

docker pull gcr.io/$2/$3:latest

docker run -d --restart=always -p 8080:8080 --name=$3 \
-e PORT=$1 \
-e PROGRAM_ID=$4 \
-e GOOGLE_CLOUD_PROJECT_ID=$2 \
-e VM_ZONES=$5 \
-e VM_NAME_PREFIX=$6 \
-e PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME=$7 \
-e MAX_USER_SESSION_PER_INSTANCE=$8 \
-e COMPUTE_ENGINE_PLAIN_SSH_PRIVATE_KEY=${9} \
-e GOOGLE_PLAIN_CREDENTIALS=${10} \
gcr.io/$2/$3:latest
EOT
sudo chmod +x /opt/scripts/docker_update.sh

sudo tee /opt/scripts/enable_https.sh > /dev/null <<EOT
#!/bin/bash

if sudo test -f "/opt/https_enabled_[[EXTERNAL_VAR_DOMAIN_NAME]]"; then
    exit 0
else
    echo "done" | sudo tee /opt/https_enabled_[[EXTERNAL_VAR_DOMAIN_NAME]]
fi

sudo certbot --nginx --non-interactive --quiet --agree-tos --redirect --uir --hsts --staple-ocsp --must-staple -d [[EXTERNAL_VAR_DOMAIN_NAME]] --email admin@[[EXTERNAL_VAR_DOMAIN_NAME]]

sudo systemctl reload nginx

sudo crontab -l > certbot_cron
echo "43 6 * * * certbot renew --quiet --post-hook \"systemctl reload nginx\"" >> certbot_cron
sudo crontab certbot_cron
sudo rm certbot_cron
EOT
sudo chmod +x /opt/scripts/enable_https.sh
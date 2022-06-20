## USAGE:

First of all, fork this repository and set up the Github Action Secrets according to the specification written below.

Run github workflow dispatch called ```Terraform Create```; then terraform will set up the environment. When it is complete, check your orchestrator vm's (gateway) public IP address. Then set A records with your domain name in your domain provider system; when you are sure that the records are acknowledged; run github workflow dispatch called ```Enable HTTPS on the Orchestrator VM```. Then it is all done.
If you want to destroy your environment, simply call workflow dispatch ```Terraform Destroy```.

Pixel streaming frontend files are located at ```orchestrator-service/services/ServicePixelStreamingOrchestrator/Views/``` ; feel free to play with the files to change the behaviour or the appearance. Currently it is optimized for both desktop and mobile experience. After your updates, commit and push your changes and run ```Build and Deploy Orchestrator Application``` workflow dispatch.

You can also find embedding example at ```orchestrator-service/services/ServicePixelStreamingOrchestrator/Examples``` ; it is tailored for embedding the frontend (served at $DOMAIN_NAME) into wordpress-elementor setup; but it is very generic so it can be used in other frontend solutions as well.

Underlining .NET framework is https://github.com/bkio/utilities-dotnet-framework an open-source framework for cloud-component abstraction developed by me. It is added to this repository as a submodule.
___
___
___
## Required environment variables to be set in Github Actions Secrets:

### GOOGLE_CLOUD_PROJECT 
Id of your google cloud project 
#### Example: 
```my-google-project```
___
### GOOGLE_CREDENTIALS 
Google service account secrets in json form. The service account should have Editor, Cloud Build Service Account, Cloud Build Editor and Compute Admin permissions added. 
#### Example: 
```json
{
  "type": "service_account",
  "project_id": "...",
  "private_key_id": "...",
  "private_key": "-----BEGIN PRIVATE KEY-----...-----END PRIVATE KEY-----",
  "client_email": "my-service-account-name@my-google-project.iam.gserviceaccount.com",
  "client_id": "...",
  "auth_uri": "https://accounts.google.com/o/oauth2/auth",
  "token_uri": "https://oauth2.googleapis.com/token",
  "auth_provider_x509_cert_url": "https://www.googleapis.com/oauth2/v1/certs",
  "client_x509_cert_url": "..."
}
```
___
### GCP_SERVICE_ACCOUNT_EMAIL 
Email address of the service account 
#### Example: 
```my-service-account-name@my-google-project.iam.gserviceaccount.com```
___
### VM_ZONES
Due to the hard limitations of Google Cloud for creating spot instances in terms of quota, there could only be one spot instance with a GPU in each region. (If you wish to have more, need to get in touch with Google, I have tried and they refused it for me. Large corporations may get it to be sorted out.) Therefore, if you need 4 GPU with VMs (meaning 4*max number of sessions per VM]); type 4 zones as a comma-separated string. 
#### Example: 
```europe-west1-d,europe-west2-b,europe-west3-b,europe-west4-c```
___
### VM_NAME_PREFIX
VM names will be created with names starting with this VM_NAME_PREFIX prefix. For example, if you specify 4 zones in the VM_ZONES variable, there will be 5 VMs created: your-vm-name-prefix-orchestrator, your-vm-name-prefix-gpu-vm-1, your-vm-name-prefix-gpu-vm-2, your-vm-name-prefix-gpu-vm-3, your-vm-name-prefix-gpu-vm-4 Be sure that these VM names are not in use project-wide.
#### Example: 
```your-vm-name-prefix```
___
### PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME
The variable you specified in the Unreal plugin, this docker image in Google Container Registry will be deployed to the GPU instances when a session is loaded for gameplay experience. 
#### Example: 
```your-unreal-application-docker-name```
___
### MAX_USER_SESSION_PER_INSTANCE
Orchestrator will allocate up to the number of user sessions you specified here to each VM. Each session will consume GPU/CPU/Memory resources, so depending on the complexity of your application, up to 4 could be ideal. Even with a basic Unreal template; 5 sessions in a VM did not work for Tesla T4 GPU based configuration.
___
### TERRAFORM_STATE_BUCKET
Name of the bucket in your cloud storage to be used for storing terraform state. 
#### Example: 
```my-google-project-bucket```
___
### DOMAIN_NAME
Your domain name to access the application. 
#### Example: 
```pixelstreaming.yourdomain.com or yourdomain.com```
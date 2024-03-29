## What is Unreal Pixelstreaming Google Cloud Deployment Template?
There are a lot of solutions for managed pixel streaming (server-side rendering) such as pureweb, 3dsource, vagon... Naturally all of these providers add additional profit margin based on minutes/hours of usage per user; which ends up being pretty expensive in a large scale. I have decided to implement this self-deployed google cloud based solution with an orchestrator service implementation in it. It is ensured to be as low cost as possible since it is based on SPOT (preemptible) resources and when there is not any sessions in the VM, the VM is terminated.
___
## How does it work?
A new pixel streaming session request comes; orchestrator checks the VMs, if there is no VM running, starts one and allocates the user to the VM. If the max session per VM capacity is exceeded; boots up a new VM. Therefore some cold-start is expected when there is not any sessions being run in a VM or previous VM is fully utilized. A complex capable health-check system is in place.
___
## Usage:

First of all, fork this repository and set up the Github Action Secrets according to the specification written below.

Run github workflow dispatch called ```Terraform Create/Update```; then terraform will set up the environment. When it is complete, check your orchestrator vm's (gateway) public IP address. Then set A records with your domain name in your domain provider system; when you are sure that the records are acknowledged; run github workflow dispatch called ```Enable HTTPS on the Orchestrator VM```. Then it is all done.
If you want to destroy your environment, simply call workflow dispatch ```Terraform Destroy```.

Pixel streaming frontend files are located at ```orchestrator-service/services/ServicePixelStreamingOrchestrator/Views/``` ; feel free to play with the files to change the behaviour or the appearance. Currently it is optimized for both desktop and mobile experience. After your updates, commit and push your changes and run ```Terraform Create/Update``` workflow dispatch.

You can also find embedding example at ```orchestrator-service/services/ServicePixelStreamingOrchestrator/Examples``` ; it is tailored for embedding the frontend (served at $DOMAIN_NAME) into wordpress-elementor setup; but it is very generic so it can be used in other frontend solutions as well.

Underlining .NET framework is https://github.com/bkio/utilities-dotnet-framework an open-source framework for cloud-component abstraction developed by me. It is added to this repository as a submodule.
___
## Notes:
If you have not requested for a quota upgrade for GPU usage, you may need to ask for that for ```GPUs (all regions)```. For me the initial quota was 0, I got it to be increased up to 4. Google responded and increased pretty much instantly.

- Compute Engine API
- Identity and Access Management (IAM) API
- Container Registry API
- Cloud Build API

must be enabled in your google project before running the action.
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
Google service account secrets in json form. The service account should have Editor, Cloud Build Service Account, Cloud Build Editor, Storage Object Admin and Compute Admin permissions added. 
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
Due to the hard limitations of Google Cloud for creating preemptible/spot instances in terms of quota, there could only be one preemptible/spot instance with a GPU in each region. (If you wish to have more, need to get in touch with Google, I have tried and they refused my request to get it to be increased more than 1. Large corporations may get it to be sorted out, then set ```GPU_INSTANCES_PER_ZONE``` secret accordingly.) Therefore, if you need 4 GPU with VMs (meaning you will have max number of sessions support for 4 * MAX_USER_SESSION_PER_INSTANCE * GPU_INSTANCES_PER_ZONE). Type 4 zones as a comma-separated string. 
#### Example: 
```europe-west1-d,europe-west2-b,europe-west3-b,europe-west4-c```
___
### VM_NAME_PREFIX
VM names will be created with names starting with this VM_NAME_PREFIX prefix. For example, if you specify 4 zones in the VM_ZONES variable, there will be (4 * GPU_INSTANCES_PER_ZONE + 1) VMs created: your-vm-name-prefix-orchestrator-vm, your-vm-name-prefix-gpu-vm-1,your-vm-name-prefix-gpu-vm-2... Be sure that these VM names are not in use project-wide.
#### Example: 
```your-vm-name-prefix```
___
### PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME
The variable you specified in the Unreal plugin, this docker image in Google Container Registry will be deployed to the GPU instances when a session is loaded for gameplay experience. 
#### Example: 
```your-unreal-application-google-docker-repo-name```
___
### ORCHESTRATOR_CONTAINER_NAME
When this pipeline is run, the orchestrator will be built and submitted with this tag.
#### Example:
```orchestrator-docker-google-repo-name```
___
### GPU_INSTANCES_PER_ZONE
How many GPU instances will be allocated per zone. If you manage to convince Google to give you more than 1 preemptible GPU per zone (which I could not), set this to that number. Otherwise 1.
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
```pixelstreaming.yourdomain.com``` or ```yourdomain.com```
___
### ACME_OWNER_EMAIL
Your e-mail address that will be used for ssl certificate generation with LetsEncrypt. Use an e-mail that is -believable- by LetsEncrypt, otherwise no warnings will be given due to such error unless you look into the logs of HTTPS creation stage.
#### Example: 
```valid_email@gmail.com```
___
### CLOUD_API_SECRET_KEYS
You may want to read/write files to cloud storage for storing states, or some other custom API needs. through orchestrator api. (/api/...) For this, orchestrator needs "Storage Object Admin" role and this environment variable to be set for knowing if the request is coming from a trusted source. /api/file is already implemented, you can check its logic. Post request, needs basic authorization key set as one of these secret strings.
#### Example: 
```["secret_string_1", "secret_string_2", ...]```
___
### FILE_API_BUCKET_NAME
Bucket name for orchestrator usage for custom file operations, see description of CLOUD_API_SECRET_KEYS for more details.
#### Example: 
```bucket-name```
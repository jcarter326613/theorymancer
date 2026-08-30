# Bootstrap

This root is applied manually by a project administrator before GitHub Actions
can deploy infrastructure. Use `initialize.ps1` to create the Terraform state
bucket, apply this root and the shared root, and configure the Google provider
for an Identity Platform tenant without placing its OAuth secret in Terraform
state.

The operator needs Terraform, the Google Cloud CLI, a project-administrator
identity, and access to create a Google OAuth Web Client. When local Google
credentials are missing or expired, the script opens one `gcloud auth login
--update-adc` browser flow to refresh both gcloud and Terraform credentials.
It automatically sets the selected project as the Application Default
Credentials quota project and exports it for Terraform, which Firebase requires
for local Terraform runs. The script also sends that quota project with its
Identity Platform REST requests.
The script pauses after it creates the Firebase web app and displays the
redirect URI to register on that client.

Run the development bootstrap from the repository root:

```powershell
.\infrastructure\bootstrap\initialize.ps1 `
  -ProjectId theorymancer `
  -StateBucketName theorymancer-terraform-state `
  -GitHubRepository jcarter326613/theorymancer
```

The script prompts for the OAuth secret as a secure value, sends it directly to
Identity Platform, and does not save it in Secret Manager, Terraform variables,
state, outputs, logs, or GitHub. Identity Platform retains the active provider
configuration. Re-run the script with a new Google-issued secret to rotate it.

Use `-Environment production` only after production DNS and edge routing are
ready. Add `-AutoApprove` only when the Terraform changes were reviewed in
advance.

For an existing infrastructure migration, complete the state imports in
[`../README.md`](../README.md) before running the script.

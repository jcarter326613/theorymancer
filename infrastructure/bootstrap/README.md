# Bootstrap

This root is applied manually by a project administrator before GitHub Actions
can deploy infrastructure. It does not create the Terraform state bucket: that
bucket must already exist so this root can use remote state.

Create `bootstrap.tfvars` from `terraform.tfvars.example`, then follow the
commands in [`../README.md`](../README.md).

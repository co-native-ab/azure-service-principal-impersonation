# 🔐 Azure Service Principal Impersonation

[![MIT License](https://img.shields.io/badge/License-MIT-green.svg)](https://github.com/co-native-ab/azure-service-principal-impersonation/blob/main/LICENSE)
[![Azure](https://img.shields.io/badge/azure-%230072C6.svg?style=flat&logo=microsoftazure&logoColor=white)](https://azure.microsoft.com)
[![Terraform](https://img.shields.io/badge/terraform-%235835CC.svg?style=flat&logo=terraform&logoColor=white)](https://www.terraform.io/)

A prototype for implementing Azure Service Principal Impersonation using Azure Functions and Key Vault. This project demonstrates an approach to temporary privilege elevation in complex Azure environments.

> **⚠️ PROTOTYPE:** This is a demonstration project and not intended for production use without additional security reviews.

## 🌟 Key Features

- **Group-based Access Control**: Impersonate only service principals you have access to
- **Secure Token Handling**: Leverages Azure Key Vault for secure key operations
- **Audit Trail Ready**: Designed with security monitoring in mind
- **Infrastructure as Code**: Complete Terraform setup for reproducibility

## 🔄 How It Works

The following sequence diagram illustrates the Azure Service Principal Impersonation process:

```mermaid
sequenceDiagram
    title Azure Service Principal Impersonation (ASPI) Flow

    participant User
    participant CLI as Azure CLI
    participant Entra as Microsoft Entra ID
    participant FuncApp as Function App
    participant KV as Key Vault
    participant Graph as Microsoft Graph API

    Note over User,CLI: Authentication Phase
    User->>CLI: Login using Azure CLI
    CLI->>Entra: Request authentication
    Entra->>CLI: Authentication response
    CLI->>User: Return the login session

    Note over User,Entra: Token Acquisition Phase
    User->>Entra: Request Function App access token (audience: api://AzureServicePrincipalImpersonation)
    Entra->>User: Return the Function App access token

    Note over User,KV: ASPI Token Generation
    User->>FuncApp: Request ASPI token with Function App access token for a specific group
    activate FuncApp
    FuncApp->>Entra: Validate the Function App access token
    FuncApp->>Entra: Check if user is a member of the requested group
    alt User is not a member
        FuncApp-->>User: Return 403 Forbidden
    else User is a member
        FuncApp->>FuncApp: Generate ASPI token payload
        FuncApp->>KV: Request to sign the ASPI token with managed identity
        KV->>FuncApp: Return the signed ASPI token
        FuncApp->>User: Return the signed ASPI token
    end
    deactivate FuncApp

    Note over User,Graph: Service Principal Impersonation
    User->>Entra: Request impersonation token using ASPI token as client_assertion
    activate Entra
    Entra->>FuncApp: Retrieve OpenID configuration (.well-known/openid-configuration)
    FuncApp->>Entra: Return issuer and JWKS URI
    Entra->>FuncApp: Request public keys (JWKS)
    FuncApp->>KV: Retrieve RSA public keys
    KV->>FuncApp: Return RSA public keys
    FuncApp->>Entra: Return JWKS with public keys
    Entra->>Entra: Validate the ASPI token signature using public keys
    Entra->>Entra: Validate token claims (issuer, audience & subject)
    Entra->>User: Return the Service Principal impersonation token
    deactivate Entra

    Note over User,Graph: Resource Access
    User->>Graph: Access Microsoft Graph API with impersonation token
    Graph->>User: Return requested data
```

## 📋 Prerequisites

Before you begin, ensure you have the following tools installed:

- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) - For Azure resource management
- [Terraform](https://learn.hashicorp.com/tutorials/terraform/install-cli) - For infrastructure provisioning
- [jq](https://stedolan.github.io/jq/download/) - For JSON parsing
- [Azure Functions Core Tools](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local) - For Function App development

All commands in this guide are intended for Unix-like environments. If you're using Windows, consider using [Windows Subsystem for Linux](https://docs.microsoft.com/en-us/windows/wsl/install).

## 🚀 Quick Start

### 1. Prepare Your Environment

Create configuration variables:

```shell
mkdir -p .tmp/
cat <<EOF > .tmp/lab.tfvars
common_name    = "aspi"
environment    = "lab"
location       = "Sweden Central"
location_short = "sc"
EOF
```

### 2. Deploy Infrastructure

```shell
terraform -chdir=terraform/ init
terraform -chdir=terraform/ apply -var-file=../.tmp/lab.tfvars
```

### 3. Deploy Function App

```shell
TF_OUTPUT=$(terraform -chdir=terraform/ output -json configuration)
FUNCTION_APP_NAME=$(jq -r .function_app_name <<< $TF_OUTPUT)
env --chdir=src/ func azure functionapp publish ${FUNCTION_APP_NAME}
```

## 🧪 Testing

### Test with Group Membership

Run the following script to test impersonation when your user has appropriate group membership:

```shell
TF_OUTPUT=$(terraform -chdir=terraform/ output -json configuration)
TENANT_ID=$(jq -r .tenant_id <<< $TF_OUTPUT)
TEST_CASES=( "demo_direct_membership" "demo_with_nested_group" "demo_user_assigned_managed_identity" )
FUNCTION_APP_URL=$(jq -r .function_app_url <<< $TF_OUTPUT)

for TEST_CASE in "${TEST_CASES[@]}"; do
  GROUP_OBJECT_ID=$(jq -r .groups.${TEST_CASE} <<< $TF_OUTPUT)
  TOKEN=$(az account get-access-token --resource api://AzureServicePrincipalImpersonation | jq -r .accessToken)
  ASPI_TOKEN=$(curl --fail -s -H "Authorization: Bearer ${TOKEN}" "https://${FUNCTION_APP_URL}/token?group_object_id=${GROUP_OBJECT_ID}" | jq -r .access_token)
  APPLICATION_ID_OF_SERVICE_PRINCIPAL_TO_IMPERSONATE=$(jq -r .applications.${TEST_CASE} <<< $TF_OUTPUT)
  IMPERSONATION_TOKEN=$(curl --fail -s -X POST "https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=client_credentials' \
    --data-urlencode "client_id=${APPLICATION_ID_OF_SERVICE_PRINCIPAL_TO_IMPERSONATE}" \
    --data-urlencode 'client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer' \
    --data-urlencode 'scope=https://graph.microsoft.com/.default' \
    --data-urlencode "client_assertion=${ASPI_TOKEN}" \
    | jq -r .access_token)
  APP_NAME=$(curl --fail -s -H "Authorization: Bearer ${IMPERSONATION_TOKEN}" "https://graph.microsoft.com/v1.0/servicePrincipals(appId='${APPLICATION_ID_OF_SERVICE_PRINCIPAL_TO_IMPERSONATE}')" | jq -r .displayName)
  echo "${TEST_CASE}: ${APP_NAME}"
done
```

Expected output:

```
demo_direct_membership: aspi-demo-direct-membership
demo_with_nested_group: aspi-demo-with-nested-group
demo_user_assigned_managed_identity: aspi-demo-user-assigned-managed-identity
```

### Test Without Membership

Test access denial when user lacks group membership:

```shell
TF_OUTPUT=$(terraform -chdir=terraform/ output -json configuration)
TENANT_ID=$(jq -r .tenant_id <<< $TF_OUTPUT)
GROUP_OBJECT_ID=$(jq -r .groups.demo_without_members <<< $TF_OUTPUT)
TOKEN=$(az account get-access-token --resource api://AzureServicePrincipalImpersonation | jq -r .accessToken)
curl -v -H "Authorization: Bearer ${TOKEN}" "https://${FUNCTION_APP_URL}/token?group_object_id=${GROUP_OBJECT_ID}"
```

## 🔍 Use Cases

- **DevOps Automation**: Securely automate tasks requiring different service principal permissions
- **Temporary Access**: Grant time-limited access to service principals
- **Cross-Team Collaboration**: Allow teams to use service principals without sharing credentials

## ⭐ Support This Project

If you find this project useful, please consider giving it a star on GitHub! It helps others discover the project and encourages continued development.

[![Star this project](https://img.shields.io/badge/⭐-Star_this_project-yellow?style=for-the-badge)](https://github.com/co-native-ab/azure-service-principal-impersonation)

## 📜 License

This project is licensed under the [MIT License](LICENSE).

terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "4.19.0"
    }
    azidentity = {
      source  = "co-native-ab/azidentity"
      version = "0.0.12"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "3.1.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "3.6.3"
    }
  }
}

provider "azidentity" {}

ephemeral "azidentity_azure_cli_account" "this" {}

provider "azurerm" {
  features {}
  subscription_id     = ephemeral.azidentity_azure_cli_account.this.subscription_id
  storage_use_azuread = true
}

data "azurerm_client_config" "this" {}

data "azuread_application_published_app_ids" "well_known" {}

resource "azurerm_resource_group" "this" {
  name     = "rg-${var.environment}-${var.location_short}-${var.common_name}"
  location = var.location
}

resource "azurerm_storage_account" "this" {
  name                      = "sa${var.environment}${var.location_short}${var.common_name}"
  resource_group_name       = azurerm_resource_group.this.name
  location                  = azurerm_resource_group.this.location
  account_tier              = "Standard"
  account_replication_type  = "LRS"
  local_user_enabled        = false
  shared_access_key_enabled = false
}

resource "azurerm_role_assignment" "uami_storage_account_contributor" {
  scope                = azurerm_storage_account.this.id
  role_definition_name = "Storage Account Contributor"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

resource "azurerm_role_assignment" "current_storage_account_contributor" {
  scope                = azurerm_storage_account.this.id
  role_definition_name = "Storage Account Contributor"
  principal_id         = data.azurerm_client_config.this.object_id
}

resource "azurerm_log_analytics_workspace" "this" {
  name                          = "law-${var.environment}-${var.location_short}-${var.common_name}"
  resource_group_name           = azurerm_resource_group.this.name
  location                      = azurerm_resource_group.this.location
  sku                           = "PerGB2018"
  retention_in_days             = 30
  local_authentication_disabled = true
}

resource "azurerm_application_insights" "this" {
  name                          = "appi-${var.environment}-${var.location_short}-${var.common_name}"
  workspace_id                  = azurerm_log_analytics_workspace.this.id
  resource_group_name           = azurerm_resource_group.this.name
  location                      = azurerm_resource_group.this.location
  application_type              = "web"
  local_authentication_disabled = true
}

resource "azurerm_role_assignment" "uami_monitoring_metrics_publisher" {
  scope                = azurerm_application_insights.this.id
  role_definition_name = "Monitoring Metrics Publisher"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

resource "azurerm_role_assignment" "current_monitoring_metrics_publisher" {
  scope                = azurerm_application_insights.this.id
  role_definition_name = "Monitoring Metrics Publisher"
  principal_id         = data.azurerm_client_config.this.object_id
}

resource "azurerm_service_plan" "this" {
  name                = "asp-${var.environment}-${var.location_short}-${var.common_name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  os_type             = "Linux"
  sku_name            = "P0v3"
  worker_count        = 1
}

resource "azurerm_key_vault" "this" {
  name                       = "kv-${var.environment}-${var.location_short}-${var.common_name}"
  location                   = azurerm_resource_group.this.location
  resource_group_name        = azurerm_resource_group.this.name
  tenant_id                  = data.azurerm_client_config.this.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  enable_rbac_authorization  = true
}

resource "azurerm_role_assignment" "uami_key_vault_crypto_user" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Crypto User"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

resource "azurerm_role_assignment" "current_key_vault_administrator" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = data.azurerm_client_config.this.object_id
}

resource "azurerm_key_vault_key" "openid_connect_jwks" {
  name         = "openid-connect-jwks"
  key_vault_id = azurerm_role_assignment.current_key_vault_administrator.scope
  key_type     = "RSA"
  key_size     = 4096

  key_opts = [
    "sign",
    "verify",
  ]

  rotation_policy {
    automatic {
      time_after_creation = "P7D"
    }

    expire_after         = "P29D"
    notify_before_expiry = "P7D"
  }
}

resource "azurerm_linux_function_app" "this" {
  name                = "fn-${var.environment}-${var.location_short}-${var.common_name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location

  storage_account_name          = azurerm_storage_account.this.name
  storage_uses_managed_identity = true
  service_plan_id               = azurerm_service_plan.this.id

  site_config {
    application_insights_connection_string = azurerm_application_insights.this.connection_string
    application_stack {
      use_dotnet_isolated_runtime = true
      dotnet_version              = "9.0"
    }
  }

  app_settings = {
    WEBSITE_RUN_FROM_PACKAGE                  = "1"
    APPLICATIONINSIGHTS_AUTHENTICATION_STRING = "Authorization=AAD;ClientId=${azurerm_user_assigned_identity.this.client_id}"
    KEY_VAULT_URL                             = azurerm_key_vault.this.vault_uri
    KEY_VAULT_CLIENT_ID                       = azurerm_user_assigned_identity.this.client_id
    KEY_VAULT_OPENID_CONNECT_JWKS             = azurerm_key_vault_key.openid_connect_jwks.name
    JWT_ISSUER                                = "https://login.microsoftonline.com/${data.azurerm_client_config.this.tenant_id}/v2.0"
    JWT_AUDIENCE                              = azuread_application.azure_service_principal_impersonation.client_id
  }

  identity {
    type = "UserAssigned"
    identity_ids = [
      azurerm_user_assigned_identity.this.id,
    ]
  }

  lifecycle {
    replace_triggered_by = [azurerm_service_plan.this]
    ignore_changes = [
      tags["hidden-link: /app-insights-conn-string"],
      tags["hidden-link: /app-insights-instrumentation-key"],
      tags["hidden-link: /app-insights-resource-id"],
    ]
  }
}

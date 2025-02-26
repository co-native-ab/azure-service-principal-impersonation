#
# Direct membership
#

resource "azuread_group" "demo_direct_membership" {
  display_name            = "${var.common_name}-demo-group-direct-membership"
  security_enabled        = true
  prevent_duplicate_names = true
}

resource "azuread_group_member" "demo_direct_membership" {
  group_object_id  = azuread_group.demo_direct_membership.object_id
  member_object_id = data.azurerm_client_config.this.object_id
}

resource "azuread_application" "demo_direct_membership" {
  display_name            = "${var.common_name}-demo-direct-membership"
  prevent_duplicate_names = true
}

resource "azuread_application_federated_identity_credential" "demo_direct_membership" {
  application_id = azuread_application.demo_direct_membership.id
  display_name   = "${var.common_name}-demo-direct-membership"
  description    = "Demo federated identity credential"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://${azurerm_linux_function_app.this.default_hostname}"
  subject        = azuread_group.demo_direct_membership.object_id
}

resource "azuread_service_principal" "demo_direct_membership" {
  client_id = azuread_application.demo_direct_membership.client_id
}

#
# Nested membership
#

resource "azuread_group" "demo_with_nested_group" {
  display_name            = "${var.common_name}-demo-group-with-nested-group"
  security_enabled        = true
  prevent_duplicate_names = true
}

resource "azuread_group_member" "demo_with_nested_group" {
  group_object_id  = azuread_group.demo_with_nested_group.object_id
  member_object_id = azuread_group.demo_direct_membership.object_id
}

resource "azuread_application" "demo_with_nested_group" {
  display_name            = "${var.common_name}-demo-with-nested-group"
  prevent_duplicate_names = true
}

resource "azuread_application_federated_identity_credential" "demo_with_nested_group" {
  application_id = azuread_application.demo_with_nested_group.id
  display_name   = "${var.common_name}-demo-with-nested-group"
  description    = "Demo federated identity credential"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://${azurerm_linux_function_app.this.default_hostname}"
  subject        = azuread_group.demo_with_nested_group.object_id
}

resource "azuread_service_principal" "demo_with_nested_group" {
  client_id = azuread_application.demo_with_nested_group.client_id
}

#
# Without membership
#

resource "azuread_group" "demo_without_members" {
  display_name            = "${var.common_name}-demo-group-without-members"
  security_enabled        = true
  prevent_duplicate_names = true
}

resource "azuread_application" "demo_without_members" {
  display_name            = "${var.common_name}-demo-without-members"
  prevent_duplicate_names = true
}

resource "azuread_application_federated_identity_credential" "demo_without_members" {
  application_id = azuread_application.demo_without_members.id
  display_name   = "${var.common_name}-demo-without-members"
  description    = "Demo federated identity credential"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://${azurerm_linux_function_app.this.default_hostname}"
  subject        = azuread_group.demo_without_members.object_id
}

resource "azuread_service_principal" "demo_without_members" {
  client_id = azuread_application.demo_without_members.client_id
}

#
# User Assigned Managed Identity
#

resource "azuread_group" "demo_user_assigned_managed_identity" {
  display_name            = "${var.common_name}-demo-group-user-assigned-managed-identity"
  security_enabled        = true
  prevent_duplicate_names = true
}

resource "azuread_group_member" "demo_user_assigned_managed_identity" {
  group_object_id  = azuread_group.demo_user_assigned_managed_identity.object_id
  member_object_id = data.azurerm_client_config.this.object_id
}

resource "azurerm_user_assigned_identity" "demo_user_assigned_managed_identity" {
  name                = "${var.common_name}-demo-user-assigned-managed-identity"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
}

resource "azurerm_federated_identity_credential" "demo_user_assigned_managed_identity" {
  name                = "${var.common_name}-demo-user-assigned-managed-identity"
  resource_group_name = azurerm_resource_group.this.name
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://${azurerm_linux_function_app.this.default_hostname}"
  parent_id           = azurerm_user_assigned_identity.demo_user_assigned_managed_identity.id
  subject             = azuread_group.demo_user_assigned_managed_identity.object_id
}

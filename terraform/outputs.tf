output "configuration" {
  description = "The exported configuration"
  value = {
    function_app_url    = azurerm_linux_function_app.this.default_hostname
    function_app_name   = azurerm_linux_function_app.this.name
    tenant_id           = data.azurerm_client_config.this.tenant_id
    aspi_identifier_uri = one(azuread_application.azure_service_principal_impersonation.identifier_uris)
    groups = {
      demo_direct_membership              = azuread_group.demo_direct_membership.object_id
      demo_with_nested_group              = azuread_group.demo_with_nested_group.object_id
      demo_without_members                = azuread_group.demo_without_members.object_id
      demo_user_assigned_managed_identity = azuread_group.demo_user_assigned_managed_identity.object_id
    }
    applications = {
      demo_direct_membership              = azuread_application.demo_direct_membership.client_id
      demo_with_nested_group              = azuread_application.demo_with_nested_group.client_id
      demo_without_members                = azuread_application.demo_without_members.client_id
      demo_user_assigned_managed_identity = azurerm_user_assigned_identity.demo_user_assigned_managed_identity.client_id
    }
  }
}

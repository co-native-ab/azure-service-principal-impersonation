resource "azurerm_user_assigned_identity" "this" {
  name                = "uami-${var.environment}-${var.location_short}-${var.common_name}"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
}

resource "azuread_service_principal" "microsoft_graph" {
  client_id    = data.azuread_application_published_app_ids.well_known.result.MicrosoftGraph
  use_existing = true
}

resource "azuread_app_role_assignment" "uami_microsoft_graph_user_read_all" {
  app_role_id         = azuread_service_principal.microsoft_graph.app_role_ids["GroupMember.Read.All"]
  principal_object_id = azurerm_user_assigned_identity.this.principal_id
  resource_object_id  = azuread_service_principal.microsoft_graph.object_id
}

resource "azuread_app_role_assignment" "uami_microsoft_graph_user_read_basic_all" {
  app_role_id         = azuread_service_principal.microsoft_graph.app_role_ids["User.ReadBasic.All"]
  principal_object_id = azurerm_user_assigned_identity.this.principal_id
  resource_object_id  = azuread_service_principal.microsoft_graph.object_id
}

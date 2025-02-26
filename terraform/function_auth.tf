resource "random_uuid" "azure_service_principal_impersonation_oauth2_permission_scope_user_impersonation" {}

resource "azuread_application" "azure_service_principal_impersonation" {
  display_name    = "AzureServicePrincipalImpersonation"
  identifier_uris = ["api://AzureServicePrincipalImpersonation"]

  api {
    requested_access_token_version = 2

    oauth2_permission_scope {
      admin_consent_description  = "Grants the app the ability to act as the signed-in user"
      admin_consent_display_name = "Act as the user"
      enabled                    = true
      id                         = random_uuid.azure_service_principal_impersonation_oauth2_permission_scope_user_impersonation.result
      type                       = "User"
      user_consent_description   = "Grants the app the ability to act as you"
      user_consent_display_name  = "Act as you"
      value                      = "user_impersonation"
    }
  }
}

resource "azuread_application_pre_authorized" "azure_service_principal_impersonation_azure_cli" {
  application_id       = azuread_application.azure_service_principal_impersonation.id
  authorized_client_id = data.azuread_application_published_app_ids.well_known.result["MicrosoftAzureCli"]

  permission_ids = [
    random_uuid.azure_service_principal_impersonation_oauth2_permission_scope_user_impersonation.result
  ]
}

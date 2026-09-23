// Private ingress successor design. This module is authored and compiles, but the POC deploys no
// endpoint from it, because the subscription does not currently satisfy either prerequisite:
//
//   1. An App Service plan at B1 or better that stays there. F1 Free and D1 Shared cannot host a
//      private endpoint, and an external process returns the POC plan to F1 on roughly a 24 hour
//      cycle, so an endpoint provisioned today would not survive the next degradation.
//   2. A virtual network in the same region as the apps, with a linked privatelink.azurewebsites.net
//      private DNS zone. No such zone, gateway, or peering exists in the subscription today.
//
// Set ingressMode to Private in the parent template once both hold.

targetScope = 'resourceGroup'

@description('Azure region for the private endpoint. Must match the region of the target subnet.')
param location string

@description('Deterministic name of the private endpoint.')
@minLength(2)
@maxLength(80)
param privateEndpointName string

@description('Resource ID of the App Service site this endpoint fronts.')
param siteResourceId string

@description('Resource ID of the subnet hosting the endpoint. Private endpoint network policies must be disabled on it.')
param subnetResourceId string

@description('Resource ID of the privatelink.azurewebsites.net private DNS zone linked to the virtual network.')
param privateDnsZoneResourceId string

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: privateEndpointName
  location: location
  properties: {
    subnet: {
      id: subnetResourceId
    }
    privateLinkServiceConnections: [
      {
        name: privateEndpointName
        properties: {
          privateLinkServiceId: siteResourceId
          // The sites subresource fronts the app itself. A plan-level target would not exist.
          groupIds: [
            'sites'
          ]
        }
      }
    ]
  }

  // The sites group registers A records for both the application hostname and the scm hostname in the
  // linked zone. Resolving the application hostname privately while scm stays public produces a
  // working demo and a broken deployment, so a single zone group must cover both.
  resource dnsZoneGroup 'privateDnsZoneGroups@2024-05-01' = {
    name: '${privateEndpointName}-dns'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: 'privatelink-azurewebsites-net'
          properties: {
            privateDnsZoneId: privateDnsZoneResourceId
          }
        }
      ]
    }
  }
}

@description('Resource ID of the private endpoint.')
output privateEndpointId string = privateEndpoint.id

@description('Name of the private endpoint.')
output privateEndpointName string = privateEndpoint.name

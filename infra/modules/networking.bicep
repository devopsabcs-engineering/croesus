// networking.bicep
// Provisions the private-networking substrate that keeps the Key Vault off the
// public internet while still reachable by the API Web App:
//   * croesus-vnet (10.10.0.0/16) with two subnets:
//       - snet-pe  (10.10.1.0/24) hosts the Key Vault private endpoint;
//         privateEndpointNetworkPolicies is Disabled so the PE can bind.
//       - snet-app (10.10.2.0/24) is delegated to Microsoft.Web/serverFarms
//         for App Service regional VNet integration.
//   * privatelink.vaultcore.azure.net private DNS zone, linked to the VNet so
//     the vault FQDN resolves to the private endpoint from inside the VNet.
//
// The Key Vault private endpoint and its DNS zone group are declared in
// keyvault.bicep (co-located with the vault so the PE naturally depends on it);
// this module only exposes the subnet and zone resource IDs it needs.

@description('Azure region for the virtual network and private DNS zone link.')
param location string

@description('Name of the virtual network.')
param vnetName string = 'croesus-vnet'

@description('Address space for the virtual network.')
param vnetAddressPrefix string = '10.10.0.0/16'

@description('Name of the subnet that hosts private endpoints.')
param peSubnetName string = 'snet-pe'

@description('Address prefix for the private endpoint subnet.')
param peSubnetPrefix string = '10.10.1.0/24'

@description('Name of the subnet delegated to App Service regional VNet integration.')
param appSubnetName string = 'snet-app'

@description('Address prefix for the App Service integration subnet.')
param appSubnetPrefix string = '10.10.2.0/24'

@description('Name of the private DNS zone for Key Vault private link.')
param keyVaultPrivateDnsZoneName string = 'privatelink.vaultcore.azure.net'

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: peSubnetName
        properties: {
          addressPrefixes: [
            peSubnetPrefix
          ]
          // Private endpoints require network policies disabled on their subnet.
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: appSubnetName
        properties: {
          addressPrefixes: [
            appSubnetPrefix
          ]
          // Regional VNet integration requires a subnet delegated to serverFarms.
          delegations: [
            {
              name: 'appservice-delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
    ]
  }
}

// Private DNS zone so <vault>.vault.azure.net resolves to the private endpoint
// address from within the VNet. Registration is disabled (PE A-records are
// managed by the private DNS zone group, not VM auto-registration).
resource keyVaultPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: keyVaultPrivateDnsZoneName
  location: 'global'
}

resource keyVaultPrivateDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: keyVaultPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

@description('Resource ID of the App Service integration subnet (snet-app).')
output appSubnetId string = '${vnet.id}/subnets/${appSubnetName}'

@description('Resource ID of the private endpoint subnet (snet-pe).')
output peSubnetId string = '${vnet.id}/subnets/${peSubnetName}'

@description('Resource ID of the Key Vault private DNS zone.')
output keyVaultPrivateDnsZoneId string = keyVaultPrivateDnsZone.id

@description('Resource ID of the virtual network.')
output vnetId string = vnet.id

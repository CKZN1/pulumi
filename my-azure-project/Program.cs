using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Pulumi.AzureNative.ApiManagement;
using Pulumi.AzureNative.CosmosDB;
using Pulumi.AzureNative.ApiManagement.Inputs;
using Pulumi.AzureNative.KeyVault;
using Pulumi.AzureNative.KeyVault.Inputs;
using System.Collections.Generic;
using Pulumi.AzureNative.CosmosDB.Inputs;
using Pulumi.AzureNative.ApplicationInsights;

return await Pulumi.Deployment.RunAsync(() =>
{
    // Create an Azure Resource Group
    var resourceGroup = new ResourceGroup("resourceGroup");
    

    // Get current Azure configuration
    var currentConfig = Pulumi.AzureNative.Authorization.GetClientConfig.Invoke();
    
    // Create a Key Vault for storing secrets
    var keyVault = new Vault("keyVault", new VaultArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Properties = new VaultPropertiesArgs
        {
            TenantId = currentConfig.Apply(config => config.TenantId),
            Sku = new Pulumi.AzureNative.KeyVault.Inputs.SkuArgs
            {
                Family = "A",
                Name = Pulumi.AzureNative.KeyVault.SkuName.Standard
            },
            AccessPolicies = new[]
            {
                new AccessPolicyEntryArgs
                {
                    TenantId = currentConfig.Apply(config => config.TenantId),
                    ObjectId = currentConfig.Apply(config => config.ObjectId),
                    Permissions = new PermissionsArgs
                    {
                        Keys = { "get", "list", "create", "delete", "update", "backup", "restore" },
                        Secrets = { "get", "list", "set", "delete", "recover", "backup", "restore" }
                    }
                }
            }
        }
    });

    // Create an Azure Storage Account for static website hosting
    var storageAccount = new StorageAccount("sa", new StorageAccountArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Sku = new Pulumi.AzureNative.Storage.Inputs.SkuArgs
        {
            Name = Pulumi.AzureNative.Storage.SkuName.Standard_LRS
        },
        Kind = Pulumi.AzureNative.Storage.Kind.StorageV2,
        AllowBlobPublicAccess = true
    });

    // Enable static website on storage account (requires separate call)
    var staticWebsite = new StorageAccountStaticWebsite("staticWebsite", new StorageAccountStaticWebsiteArgs
    {
        ResourceGroupName = resourceGroup.Name,
        AccountName = storageAccount.Name,
        IndexDocument = "index.html",
        Error404Document = "404.html"
    });

    // Get storage account keys
    var storageAccountKeys = ListStorageAccountKeys.Invoke(new ListStorageAccountKeysInvokeArgs
    {
        ResourceGroupName = resourceGroup.Name,
        AccountName = storageAccount.Name
    });

    var primaryStorageKey = storageAccountKeys.Apply(accountKeys =>
    {
        var firstKey = accountKeys.Keys[0].Value;
        return firstKey;
    });

    // Create Cosmos DB Account (fix naming issue)
    var cosmosAccount = new DatabaseAccount("cosmosdb", new DatabaseAccountArgs
    {
        ResourceGroupName = resourceGroup.Name,
        DatabaseAccountOfferType = DatabaseAccountOfferType.Standard,
        Locations = new[]
        {
            new LocationArgs
            {
                LocationName = "East US",
                FailoverPriority = 0
            }
        },
        ConsistencyPolicy = new ConsistencyPolicyArgs
        {
            DefaultConsistencyLevel = DefaultConsistencyLevel.Session
        }
    });

    // Get Cosmos DB connection string
    var cosmosKeys = ListDatabaseAccountKeys.Invoke(new ListDatabaseAccountKeysInvokeArgs
    {
        ResourceGroupName = resourceGroup.Name,
        AccountName = cosmosAccount.Name
    });

    // Create Log Analytics Workspace (required for Application Insights)
    var logAnalyticsWorkspace = new Pulumi.AzureNative.OperationalInsights.Workspace("logAnalytics", new()
    {
        ResourceGroupName = resourceGroup.Name,
        Sku = new Pulumi.AzureNative.OperationalInsights.Inputs.WorkspaceSkuArgs
        {
            Name = "PerGB2018"
        },
        RetentionInDays = 30
    });

    // Create Application Insights with Log Analytics workspace
    var appInsights = new Component("appInsights", new ComponentArgs
    {
        ResourceGroupName = resourceGroup.Name,
        ApplicationType = ApplicationType.Web,
        Kind = "web",
        WorkspaceResourceId = logAnalyticsWorkspace.Id
    });

    // Create App Service Plan with custom auto scaling
    var appServicePlan = new AppServicePlan("appServicePlan", new AppServicePlanArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Sku = new SkuDescriptionArgs
        {
            Name = "S1",
            Tier = "Standard",
            Capacity = 1
        }
    });

    // // Create Auto Scale Settings for App Service Plan
    // var autoScaleSettings = new AutoscaleSetting("autoScaleSettings", new AutoscaleSettingArgs
    // {
    //     ResourceGroupName = resourceGroup.Name,
    //     TargetResourceUri = appServicePlan.Id,
    //     Enabled = true,
    //     Profiles = new[]
    //     {
    //         new AutoscaleProfileArgs
    //         {
    //             Name = "defaultProfile",
    //             Capacity = new ScaleCapacityArgs
    //             {
    //                 Minimum = "1",
    //                 Maximum = "10",
    //                 Default = "1"
    //             },
    //             Rules = new[]
    //             {
    //                 new ScaleRuleArgs
    //                 {
    //                     MetricTrigger = new MetricTriggerArgs
    //                     {
    //                         MetricName = "CpuPercentage",
    //                         MetricResourceUri = appServicePlan.Id,
    //                         TimeGrain = "PT1M",
    //                         Statistic = MetricStatisticType.Average,
    //                         TimeWindow = "PT10M",
    //                         TimeAggregation = TimeAggregationType.Average,
    //                         Operator = ComparisonOperationType.GreaterThan,
    //                         Threshold = 70
    //                     },
    //                     ScaleAction = new ScaleActionArgs
    //                     {
    //                         Direction = ScaleDirection.Increase,
    //                         Type = ScaleType.ChangeCount,
    //                         Value = "1",
    //                         Cooldown = "PT10M"
    //                     }
    //                 },
    //                 new ScaleRuleArgs
    //                 {
    //                     MetricTrigger = new MetricTriggerArgs
    //                     {
    //                         MetricName = "CpuPercentage",
    //                         MetricResourceUri = appServicePlan.Id,
    //                         TimeGrain = "PT1M",
    //                         Statistic = MetricStatisticType.Average,
    //                         TimeWindow = "PT10M",
    //                         TimeAggregation = TimeAggregationType.Average,
    //                         Operator = ComparisonOperationType.LessThan,
    //                         Threshold = 25
    //                     },
    //                     ScaleAction = new ScaleActionArgs
    //                     {
    //                         Direction = ScaleDirection.Decrease,
    //                         Type = ScaleType.ChangeCount,
    //                         Value = "1",
    //                         Cooldown = "PT10M"
    //                     }
    //                 }
    //             }
    //         }
    //     }
    // });

    // Create Web App with temp slot
    var webApp = new WebApp("webApp", new WebAppArgs
    {
        ResourceGroupName = resourceGroup.Name,
        ServerFarmId = appServicePlan.Id,
        SiteConfig = new SiteConfigArgs
        {
            AppSettings = new[]
            {
                new NameValuePairArgs
                {
                    Name = "APPINSIGHTS_INSTRUMENTATIONKEY",
                    Value = appInsights.InstrumentationKey
                },
                new NameValuePairArgs
                {
                    Name = "CosmosConnectionString",
                    Value = cosmosKeys.Apply(keys => 
                        $"AccountEndpoint={cosmosAccount.DocumentEndpoint};AccountKey={keys.PrimaryMasterKey};")
                }
            }
        }
    });

    // Create deployment slot (temp slot)
    var deploymentSlot = new WebAppSlot("tempSlot", new WebAppSlotArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Name = webApp.Name,
        Slot = "temp",
        ServerFarmId = appServicePlan.Id,
        SiteConfig = new SiteConfigArgs
        {
            AppSettings = new[]
            {
                new NameValuePairArgs
                {
                    Name = "APPINSIGHTS_INSTRUMENTATIONKEY",
                    Value = appInsights.InstrumentationKey
                },
                new NameValuePairArgs
                {
                    Name = "CosmosConnectionString",
                    Value = cosmosKeys.Apply(keys => 
                        $"AccountEndpoint={cosmosAccount.DocumentEndpoint};AccountKey={keys.PrimaryMasterKey};")
                }
            }
        }
    });

    // Create API Management Service (fix Developer SKU settings)
    var apiManagement = new ApiManagementService("apiManagement", new ApiManagementServiceArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Sku = new ApiManagementServiceSkuPropertiesArgs
        {
            Name = SkuType.Developer,
            Capacity = 1
        },
        PublisherEmail = "admin@example.com", // Replace with your email
        PublisherName = "Your Organization", // Replace with your organization name
        EnableClientCertificate = true // Developer SKU requires this to be true
    });

    // Create API in API Management
    var api = new Api("sampleApi", new ApiArgs
    {
        ResourceGroupName = resourceGroup.Name,
        ServiceName = apiManagement.Name,
        DisplayName = "Sample API",
        Path = "sample",
        Protocols = { Protocol.Https },
        ServiceUrl = webApp.DefaultHostName.Apply(hostname => $"https://{hostname}")
    });

    // Create simple API Policy (without named values for now due to Key Vault complexity)
    var apiPolicy = new ApiPolicy("apiPolicy", new ApiPolicyArgs
    {
        ResourceGroupName = resourceGroup.Name,
        ServiceName = apiManagement.Name,
        ApiId = api.Name,
        Value = @"
        <policies>
            <inbound>
                <base />
                <set-header name='X-Powered-By' exists-action='override'>
                    <value>Pulumi + Azure</value>
                </set-header>
            </inbound>
            <backend>
                <base />
            </backend>
            <outbound>
                <base />
            </outbound>
            <on-error>
                <base />
            </on-error>
        </policies>"
    });

    // Output list of created resources
    return new Dictionary<string, object?>
    {
        ["resourceGroupName"] = resourceGroup.Name,
        ["storageAccountName"] = storageAccount.Name,
        ["storageAccountPrimaryKey"] = primaryStorageKey,
        ["staticWebsiteUrl"] = storageAccount.PrimaryEndpoints.Apply(endpoints => endpoints.Web),
        ["cosmosAccountName"] = cosmosAccount.Name,
        ["cosmosEndpoint"] = cosmosAccount.DocumentEndpoint,
        ["appInsightsInstrumentationKey"] = appInsights.InstrumentationKey,
        ["appServicePlanName"] = appServicePlan.Name,
        ["webAppName"] = webApp.Name,
        ["webAppUrl"] = webApp.DefaultHostName.Apply(hostname => $"https://{hostname}"),
        ["deploymentSlotUrl"] = deploymentSlot.DefaultHostName.Apply(hostname => $"https://{hostname}"),
        ["apiManagementName"] = apiManagement.Name,
        ["apiManagementUrl"] = apiManagement.GatewayUrl,
        ["keyVaultName"] = keyVault.Name,
        ["keyVaultUri"] = keyVault.Properties.Apply(p => p.VaultUri),
        ["resourcesCreated"] = new[]
        {
            "Resource Group",
            "Key Vault",
            "Storage Account (with static website)",
            "Cosmos DB Account",
            "Application Insights",
            "App Service Plan (with auto-scaling)",
            "Web App",
            "Deployment Slot (temp)",
            "API Management Service",
            "API with Policy"
        }
    };
});
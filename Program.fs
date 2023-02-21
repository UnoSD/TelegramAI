module Program

open Pulumi.FSharp.AzureNative.CognitiveServices
open Pulumi.FSharp.AzureNative.Resources.Inputs
open Pulumi.FSharp.AzureNative.KeyVault.Inputs
open Pulumi.FSharp.AzureNative.Authorization
open Pulumi.FSharp.AzureNative.Storage.Inputs
open Pulumi.FSharp.AzureNative.Logic.Inputs
open Pulumi.FSharp.NamingConventions.Azure
open Pulumi.AzureNative.CognitiveServices
open Pulumi.FSharp.AzureNative.Resources
open Pulumi.FSharp.AzureNative.KeyVault
open Pulumi.FSharp.AzureNative.Storage
open Pulumi.AzureNative.Authorization
open Pulumi.FSharp.AzureNative.Logic
open Pulumi.AzureNative.Resources
open Pulumi.AzureNative.KeyVault
open Pulumi.AzureNative.Storage
open Pulumi.AzureNative.Logic
open Pulumi.FSharp.Outputs
open Pulumi.FSharp.Random
open Pulumi.FSharp
open System.IO
open System
open Pulumi

let connectionDefinition = Pulumi.FSharp.AzureNative.Web.Inputs.apiConnectionDefinitionProperties
let apiReference = Pulumi.FSharp.AzureNative.Web.Inputs.apiReference
let sttSku = Pulumi.FSharp.AzureNative.CognitiveServices.Inputs.sku
let configSecret = Pulumi.FSharp.Config.secret
let config = Pulumi.FSharp.Config.config
let kvSku = Pulumi.FSharp.AzureNative.KeyVault.Inputs.sku

// Add those to the library
let ``Key Vault Secrets User`` = "4633458b-17de-408a-b874-0445c86b69e6"
let ``Key Vault Secrets Officer`` = "b86a8fe4-44ce-4948-aee5-eccb2c155cd7"

Deployment.run (fun () ->
    let azureConfig =
        GetClientConfig.InvokeAsync().Result
    
    let rg =
        resourceGroup {
            name $"rg-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
        }
    
    let speech =
        account {
            name          $"cog-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
            resourceGroup rg.Name
            kind          "SpeechServices"
            sttSku        { name "S0" }
        }
    
    let keyVault =
        vault {
            name          $"kvtstt{Deployment.Instance.StackName}{Region.shortName}001"
            resourceGroup rg.Name
            
            vaultProperties {
                enableRbacAuthorization true
                enableSoftDelete        false
                tenantId                azureConfig.TenantId
                
                kvSku {
                    SkuName.Standard
                    family SkuFamily.A
                }
            }
        }
    
    let botSecret =
        secret {
            name          $"botsecret-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
            vaultName     keyVault.Name
            resourceGroup rg.Name
            
            secretProperties {
                value configSecret["bot-key"]
            }
        }
    
    let speechKey =
        output {
            let! result =
                ListAccountKeys.Invoke(ListAccountKeysInvokeArgs(AccountName = speech.Name, ResourceGroupName = rg.Name))
            
            return result.Key1
        }
    
    let speechSecret =
        secret {
            name          $"speechsecret-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
            vaultName     keyVault.Name
            resourceGroup rg.Name
            
            secretProperties {
                value speechKey
            }
        }
    
    let oaiSecret =
        secret {
            name          $"oaisecret-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
            vaultName     keyVault.Name
            resourceGroup rg.Name
            
            secretProperties {
                value configSecret["openai-key"]
            }
        }
    
    let region =
        Config("azure-native").Get("location").Replace(" ", "").ToLowerInvariant()
    
    let storage =
        storageAccount {
            name                   $"satai{Deployment.Instance.StackName}{Region.shortName}001"
            resourceGroup          rg.Name
            kind                   Kind.StorageV2
            sku                    { name SkuName.Standard_LRS }
            allowSharedKeyAccess   true
            enableHttpsTrafficOnly true
            allowBlobPublicAccess  true
            minimumTlsVersion      MinimumTlsVersion.TLS1_2
            
            AccessTier.Hot
        }
    
    let container =
        blobContainer {
            name                        $"sac-tai-{Deployment.Instance.StackName}-{Region.shortName}-audio"
            resourceGroup               rg.Name
            accountName                 storage.Name
            containerName               "audio"
            
            PublicAccess.Container
        }
    
    let connectionName =
        $"kvc-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
    
    let saConnectionName =
        $"sac-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
    
    let kvConnectionTemplate =
        output {
            let! vaultName = keyVault.Name
            let! storageName = storage.Name
            
            return File.ReadAllText("Connection.json")
                       .Replace("$$SANAME$$", storageName)
                       .Replace("$$BLOBCONNECTIONNAME$$", saConnectionName)
                       .Replace("$$CONNECTIONNAME$$", connectionName)
                       .Replace("$$LOCATION$$", region)
                       .Replace("$$VAULTNAME$$", vaultName)
        } |> InputJson.op_Implicit
    
    let kvConnection =
        (*connection {
            name          $"kvc-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
            resourceGroup rg.Name
            
            connectionDefinition {
                displayName                "Key Vault connection"
                parameterValueType         "Alternative"                  // Not available on the API definition
                alternativeParameterValues [ "vaultName", keyVault.Name ] // Not available on the API definition
                
                apiReference {
                    name "keyvault"
                    id   $"/subscriptions/{azureConfig.SubscriptionId}/providers/Microsoft.Web/locations/{region}/managedApis/keyvault"
                }
            }
        }*)
        deployment {
            name                 $"kvc-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
            resourceGroup        rg.Name
            
            deploymentProperties { 
                template kvConnectionTemplate
                
                DeploymentMode.Incremental
            }
        }
    
    let workflowDefinition =
        output {
            let! botSecretName = botSecret.Name
            let! speechSecretName = speechSecret.Name
            let! oaiSecretName = oaiSecret.Name
            let! containerName = container.Name
            let! saName = storage.Name
            
            let usernames : string list = Config().GetObject("accept-chat-usernames")
            
            return File.ReadAllText("Workflow.json")
                       .Replace("$$SANAME$$", saName)
                       .Replace("$$BLOBCONNECTIONNAME$$", saConnectionName)
                       .Replace("$$CONNECTIONNAME$$", connectionName)            
                       .Replace("$$BOTSECRET$$", botSecretName)
                       .Replace("$$SPEECHSECRET$$", speechSecretName)
                       .Replace("$$OAISECRET$$", oaiSecretName)
                       .Replace("$$BLOBCONTAINER$$", containerName)
                       .Replace("$$USERNAMES$$", String.Join(", ", usernames |> List.map (fun u -> $"'{u}'")))
        } |> InputJson.op_Implicit

    let kvConnectionParameter =
        output {
            let! rgName = rg.Name
            let connectionId = $"/subscriptions/{azureConfig.SubscriptionId}/resourceGroups/{rgName}/providers/Microsoft.Web/connections/{connectionName}"
        
            return $"""{{
    "{connectionName}": {{
        "connectionId": "{connectionId}",
        "connectionName": "{connectionName}",
        "connectionProperties": {{
            "authentication": {{
                "type": "ManagedServiceIdentity"
            }}
        }},
        "id": "/subscriptions/{azureConfig.SubscriptionId}/providers/Microsoft.Web/locations/{region}/managedApis/keyvault"
    }}
}}""" |> InputJson.op_Implicit
        }
    
    let la =
        workflow {
            name                   $"logic-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
            resourceGroup          rg.Name
            definition             workflowDefinition
            dependsOn              kvConnection
            managedServiceIdentity { resourceType ManagedServiceIdentityType.SystemAssigned }
            
            parameters [
                "$connections", workflowParameter { value kvConnectionParameter }
            ]
        }
    
    let assignmentNameUuid =
        randomUuid { name $"rakvname-tai-{Deployment.Instance.StackName}-{Region.shortName}-001" }
    
    roleAssignment {
        name               $"rakv-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
        roleAssignmentName assignmentNameUuid.Result
        scope              keyVault.Id
        principalId        (la.Identity.Apply(fun i -> i.PrincipalId))
        roleDefinitionId   $"/subscriptions/{azureConfig.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{``Key Vault Secrets User``}"
    }
    
    roleAssignment {
        name               $"radp-tai-{Deployment.Instance.StackName}-{Region.shortName}-001"
        roleAssignmentName (randomUuid { name $"radpname-tai-{Deployment.Instance.StackName}-{Region.shortName}-001" }).Result
        scope              keyVault.Id
        principalId        (AzureAD.GetClientConfig.InvokeAsync().Result.ObjectId)
        roleDefinitionId   $"/subscriptions/{azureConfig.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{``Key Vault Secrets Officer``}"
    }
    
    let workflowTriggerUrl =
        secretOutput {
            let! laName = la.Name
            let! rgName = rg.Name
            
            let! result = ListWorkflowCallbackUrl.InvokeAsync(ListWorkflowCallbackUrlArgs(
                WorkflowName = laName,
                ResourceGroupName = rgName))
            // TriggerName?
            
            (*let! botKey = configSecret["bot-key"]
            
            let body =
                $"""{{"url":"{result.Value}", "allowed_updates":["message"]}}"""
            
            let! setWebhookResult =
                Http.AsyncRequest($"https://api.telegram.org/bot{botKey}/setWebhook",
                                  httpMethod = HttpMethod.Post,
                                  headers = [ HttpRequestHeaders.ContentType HttpContentTypes.Json ],
                                  body = HttpRequestBody.TextRequest body)
                |> Async.StartAsTask
            
            return
                match setWebhookResult.Body with
                | Text json -> JsonValue.Parse(json).Properties()
                               |> Array.find (fun (k, _) -> k = "ok")
                               |> snd
                               |> (fun x -> x.AsBoolean())
                | _         -> failwith "Bad response data"*)
            return result.Value
        }
       
    dict [ "WorkflowTriggerUrl", workflowTriggerUrl ]
)
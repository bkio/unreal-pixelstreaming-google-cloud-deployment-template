/// Copyright 2022- Burak Kara, All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using CloudConnectors;
using CloudServiceUtilities;
using WebServiceUtilities;
using ServicePixelStreamingOrchestrator.Endpoints;
using ServicePixelStreamingOrchestrator.Controllers;
using static WebServiceUtilities.WebPrefixStructure;
using CommonUtilities;
using Newtonsoft.Json.Linq;

namespace ServicePixelStreamingOrchestrator
{
    //Not designed to be stateless. It is stateful.
    class Program
    {
        static void Main()
        {
            Console.WriteLine("Initializing the service...");

            /*
            * Common initialization step
            */
            if (!CloudConnector.Initialize(out CloudConnector Connector,
                new string[][]
                {
                    new string[] { "GOOGLE_CLOUD_PROJECT_ID" },
                    new string[] { "PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME" },
                    new string[] { "GPU_INSTANCES_VM_NAME_PREFIX" },
                    new string[] { "VM_ZONES" },
                    new string[] { "GPU_INSTANCES_PER_ZONE" },
                    new string[] { "MAX_USER_SESSION_PER_INSTANCE" },
                    new string[] { "COMPUTE_ENGINE_PLAIN_PRIVATE_KEY_BASE64" },
                    new string[] { "CLOUD_API_SECRET_KEYS_BASE64" },
                    new string[] { "FILE_API_BUCKET_NAME" }
                }))
                return;

            var PixelStreaming_GPUInstancesNamePrefix = Connector.RequiredEnvironmentVariables["GPU_INSTANCES_VM_NAME_PREFIX"];

            var PixelStreaming_GPUInstancesZones = Connector.RequiredEnvironmentVariables["VM_ZONES"].Split(',');

            if (!int.TryParse(Connector.RequiredEnvironmentVariables["GPU_INSTANCES_PER_ZONE"], out int PixelStreaming_GPUInstancesPerZone))
            {
                Connector.LogService.WriteLogs(LogServiceMessageUtility.Single(ELogServiceLogType.Info, "GPU_INSTANCES_PER_ZONE must be an integer."), Connector.ProgramID, "WebService");
                return;
            }

            if (!int.TryParse(Connector.RequiredEnvironmentVariables["MAX_USER_SESSION_PER_INSTANCE"], out int MaxUserSessionPerInstance))
            {
                Connector.LogService.WriteLogs(LogServiceMessageUtility.Single(ELogServiceLogType.Info, "MAX_USER_SESSION_PER_INSTANCE must be an integer."), Connector.ProgramID, "WebService");
                return;
            }

            if (!Utility.Base64Decode(out string ComputeEngineSSHPrivateKey, Connector.RequiredEnvironmentVariables["COMPUTE_ENGINE_PLAIN_PRIVATE_KEY_BASE64"],
                (string _Message) =>
                {
                    Connector.LogService.WriteLogs(LogServiceMessageUtility.Single(ELogServiceLogType.Info, $"{_Message} - Base64 decode operation for compute engine private ssh key has failed: {Connector.RequiredEnvironmentVariables["COMPUTE_ENGINE_PLAIN_PRIVATE_KEY_BASE64"]}"), Connector.ProgramID, "WebService");
                })) return;

            if (!Controller_PixelStreaming.Get().Initialize(
                Connector.ProgramID,
                Connector.RequiredEnvironmentVariables["GOOGLE_CLOUD_PROJECT_ID"],
                Connector.RequiredEnvironmentVariables["PIXEL_STREAMING_UNREAL_CONTAINER_IMAGE_NAME"],
                ComputeEngineSSHPrivateKey,
                PixelStreaming_GPUInstancesNamePrefix, 
                PixelStreaming_GPUInstancesZones, 
                PixelStreaming_GPUInstancesPerZone, 
                MaxUserSessionPerInstance,

                (string Message) =>
                {
                    Connector.LogService.WriteLogs(LogServiceMessageUtility.Single(ELogServiceLogType.Info, Message), Connector.ProgramID, "WebService");
                })) return;

            if (!Controller_Security.Get().Initialize(
                (string Message) =>
                {
                    Connector.LogService.WriteLogs(LogServiceMessageUtility.Single(ELogServiceLogType.Info, Message), Connector.ProgramID, "WebService");
                })) return;

            if (!Utility.Base64Decode(out string CloudAPISecretsRaw, Connector.RequiredEnvironmentVariables["CLOUD_API_SECRET_KEYS_BASE64"],
                (string _Message) =>
                {
                    Connector.LogService.WriteLogs(LogServiceMessageUtility.Single(ELogServiceLogType.Info, $"{_Message} - Base64 decode operation for CLOUD_API_SECRET_KEYS_BASE64 has failed: {Connector.RequiredEnvironmentVariables["CLOUD_API_SECRET_KEYS_BASE64"]}"), Connector.ProgramID, "WebService");
                })) return;
            var CloudAPISecrets = new HashSet<string>();
            try
            {
                var TmpJArray = JArray.Parse(CloudAPISecretsRaw);

                foreach (var TmpJToken in TmpJArray)
                {
                    if (TmpJToken.Type == JTokenType.String)
                    {
                        var AsStr = (string)TmpJToken;
                        if (CloudAPISecrets.Contains(AsStr))
                        {
                            throw new Exception("Non-unique array elements");
                        }
                        CloudAPISecrets.Add(AsStr);
                    }
                    else
                    {
                        throw new Exception("Non-string array elements");
                    }
                }
            }
            catch (Exception)
            {
                Connector.LogService.WriteLogs(LogServiceMessageUtility.Single(ELogServiceLogType.Error, "CLOUD_API_SECRET_KEYS_BASE64 is misconfigured. It should contain strigified JSON array with unique string elements."), Connector.ProgramID, "WebService");
                return;
            }

            var FileAPIBucketName = Connector.RequiredEnvironmentVariables["FILE_API_BUCKET_NAME"];

            /*
            * Web-http service initialization
            */
            new WebService(new List<WebPrefixStructure>()
            {
                new WebPrefixStructure(new string[] { "*" }, () => new Handle_WebAndWebSocket_Request(Connector.FileService, CloudAPISecrets, FileAPIBucketName), new WebSocketListenParameters(false))
            }
            .ToArray(), Connector.ServerPort).Run((string Message) =>
            {
                Connector.LogService.WriteLogs(LogServiceMessageUtility.Single(ELogServiceLogType.Info, Message), Connector.ProgramID, "WebService");
            });

            /*
            * Make main thread sleep forever
            */
            Thread.Sleep(Timeout.Infinite);
        }
    }
}

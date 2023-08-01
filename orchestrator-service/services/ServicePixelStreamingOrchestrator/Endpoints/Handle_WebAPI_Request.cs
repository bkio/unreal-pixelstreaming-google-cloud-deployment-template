/// Copyright 2022- Burak Kara, All rights reserved.

using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using WebServiceUtilities;
using WebResponse = WebServiceUtilities.WebResponse;
using CloudServiceUtilities;

namespace ServicePixelStreamingOrchestrator.Endpoints
{
    internal class Handle_WebAPI_Request : WebServiceBase
    {
        private readonly IFileServiceInterface FileService;
        private readonly HashSet<string> CloudAPISecrets;
        private readonly string FileAPIBucketName;
        internal Handle_WebAPI_Request(IFileServiceInterface _FileService, HashSet<string> _CloudAPISecrets, string _FileAPIBucketName)
        {
            FileService = _FileService;
            CloudAPISecrets = _CloudAPISecrets;
            FileAPIBucketName = _FileAPIBucketName;
        }

        protected override WebServiceResponse OnRequest(HttpListenerContext _Context, Action<string> _ErrorMessageAction = null)
        {
            var Path = _Context.Request.Url.AbsolutePath.Substring("/api/".Length);

            if (_Context.Request.HttpMethod != "POST")
            {
                return WebResponse.MethodNotAllowed("Only POST requests are allowed.");
            }

            if (!WebUtilities.DoesContextContainHeader(out List<string> AuthorizationValues, out string _, _Context, "Authorization"))
            {
                return WebResponse.Unauthorized("Unauthorized request.");
            }
            bool bAuthorized = false;
            foreach (var AuthorizationValue in AuthorizationValues)
            {
                if (!AuthorizationValue.StartsWith("Basic "))
                {
                    return WebResponse.Unauthorized("Authorization token (secret) must start with 'Basic '");
                }
                if (CloudAPISecrets.Contains(AuthorizationValue.Substring("Basic ".Length)))
                {
                    bAuthorized = true;
                    break;
                }
            }
            if (!bAuthorized)
            {
                return WebResponse.Unauthorized("Incorrect credentials.");
            }

            JObject ParsedBody;
            try
            {
                using (var InputStream = _Context.Request.InputStream)
                {
                    using (var Reader = new StreamReader(InputStream))
                    {
                        ParsedBody = JObject.Parse(Reader.ReadToEnd());
                    }
                }

            }
            catch (JsonReaderException)
            {
                return WebResponse.BadRequest($"Invalid json body");
            }
            catch (ArgumentNullException)
            {
                return WebResponse.BadRequest($"Empty body.");
            }
            catch (Exception e)
            {
                return WebResponse.InternalError($"Error occured during request process: {e.Message}, Trace: {e.StackTrace}");
            }

            if (Path == "file")
            {
                return HandleFileAPIRequest(ParsedBody, _ErrorMessageAction);
            }
            return WebResponse.NotFound("Requested API resource does not exist.");
        }


        private WebServiceResponse HandleFileAPIRequest(JObject _Body, Action<string> _ErrorMessageAction = null)
        {
            if (!_Body.ContainsKey("operation") || _Body["operation"].Type != JTokenType.String)
                return WebResponse.BadRequest("Invalid or missing 'operation' parameter in the body.");

            var Operation = (string)_Body["operation"];
            if (Operation == "list")
            {
                if (!FileService.ListAllFilesInBucket(FileAPIBucketName, out List<string> FileKeys, _ErrorMessageAction))
                {
                    return WebResponse.InternalError("List files operation has failed.");
                }

                var Result = new JArray();

                if (FileKeys != null)
                {
                    foreach (var FileKey in FileKeys)
                    {
                        Result.Add(FileKey);
                    }
                }
                return WebResponse.StatusOK("List files operation has succeeded.", new JObject()
                {
                    ["files"] = Result
                });
            }
            else
            {
                if (!_Body.ContainsKey("file_key") || _Body["file_key"].Type != JTokenType.String)
                    return WebResponse.BadRequest("Invalid or missing 'file_key' parameter in the body.");
                var FileKey = (string)_Body["file_key"];

                if (Operation == "download")
                {
                    if (!FileService.CreateSignedURLForDownload(out string SignedUrl, FileAPIBucketName, FileKey, 1, _ErrorMessageAction))
                    {
                        return WebResponse.InternalError("Download link creation operation has failed.");
                    }
                    return WebResponse.StatusOK("Download file operation has succeeded.", new JObject()
                    {
                        ["file_key"] = FileKey,
                        ["download_url"] = SignedUrl
                    });
                }
                else if (Operation == "upload")
                {
                    if (!_Body.ContainsKey("content_type") || _Body["content_type"].Type != JTokenType.String)
                        return WebResponse.BadRequest("Invalid or missing 'content_type' parameter in the body.");

                    if (!FileService.CreateSignedURLForUpload(out string SignedUrl, FileAPIBucketName, FileKey, (string)_Body["content_type"], 1, _ErrorMessageAction))
                    {
                        return WebResponse.InternalError("Upload link creation operation has failed.");
                    }
                    return WebResponse.StatusOK("Upload file operation has succeeded.", new JObject()
                    {
                        ["file_key"] = FileKey,
                        ["upload_url"] = SignedUrl
                    });
                }
                else if (Operation == "delete")
                {
                    if (!FileService.DeleteFile(FileAPIBucketName, FileKey, _ErrorMessageAction))
                    {
                        return WebResponse.InternalError("Delete file operation has failed.");
                    }
                    return WebResponse.StatusOK("Delete file operation has succeeded.", new JObject()
                    {
                        ["file_key"] = FileKey
                    });
                }
            }
            return WebResponse.BadRequest("Parameter 'operation' in the body must be either 'list', 'download', 'delete' or 'upload'");
        }
    }
}
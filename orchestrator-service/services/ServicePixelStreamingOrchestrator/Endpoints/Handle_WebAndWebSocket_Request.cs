/// Copyright 2022- Burak Kara, All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CommonUtilities;
using CloudServiceUtilities;
using ServicePixelStreamingOrchestrator.Controllers;
using WebServiceUtilities;
using static ServicePixelStreamingOrchestrator.Controllers.Helper.PixelStreamingHelpers;
using WebResponse = WebServiceUtilities.WebResponse;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ServicePixelStreamingOrchestrator.Endpoints
{
    internal class Handle_WebAndWebSocket_Request : WebAndWebSocketServiceBase
    {
        private readonly WeakReference<WebAndWebSocketServiceBase> SelfWeakReference;
        internal Handle_WebAndWebSocket_Request(IFileServiceInterface _FileService, HashSet<string> _CloudAPISecrets, string _FileAPIBucketName)
        {
            SelfWeakReference = new WeakReference<WebAndWebSocketServiceBase>(this);

            FileService = _FileService;
            CloudAPISecrets = _CloudAPISecrets;
            FileAPIBucketName = _FileAPIBucketName;
        }

        protected override void OnWebSocketRequest(WebSocketContext _Context, Action<string> _ErrorMessageAction = null)
        {
            if (!Controller_Security.Get().VerifyCaptchaSession(out string _UserId_OnSuccess, _Context.CookieCollection, true/*_bFromWebSocket*/))
            {
                using (var CloseTask = _Context.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Unauthorized", CancellationToken.None))
                {
                    CloseTask.Wait();
                    return;
                }
            }

            var InformationQueue = new ConcurrentQueue<string>();

            var SocketClosedObserver_ForStartOperation = new Atomicable<bool>(false);
            var SocketClosedObserver_ForTunnelOperation = new Atomicable<bool>(false);

            WebSocketTunnel_StartClientHealthCheck_And_InfoStatusUpdate(
                _Context.WebSocket, 
                new Atomicable<bool>[] 
                {
                    SocketClosedObserver_ForStartOperation,
                    SocketClosedObserver_ForTunnelOperation,
                }, 
                InformationQueue);

            if (!Controller_PixelStreaming.Get().Start_AuthenticatedUserSession_Request(
                _UserId_OnSuccess,
                SelfWeakReference,
                new WeakReference<ConcurrentQueue<string>>(InformationQueue),
                new WeakReference<Atomicable<bool>>(SocketClosedObserver_ForStartOperation),
                out E_AuthenticatedUserSession_Request_Result _Result,
                out Controller_PixelStreaming.NetworkSession _NetworkSessionOnSuccess,
                _ErrorMessageAction))
            {
                if (_Result == E_AuthenticatedUserSession_Request_Result.NoAvailableSpot)
                {
                    using var CloseTask = _Context.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "No available spot", CancellationToken.None);
                    CloseTask.Wait();
                }
                else if (_Result == E_AuthenticatedUserSession_Request_Result.SessionAlreadyExistsForUser)
                {
                    using var CloseTask = _Context.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Another live session already exists for the user", CancellationToken.None);
                    CloseTask.Wait();
                }
                else
                {
                    using var CloseTask = _Context.WebSocket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "Internal error", CancellationToken.None);
                    CloseTask.Wait();
                }
                
                return;
            }

            InformationQueue.Enqueue("A container has been initialized for the session. Establishing connection with the container.");

            try
            {
                if (SocketClosedObserver_ForStartOperation.Get())
                {
                    return;
                }

                while (_Context.WebSocket.State == WebSocketState.Open)
                {
                    var LastErrorMessage = "";
                    var bSuccess = false;
                    var RetryCount = 0;

                    ClientWebSocket ClientWS;
                    do
                    {
                        ClientWS = new ClientWebSocket();

                        try
                        {
                            using (var ConnectTask = ClientWS.ConnectAsync(new Uri($"ws://{_NetworkSessionOnSuccess.PrivateIP}:{_NetworkSessionOnSuccess.HTTPPort}"), CancellationToken.None))
                            {
                                ConnectTask.Wait();
                            }

                            bSuccess = true;
                        }
                        catch (Exception e)
                        {
                            LastErrorMessage = e.Message;
                            ClientWS.Dispose();
                        }

                    } while (!bSuccess && ++RetryCount < 30 && ThreadSleep(1000));

                    if (SocketClosedObserver_ForTunnelOperation.Get())
                    {
                        return;
                    }

                    try
                    {
                        if (!bSuccess)
                        {
                            _ErrorMessageAction?.Invoke($"Handle_WebAndWebSocket_Request: Cannot establish internal socket connection: {LastErrorMessage}");

                            using (var CloseTask = _Context.WebSocket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "Internal error: Cannot establish internal socket connection", CancellationToken.None))
                            {
                                CloseTask.Wait();
                            }
                            return;
                        }

                        InformationQueue.Enqueue("Communication with the container has been established for the session.");

                        try
                        {
                            WebSocketTunnel(_Context.WebSocket, ClientWS, SocketClosedObserver_ForTunnelOperation, _ErrorMessageAction);
                        }
                        catch (Exception e)
                        {
                            _ErrorMessageAction?.Invoke($"Handle_WebAndWebSocket_Request: Exception occured in WebSocketTunnel: {e.Message} {e.StackTrace}");
                            return;
                        }
                    }
                    finally
                    {
                        try
                        {
                            using (var CloseTask = _Context.WebSocket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "Internal error: Internal socket communication error", CancellationToken.None))
                            {
                                CloseTask.Wait();
                            }
                        }
                        catch (Exception) { }

                        try
                        {
                            ClientWS.Dispose();
                        }
                        catch (Exception) { }
                    }
                }
            }
            finally
            {
                Controller_Security.Get().RemoveCaptchaSession(
                    _Context.CookieCollection);

                Controller_PixelStreaming.Get().Complete_AuthenticatedUserSession_Request(
                    _UserId_OnSuccess,
                    _NetworkSessionOnSuccess.PrivateIP,
                    SelfWeakReference,
                    _ErrorMessageAction);
            }
        }
        private static bool ThreadSleep(int _Milliseconds)
        {
            Thread.Sleep(_Milliseconds);
            return true;
        }

        private readonly IFileServiceInterface FileService;
        private readonly HashSet<string> CloudAPISecrets;
        private readonly string FileAPIBucketName;

        protected override WebServiceResponse OnRequest(HttpListenerContext _Context, Action<string> _ErrorMessageAction = null)
        {
            var RequestPath = _Context.Request.Url.AbsolutePath;

            if (RequestPath.StartsWith("/api/"))
            {
                return HandleAPIRequest(_Context, RequestPath.Substring("/api/".Length), _ErrorMessageAction);
            }

            string Filename = RequestPath;

            var HeadersToSend = new Dictionary<string, IEnumerable<string>>();

            string LocalPath;
            if (Filename == "" || Filename == "/")
            {
                string GeneratedSessionSecret = null;

                if (!Controller_Security.Get().VerifyCaptchaSession(out string _, _Context.Request.Cookies, false/*_bFromWebSocket*/)
                    && !Controller_Security.Get().VerifyCaptchaCode(_Context.Request.Cookies, out GeneratedSessionSecret))
                {
                    var CustomizedCaptchaPage = Controller_Security.Get().GenerateAndStoreCaptchaCode_ReturnCaptchaPage();
                    
                    return new WebServiceResponse(WebResponse.Status_OK_Code, new StringOrStream(CustomizedCaptchaPage), "text/html");
                }

                if (GeneratedSessionSecret != null)
                {
                    HeadersToSend.Add("Set-Cookie", new string[] { $"{Controller_Security.CAPTCHA_SESSION_SECRET_COOKIE_NAME}={GeneratedSessionSecret};Path=/;Expires={DateTime.UtcNow.AddMinutes(Controller_Security.CAPTCHA_SESSION_SECRET_COOKIE_EXPIRES_AFTER_MINUTES).ToString("ddd, dd-MMM-yyyy H:mm:ss")} GMT" });
                }

                LocalPath = "public/index.html";
            }
            else
            {
                LocalPath = "public" + Filename;
            }

            if (!MimeTypeMappings.TryGetValue(Path.GetExtension(LocalPath), out string ContentType))
            {
                ContentType = "application/octet-stream";
            }

            var AbsolutePath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), LocalPath);

            if (!File.Exists(AbsolutePath))
            {
                return WebResponse.NotFound("File not found.");
            }

            var ResultStream = new FileStream(AbsolutePath, FileMode.Open);
            var Wrapper = new StringOrStream(ResultStream, new FileInfo(AbsolutePath).Length,
                () =>
                {
                    //Always close the stream
                    try { ResultStream.Close(); } catch (Exception) { }
                    try { ResultStream.Dispose(); } catch (Exception) { }
                });

            return new WebServiceResponse(WebResponse.Status_OK_Code, HeadersToSend, Wrapper, ContentType);
        }

        private WebServiceResponse HandleAPIRequest(HttpListenerContext _Context, string _Path, Action<string> _ErrorMessageAction = null)
        {
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

            if (_Path == "file")
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
                    var MStream = new MemoryStream();
                    var StreamWrapper = new StringOrStream(MStream, 0);
                    if (!FileService.DownloadFile(FileAPIBucketName, FileKey, StreamWrapper, _ErrorMessageAction))
                    {
                        return WebResponse.InternalError("Download operation has failed.");
                    }
                    return new WebServiceResponse(200, new StringOrStream(MStream, MStream.Length, () =>
                    {
                        try
                        {
                            MStream?.Dispose();
                        }
                        catch (Exception) { }
                    }));
                }
                else if (Operation == "upload")
                {
                    if (!_Body.ContainsKey("content_base64") || _Body["content_base64"].Type != JTokenType.String)
                        return WebResponse.BadRequest("Invalid or missing 'content_type' parameter in the body.");

                    byte[] Content;
                    try
                    {
                        Content = Convert.FromBase64String((string)_Body["content_base64"]);
                    }
                    catch (Exception)
                    {
                        return WebResponse.BadRequest("Field 'content_base64' must be base64 encoded.");
                    }

                    using (var MStream = new MemoryStream())
                    {
                        var StreamWrapper = new StringOrStream(MStream, 0);
                        MStream.Write(Content);

                        if (!FileService.UploadFile(StreamWrapper, FileAPIBucketName, FileKey, ERemoteFileReadPublicity.AuthenticatedRead, null, _ErrorMessageAction))
                        {
                            return WebResponse.InternalError("Upload operation has failed.");
                        }
                    }
                    return WebResponse.StatusOK("Upload file operation has succeeded.", new JObject()
                    {
                        ["file_key"] = FileKey
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

        /// <summary>
        /// Mime Type conversion table
        /// </summary>
        private static readonly IDictionary<string, string> MimeTypeMappings =
            new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                {".asf", "video/x-ms-asf"},
                {".asx", "video/x-ms-asf"},
                {".avi", "video/x-msvideo"},
                {".bin", "application/octet-stream"},
                {".cco", "application/x-cocoa"},
                {".crt", "application/x-x509-ca-cert"},
                {".css", "text/css"},
                {".deb", "application/octet-stream"},
                {".der", "application/x-x509-ca-cert"},
                {".dll", "application/octet-stream"},
                {".dmg", "application/octet-stream"},
                {".ear", "application/java-archive"},
                {".eot", "application/octet-stream"},
                {".exe", "application/octet-stream"},
                {".flv", "video/x-flv"},
                {".gif", "image/gif"},
                {".hqx", "application/mac-binhex40"},
                {".htc", "text/x-component"},
                {".htm", "text/html"},
                {".html", "text/html"},
                {".ico", "image/x-icon"},
                {".img", "application/octet-stream"},
                {".iso", "application/octet-stream"},
                {".jar", "application/java-archive"},
                {".jardiff", "application/x-java-archive-diff"},
                {".jng", "image/x-jng"},
                {".jnlp", "application/x-java-jnlp-file"},
                {".jpeg", "image/jpeg"},
                {".jpg", "image/jpeg"},
                {".js", "application/x-javascript"},
                {".mml", "text/mathml"},
                {".mng", "video/x-mng"},
                {".mov", "video/quicktime"},
                {".mp3", "audio/mpeg"},
                {".mpeg", "video/mpeg"},
                {".mpg", "video/mpeg"},
                {".msi", "application/octet-stream"},
                {".msm", "application/octet-stream"},
                {".msp", "application/octet-stream"},
                {".pdb", "application/x-pilot"},
                {".pdf", "application/pdf"},
                {".pem", "application/x-x509-ca-cert"},
                {".pl", "application/x-perl"},
                {".pm", "application/x-perl"},
                {".png", "image/png"},
                {".prc", "application/x-pilot"},
                {".ra", "audio/x-realaudio"},
                {".rar", "application/x-rar-compressed"},
                {".rpm", "application/x-redhat-package-manager"},
                {".rss", "text/xml"},
                {".run", "application/x-makeself"},
                {".sea", "application/x-sea"},
                {".shtml", "text/html"},
                {".sit", "application/x-stuffit"},
                {".swf", "application/x-shockwave-flash"},
                {".tcl", "application/x-tcl"},
                {".tk", "application/x-tcl"},
                {".txt", "text/plain"},
                {".war", "application/java-archive"},
                {".wbmp", "image/vnd.wap.wbmp"},
                {".wmv", "video/x-ms-wmv"},
                {".xml", "text/xml"},
                {".xpi", "application/x-xpinstall"},
                {".zip", "application/zip"}
            };
    }
}
/// Copyright 2022- Burak Kara, All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Collections.Concurrent;
using CommonUtilities;
using ServicePixelStreamingOrchestrator.Controllers;
using WebServiceUtilities;
using static ServicePixelStreamingOrchestrator.Controllers.Helper.PixelStreamingHelpers;
using WebResponse = WebServiceUtilities.WebResponse;

namespace ServicePixelStreamingOrchestrator.Endpoints
{
    internal class Handle_WebAndWebSocket_Request : WebAndWebSocketServiceBase
    {
        private readonly WeakReference<WebAndWebSocketServiceBase> SelfWeakReference;
        internal Handle_WebAndWebSocket_Request()
        {
            SelfWeakReference = new WeakReference<WebAndWebSocketServiceBase>(this);
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

        protected override WebServiceResponse OnRequest(HttpListenerContext _Context, Action<string> _ErrorMessageAction = null)
        {
            string Filename = _Context.Request.Url.AbsolutePath;

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
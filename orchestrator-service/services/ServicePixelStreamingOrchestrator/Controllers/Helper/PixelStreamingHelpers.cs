/// Copyright 2022- Burak Kara, All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using CommonUtilities;
using CloudServiceUtilities;
using Renci.SshNet;

namespace ServicePixelStreamingOrchestrator.Controllers.Helper
{
    internal static class PixelStreamingHelpers
    {
        internal enum E_AuthenticatedUserSession_Request_Result
        {
            InternalError,
            NoAvailableSpot,
            SessionAlreadyExistsForUser,
            Cancelled,
            Success
        }

        internal enum EUserExistenceCondition
        {
            MustExist,
            MustNotExist
        }
        internal static bool PerformCancellableVMOperation(
            string _VMName,
            string _VMZone,
            string _CallerMethod,
            string _VMOperationName,
            EUserExistenceCondition _UserExistenceCondition,
            WeakReference<Atomicable<bool>> _SocketClosedToken,
            out E_AuthenticatedUserSession_Request_Result _FailedResult,
            Action<string> _ErrorMessageAction,
            Func<bool> _VMOperation)
        {
            var Result = new Atomicable<bool>(true);
            var Completed = new Atomicable<bool>(false);

            if (!_SocketClosedToken.TryGetTarget(out Atomicable<bool> SocketClosedToken))
            {
                SocketClosedToken = null;
            }

            TaskWrapper.Run(() =>
            {
                try
                {
                    if (!_VMOperation())
                    {
                        _ErrorMessageAction?.Invoke($"{_CallerMethod}-PerformCancellableVMOperation: {_VMOperationName} has failed for {_VMName} in the zone {_VMZone}.");
                        Result.Set(false);
                    }

                    Completed.Set(true);

                }
                catch (Exception) { }

            }, SocketClosedToken);

            do
            {
                Thread.Sleep(250);

                //If something has changed
                var DoesHaveUsers = Controller_PixelStreaming.Get().DoesVMHaveAssignedUsers(_VMName);

                if ((_UserExistenceCondition == EUserExistenceCondition.MustExist && !DoesHaveUsers)
                    || (_UserExistenceCondition == EUserExistenceCondition.MustNotExist && DoesHaveUsers))
                {
                    try
                    {
                        SocketClosedToken.Set(true);
                    }
                    catch (Exception) { }

                    _ErrorMessageAction?.Invoke($"{_CallerMethod}-PerformCancellableVMOperation: During the loop of {_VMOperationName} DB status has changed for {_VMName} in the zone {_VMZone}. Cancelling the operation.");

                    _FailedResult = E_AuthenticatedUserSession_Request_Result.Cancelled;
                    return false;
                }

            } while (!Completed.Get() && !SocketClosedToken.Get());

            if (!Result.Get())
            {
                _FailedResult = E_AuthenticatedUserSession_Request_Result.InternalError;
                return false;
            }

            _FailedResult = E_AuthenticatedUserSession_Request_Result.Success;
            return true;
        }

        internal abstract class PerformLatentVMOperationDescription
        {
            public string VMName;
            public IVMServiceInterface VMService;
        }
        internal class PerformLatentVMOperationDescription_StartInstance : PerformLatentVMOperationDescription
        {
        }
        internal class PerformLatentVMOperationDescription_StopInstance : PerformLatentVMOperationDescription
        {
        }

        internal static bool PerformLatentVMOperation(
            PerformLatentVMOperationDescription _Operation,
            Action<string> _ErrorMessageAction)
        {
            using var WaitFor = new ManualResetEvent(false);
            var Result = new Atomicable<bool>(false, EProducerStatus.MultipleProducer);

            if (_Operation is PerformLatentVMOperationDescription_StartInstance)
            {
                if (!_Operation.VMService.StartInstances(new string[] { _Operation.VMName },
                    () => { Result.Set(true); WaitFor.Set(); },
                    () => { Result.Set(false); WaitFor.Set(); },
                    _ErrorMessageAction)) return false;
            }
            else if (_Operation is PerformLatentVMOperationDescription_StopInstance)
            {
                if (!_Operation.VMService.StopInstances(new string[] { _Operation.VMName },
                    () => { Result.Set(true); WaitFor.Set(); },
                    () => { Result.Set(false); WaitFor.Set(); },
                    _ErrorMessageAction)) return false;
            }

            WaitFor.WaitOne();

            return Result.Get();
        }

        internal static bool ExecuteSSHCommands(string _PrivateIP, string[] _SequentialCommands, Action<string> _ErrorMessageAction)
        {
            var PrivateKeyAsBytes = Encoding.UTF8.GetBytes(Controller_PixelStreaming.Get().GetComputeEngineSSHPrivateKey());

            bool bRetry;
            var RetryCount = 0;
            string LastRetryError = "";

            do
            {
                bRetry = false;

                using var KeyStream = new MemoryStream(PrivateKeyAsBytes);
                using var PKF = new PrivateKeyFile(KeyStream);

                using (var SC = new SshClient(_PrivateIP, 22, "orchestrator", PKF))
                {
                    SC.HostKeyReceived += (sender, e) =>
                    {
                        e.CanTrust = true;
                    };
                    SC.ErrorOccurred += (sender, e) =>
                    {
                        _ErrorMessageAction?.Invoke($"ExecuteSSHCommands: ErrorOccured: {e.Exception.Message}");
                    };
                    SC.ConnectionInfo.Timeout = TimeSpan.FromMinutes(2);

                    try
                    {
                        SC.Connect();
                        try
                        {
                            var CommandsCombined = string.Join(';', _SequentialCommands);
                            using var CmdResult = SC.RunCommand(CommandsCombined);
                            if (CmdResult.ExitStatus != 0)
                            {
                                _ErrorMessageAction?.Invoke($"ExecuteSSHCommands: Command: {CommandsCombined} returned {CmdResult.ExitStatus} with result: {CmdResult.Result} and error: {CmdResult.Error}");
                                return false;
                            }
                        }
                        finally
                        {
                            try
                            {
                                SC.Disconnect();
                            }
                            catch (Exception) { }
                        }
                    }
                    catch (Exception e)
                    {
                        if (e.Message == "Connection refused")
                        {
                            bRetry = true;
                            Thread.Sleep(1000);
                            LastRetryError = "Connection refused";
                            continue;
                        }

                        _ErrorMessageAction?.Invoke($"ExecuteSSHCommands: Exception: {e.Message}");

                        return false;
                    }
                }

            } while (bRetry && ++RetryCount < 30);

            if (bRetry && RetryCount >= 30)
            {
                _ErrorMessageAction?.Invoke($"ExecuteSSHCommands: Retry exhaust: {LastRetryError}");
                return false;
            }

            return true;
        }

        internal static void WebSocketTunnel_StartClientHealthCheck_And_InfoStatusUpdate(
            WebSocket _Client,
            IEnumerable<Atomicable<bool>> _NetworkFailureObservers,
            ConcurrentQueue<string> _InformationMessagesQueue)
        {
            //Health check and informative status update
            TaskWrapper.Run(() =>
            {
                try
                {
                    while (_Client.State == WebSocketState.Open)
                    {
                        while (_InformationMessagesQueue.TryDequeue(out string _Message))
                        {
                            using (var SendTask_ToPS = _Client.SendAsync(
                                new ArraySegment<byte>(Encoding.UTF8.GetBytes($"--INFO--{_Message}")),
                                WebSocketMessageType.Text,
                                true/*EndOfMessage*/,
                                CancellationToken.None))
                            {
                                SendTask_ToPS.Wait();
                            }
                        }
                        using (var SendTask_ToPS = _Client.SendAsync(
                            new ArraySegment<byte>(Encoding.UTF8.GetBytes("--HEALTH_CHECK--")),
                            WebSocketMessageType.Text,
                            true/*EndOfMessage*/,
                            CancellationToken.None))
                        {
                            SendTask_ToPS.Wait();
                        }
                    }
                }
                catch (Exception)
                {
                    //Problem with the sendtask...
                    try
                    {
                        foreach (var Observer in _NetworkFailureObservers)
                        {
                            Observer?.Set(true);
                        }
                    }
                    catch (Exception) { }

                    return;
                }

                Thread.Sleep(5000);
            });
        }

        internal static void WebSocketTunnel(
            WebSocket _Client,
            WebSocket _DestVM,
            Atomicable<bool> _From_HC_CancelTokenSource,
            Action<string> _ErrorMessageAction)
        {
            using var CommunicationDone_1 = new ManualResetEvent(false);
            using var CommunicationDone_2 = new ManualResetEvent(false);

            //From Client to DestVM
            TaskWrapper.Run(() =>
            {
                try
                {
                    var Closed = new SocketCloseDescription() { bCloseReceived = false };

                    while (!Closed.bCloseReceived && _Client.State == WebSocketState.Open && _DestVM.State == WebSocketState.Open)
                    {
                        WebSocket_ReceiveFrom_SendTo(_Client, _DestVM, "Client->DestVM", out Closed, _ErrorMessageAction);
                    }
                }
                catch (Exception) {}
                finally
                {
                    CommunicationDone_1.Set();
                }
            }, _From_HC_CancelTokenSource);

            //From DestVM to Client
            TaskWrapper.Run(() =>
            {
                try
                {
                    var Closed = new SocketCloseDescription() { bCloseReceived = false };

                    while (!Closed.bCloseReceived && _Client.State == WebSocketState.Open && _DestVM.State == WebSocketState.Open)
                    {
                        WebSocket_ReceiveFrom_SendTo(_DestVM, _Client, "DestVM->Client", out Closed, _ErrorMessageAction);
                    }
                }
                catch (Exception) { }
                finally
                {
                    CommunicationDone_2.Set();
                }
            }, _From_HC_CancelTokenSource);

            CommunicationDone_1.WaitOne();
            CommunicationDone_2.WaitOne();
        }

        private static void WebSocket_ReceiveFrom_SendTo(
            WebSocket _From, 
            WebSocket _To,
            string _CommDirection,
            out SocketCloseDescription _Closed,
            Action<string> _ErrorMessageAction)
        {
            _Closed = new SocketCloseDescription()
            {
                bCloseReceived = false
            };

            using var MStream = new MemoryStream();

            var ReceiveBuffer = new ArraySegment<byte>(new byte[8192]);

            WebSocketReceiveResult ReceiveResult = null;
            do
            {
                using (var ReceiveTask_FromClient = _From.ReceiveAsync(ReceiveBuffer, CancellationToken.None))
                {
                    ReceiveTask_FromClient.Wait();
                    ReceiveResult = ReceiveTask_FromClient.Result;

                    MStream.Write(ReceiveBuffer.Array, ReceiveBuffer.Offset, ReceiveResult.Count);
                }
            }
            while (!ReceiveResult.EndOfMessage);
            
            MStream.Seek(0, SeekOrigin.Begin);
            
            if (ReceiveResult.MessageType == WebSocketMessageType.Text)
            {
                string AsString;
                using (var SReader = new StreamReader(MStream, Encoding.UTF8))
                {
                    AsString = SReader.ReadToEnd();
                }

                using (var SendTask_ToPS = _To.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(AsString)),
                    ReceiveResult.MessageType, 
                    true/*EndOfMessage*/, 
                    CancellationToken.None))
                {
                    SendTask_ToPS.Wait();
                }

                //_ErrorMessageAction?.Invoke($"WebSocket: {_CommDirection}: Text received and emitted. Content: {AsString}");
            }
            else if (ReceiveResult.MessageType == WebSocketMessageType.Close)
            {
                _Closed.bCloseReceived = true;
                _Closed.CloseStatus = ReceiveResult.CloseStatus.GetValueOrDefault(WebSocketCloseStatus.NormalClosure);
                _Closed.CloseReason = ReceiveResult.CloseStatusDescription;

                using (var CloseTask_ToPS = _To.CloseOutputAsync(
                    _Closed.CloseStatus,
                    _Closed.CloseReason,
                    CancellationToken.None))
                {
                    CloseTask_ToPS.Wait();
                }

                //_ErrorMessageAction?.Invoke($"WebSocket: {_CommDirection}: Close received and emitted. Reason: {_Closed.CloseStatus}: {_Closed.CloseReason}");
            }
            else //Binary not needed for Unreal PS - WS Communication
            {
                throw new NotImplementedException();
            }
        }
        private struct SocketCloseDescription
        {
            public bool bCloseReceived;
            public string CloseReason;
            public WebSocketCloseStatus CloseStatus;
        }
    }
}
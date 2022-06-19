/// Copyright 2022- Burak Kara, All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using CloudServiceUtilities.VMServices;
using CloudServiceUtilities;
using static ServicePixelStreamingOrchestrator.Controllers.Helper.PixelStreamingHelpers;
using WebServiceUtilities;
using CommonUtilities;

namespace ServicePixelStreamingOrchestrator.Controllers
{
    internal class Controller_PixelStreaming
    {
        private static Controller_PixelStreaming Instance = null;
        public static Controller_PixelStreaming Get()
        {
            if (Instance == null)
            {
                Instance = new Controller_PixelStreaming();
            }
            return Instance;
        }
        private Controller_PixelStreaming()
        {
        }
        ~Controller_PixelStreaming()
        {
            bRunning = false;
        }
        private bool bRunning = true;

        private string ProgramId;
        private string GoogleProjectId;
        private string GPUInstancesNamePrefix;
        private string[] GPUInstancesZones;
        private int GPUInstancePerZone;
        private int MaxUserSessionPerInstance;

        internal string GetComputeEngineSSHPrivateKey()
        {
            return ComputeEngineSSHPrivateKey;
        }
        private string ComputeEngineSSHPrivateKey;

        //In Initialize replace: [[GOOGLE_PROJECT_ID]] [[UNREAL_PIXEL_STREAMING_CONTAINER_IMAGE]]
        private static string DOCKER_PULL_COMMAND =
            "docker pull gcr.io/[[GOOGLE_PROJECT_ID]]/[[UNREAL_PIXEL_STREAMING_CONTAINER_IMAGE]]:latest";

        //Every request replace: [[USER_ID]] [[HTTP_PORT]] [[STREAMER_PORT]] [[TURN_PORT]]
        //In Initialize replace: [[GOOGLE_PROJECT_ID]] [[UNREAL_PIXEL_STREAMING_CONTAINER_IMAGE]]
        private static string DOCKER_START_COMMAND_TEMPLATE 
            = "docker run -d --rm --gpus all --network host --name [[USER_ID]] " +
            "-e HTTP_PORT=[[HTTP_PORT]] " +
            "-e STREAMER_PORT=[[STREAMER_PORT]] " +
            "-e TURN_PORT=[[TURN_PORT]] " +
            "gcr.io/[[GOOGLE_PROJECT_ID]]/[[UNREAL_PIXEL_STREAMING_CONTAINER_IMAGE]]";

        //Every request replace: [[USER_ID]]
        private static readonly string DOCKER_STOP_COMMAND_TEMPLATE
            = "docker stop [[USER_ID]]";

        private const string DOCKER_PRUNE_COMMAND
            = "docker system prune --volumes --force";

        public bool Initialize(
            string _ProgramId,
            string _GoogleProjectId,
            string _UnrealPixelStreamingContainerImageName,
            string _ComputeEngineSSHPrivateKey,
            string _GPUInstancesNamePrefix, 
            string[] _GPUInstancesZones, 
            int _GPUInstancePerZone,
            int _MaxUserSessionPerInstance,
            Action<string> _ErrorMessageAction)
        {
            ProgramId = _ProgramId;
            GoogleProjectId = _GoogleProjectId;
            GPUInstancesNamePrefix = _GPUInstancesNamePrefix;
            GPUInstancesZones = _GPUInstancesZones;
            GPUInstancePerZone = _GPUInstancePerZone;
            MaxUserSessionPerInstance = _MaxUserSessionPerInstance;
            ComputeEngineSSHPrivateKey = _ComputeEngineSSHPrivateKey;

            DOCKER_START_COMMAND_TEMPLATE = DOCKER_START_COMMAND_TEMPLATE
                .Replace("[[GOOGLE_PROJECT_ID]]", _GoogleProjectId)
                .Replace("[[UNREAL_PIXEL_STREAMING_CONTAINER_IMAGE]]", _UnrealPixelStreamingContainerImageName);

            DOCKER_PULL_COMMAND = DOCKER_PULL_COMMAND
                .Replace("[[GOOGLE_PROJECT_ID]]", _GoogleProjectId)
                .Replace("[[UNREAL_PIXEL_STREAMING_CONTAINER_IMAGE]]", _UnrealPixelStreamingContainerImageName);

            foreach (var Zone in GPUInstancesZones)
            {
                IVMServiceInterface VMService = new VMServiceGC(ProgramId, GoogleProjectId, Zone, _ErrorMessageAction);
                if (VMService == null || !VMService.HasInitializationSucceed())
                {
                    _ErrorMessageAction?.Invoke("VM service initialization has failed.");
                    return false;
                }

                int InstancesNoInTheZone = 0;

                var InstancesInTheZone = VMService.ListInstances(_ErrorMessageAction);
                foreach (var Instance in InstancesInTheZone)
                {
                    var InstanceName = Instance.Key;
                    if (!InstanceName.StartsWith(GPUInstancesNamePrefix)) continue;

                    string NetworkIP = null;
                    foreach (JObject NetworkInterface in (JArray)(((JObject)Instance.Value)["NetworkInterfaces"]))
                    {
                        NetworkIP = (string)NetworkInterface["NetworkIP"];
                        break;
                    }

                    var Status = VMService.GetStatusFromString((string)Instance.Value["Status"]);
                    if (Status == EBVMInstanceStatus.Running || Status == EBVMInstanceStatus.PreparingToRun)
                    {
                        if (Status == EBVMInstanceStatus.PreparingToRun 
                            && !VMService.WaitUntilInstanceStatus(InstanceName, new EBVMInstanceStatus[] { EBVMInstanceStatus.Running, EBVMInstanceStatus.Stopped, EBVMInstanceStatus.Stopping }, _ErrorMessageAction))
                        {
                            _ErrorMessageAction?.Invoke($"Error: Wait until instance status has failed for {InstanceName} in the zone {Zone}.");
                            return false;
                        }

                        if (!VMService.StopInstances(new string[] { InstanceName }, () => { }, () => { }, _ErrorMessageAction))
                        {
                            _ErrorMessageAction?.Invoke($"Error: Stop instance has failed for {InstanceName} in the zone {Zone}.");
                            return false;
                        }
                    }

                    Database.Add(InstanceName, new VM_Database_Entry()
                    {
                        InstanceName = Instance.Key,
                        PrivateIP = NetworkIP,
                        RelevantService = VMService,
                        Zone = Zone
                    });

                    //No need to lock here
                    ActiveDBToVMProcesses_NotThreadSafe.Add(InstanceName, new Tuple<Mutex, int>(new Mutex(false), 0));

                    InstancesNoInTheZone++;
                }

                if (InstancesNoInTheZone != GPUInstancePerZone)
                {
                    _ErrorMessageAction?.Invoke($"Error: Number of instances with prefix {GPUInstancesNamePrefix} in the zone {Zone} is {InstancesNoInTheZone}; not equal to expected {GPUInstancePerZone}");
                    return false;
                }
            }

            TickerThread = new Thread(TickerThreadRunnable);
            TickerThread_ErrorMessageAction = _ErrorMessageAction;
            TickerThread.Start();

            return true;
        }

        private void Lock_ActiveDBToVMProcesses(string _InstanceName)
        {
            try
            {
                ActiveDBToVMProcesses_NotThreadSafe[_InstanceName].Item1.WaitOne();
            }
            catch (Exception) { }
        }
        private void Unlock_ActiveDBToVMProcesses(string _InstanceName)
        {
            try
            {
                ActiveDBToVMProcesses_NotThreadSafe[_InstanceName].Item1.ReleaseMutex();
            }
            catch (Exception) { }
        }
        private void Increment_ActiveDBToVMProcesses_ThreadSafe(string _InstanceName)
        {
            Lock_ActiveDBToVMProcesses(_InstanceName);
            ActiveDBToVMProcesses_NotThreadSafe[_InstanceName] = new Tuple<Mutex, int>(ActiveDBToVMProcesses_NotThreadSafe[_InstanceName].Item1, ActiveDBToVMProcesses_NotThreadSafe[_InstanceName].Item2 + 1);
            Unlock_ActiveDBToVMProcesses(_InstanceName);
        }
        private void Decrement_ActiveDBToVMProcesses_ThreadSafe(string _InstanceName)
        {
            Lock_ActiveDBToVMProcesses(_InstanceName);
            ActiveDBToVMProcesses_NotThreadSafe[_InstanceName] = new Tuple<Mutex, int>(ActiveDBToVMProcesses_NotThreadSafe[_InstanceName].Item1, ActiveDBToVMProcesses_NotThreadSafe[_InstanceName].Item2 - 1);
            Unlock_ActiveDBToVMProcesses(_InstanceName);
        }
        private readonly Dictionary<string, Tuple<Mutex, int>> ActiveDBToVMProcesses_NotThreadSafe = new Dictionary<string, Tuple<Mutex, int>>();

        public class NetworkSession
        {
            public string PrivateIP;
            public int HTTPPort;
            public int StreamerPort;
            public int TURNPort;
        }

        private void SendInformativeMessageToClient(
            WeakReference<ConcurrentQueue<string>> _InformationQueue, 
            string _Message)
        {
            if (_InformationQueue.TryGetTarget(out ConcurrentQueue<string> _Queue))
            {
                _Queue.Enqueue(_Message);
            }
        }

        public bool Start_AuthenticatedUserSession_Request(
            string _UserId,
            WeakReference<WebAndWebSocketServiceBase> _OwnerRequestHandler,
            WeakReference<ConcurrentQueue<string>> _InformationQueue,
            WeakReference<Atomicable<bool>> _SocketClosedToken,
            out E_AuthenticatedUserSession_Request_Result _Result, 
            out NetworkSession _NetworkSessionOnSuccess,
            Action<string> _ErrorMessageAction = null)
        {
            _NetworkSessionOnSuccess = null;

            bool bAvailableInstanceFound = false;
            string ChosenInstanceName = null;
            string ChosenInstanceZone = null;
            IVMServiceInterface ChosenInstanceVMService = null;

            lock (Database)
            {
                foreach (var DatabaseEntry in Database.Values)
                {
                    if (DatabaseEntry.GetNumberOfUsers() < MaxUserSessionPerInstance)
                    {
                        bAvailableInstanceFound = true;

                        DatabaseEntry.AddUser(_OwnerRequestHandler);

                        _NetworkSessionOnSuccess = new NetworkSession()
                        {
                            PrivateIP = DatabaseEntry.PrivateIP
                        };

                        ChosenInstanceName = DatabaseEntry.InstanceName;
                        ChosenInstanceZone = DatabaseEntry.Zone;
                        ChosenInstanceVMService = DatabaseEntry.RelevantService;
                        
                        break;
                    }
                }
            }

            if (!bAvailableInstanceFound)
            {
                _Result = E_AuthenticatedUserSession_Request_Result.NoAvailableSpot;
                return false;
            }

            SendInformativeMessageToClient(_InformationQueue, "Found an available server for the session.");

            var PrivateIP = _NetworkSessionOnSuccess.PrivateIP;

            Increment_ActiveDBToVMProcesses_ThreadSafe(ChosenInstanceName);
            try
            {
                if (!ChosenInstanceVMService.GetInstanceStatus(ChosenInstanceName, out EBVMInstanceStatus InstanceStatus, _ErrorMessageAction))
                {
                    _ErrorMessageAction?.Invoke($"Start_AuthenticatedUserSession_Request: GetInstanceStatus has failed for {ChosenInstanceName} in the zone {ChosenInstanceZone}.");

                    _Result = E_AuthenticatedUserSession_Request_Result.InternalError;
                    return false;
                }

                if (InstanceStatus == EBVMInstanceStatus.Stopped || InstanceStatus == EBVMInstanceStatus.Stopping)
                {
                    SendInformativeMessageToClient(_InformationQueue, "A server is being rebooted up for the session.");

                    if (InstanceStatus == EBVMInstanceStatus.Stopping)
                    {
                        if (!PerformCancellableVMOperation(
                            ChosenInstanceName,
                            ChosenInstanceZone,
                            "Start_AuthenticatedUserSession_Request",
                            "WaitUntilInstanceStatus",
                            EUserExistenceCondition.MustExist,
                            _SocketClosedToken,
                            out _Result,
                            _ErrorMessageAction,
                            () =>
                            {
                                return ChosenInstanceVMService.WaitUntilInstanceStatus(ChosenInstanceName, new EBVMInstanceStatus[] { EBVMInstanceStatus.Stopped }, _ErrorMessageAction);
                            }))
                        {
                            return false;
                        }
                    }

                    SendInformativeMessageToClient(_InformationQueue, "A server is being booted up for the session.");

                    if (!PerformCancellableVMOperation(
                        ChosenInstanceName,
                        ChosenInstanceZone,
                        "Start_AuthenticatedUserSession_Request",
                        "StartInstances",
                        EUserExistenceCondition.MustExist,
                        _SocketClosedToken,
                        out _Result,
                        _ErrorMessageAction,
                        () =>
                        {
                            return PerformLatentVMOperation(
                                new PerformLatentVMOperationDescription_StartInstance()
                                {
                                    VMName = ChosenInstanceName,
                                    VMService = ChosenInstanceVMService
                                },
                                _ErrorMessageAction);
                        }))
                    {
                        return false;
                    }

                    if (!PerformCancellableVMOperation(
                        ChosenInstanceName,
                        ChosenInstanceZone,
                        "Start_AuthenticatedUserSession_Request",
                        "WaitUntilInstanceStatus",
                        EUserExistenceCondition.MustExist,
                        _SocketClosedToken,
                        out _Result,
                        _ErrorMessageAction,
                        () =>
                        {
                            return ChosenInstanceVMService.WaitUntilInstanceStatus(ChosenInstanceName, new EBVMInstanceStatus[] { EBVMInstanceStatus.Running }, _ErrorMessageAction);
                        }))
                    {
                        return false;
                    }

                    SendInformativeMessageToClient(_InformationQueue, "Getting the latest visual container image.");

                    if (!PerformCancellableVMOperation(
                        ChosenInstanceName,
                        ChosenInstanceZone,
                        "Start_AuthenticatedUserSession_Request",
                        "RunCommands-DockerPullCommand",
                        EUserExistenceCondition.MustExist,
                        _SocketClosedToken,
                        out _Result,
                        _ErrorMessageAction,
                        () =>
                        {
                            return ExecuteSSHCommands(
                                    PrivateIP,
                                    new string[] {
                                        DOCKER_PULL_COMMAND
                                    },
                                    _ErrorMessageAction);
                        }))
                    {
                        return false;
                    }
                }

                var HTTPPort = FindUnusedPort(EFindUnusedPortFor.HTTP);
                var StreamerPort = FindUnusedPort(EFindUnusedPortFor.Streamer);
                var TURNPort = FindUnusedPort(EFindUnusedPortFor.TURN);

                _NetworkSessionOnSuccess.HTTPPort = HTTPPort;
                _NetworkSessionOnSuccess.StreamerPort = StreamerPort;
                _NetworkSessionOnSuccess.TURNPort = TURNPort;

                SendInformativeMessageToClient(_InformationQueue, "Starting a session.");

                if (!PerformCancellableVMOperation(
                    ChosenInstanceName,
                    ChosenInstanceZone,
                    "Start_AuthenticatedUserSession_Request",
                    "RunCommands-DockerRunCommand",
                    EUserExistenceCondition.MustExist,
                    _SocketClosedToken,
                    out _Result,
                    _ErrorMessageAction,
                    () =>
                    {
                        return ExecuteSSHCommands(
                                PrivateIP,
                                new string[] {
                                    DOCKER_START_COMMAND_TEMPLATE
                                        .Replace("[[USER_ID]]", _UserId)
                                        .Replace("[[HTTP_PORT]]", $"{HTTPPort}")
                                        .Replace("[[STREAMER_PORT]]", $"{StreamerPort}")
                                        .Replace("[[TURN_PORT]]", $"{TURNPort}")
                                },
                                _ErrorMessageAction); //We don't wanna have too many outputs due to expected failure for double session detection.
                    }))
                {
                    //Double session!
                    _Result = E_AuthenticatedUserSession_Request_Result.SessionAlreadyExistsForUser;
                    return false;
                }

                _Result = E_AuthenticatedUserSession_Request_Result.Success;
                return true;
            }
            finally
            {
                Decrement_ActiveDBToVMProcesses_ThreadSafe(ChosenInstanceName);
            }
        }
        public bool Complete_AuthenticatedUserSession_Request(
            string _UserId,
            string _PrivateIP,
            WeakReference<WebAndWebSocketServiceBase> _OwnerRequestHandler,
            Action<string> _ErrorMessageAction = null)
        {
            bool bInstanceFound = false;

            string InstanceName = null;
            string InstanceZone = null;
            IVMServiceInterface InstanceVMService = null;

            lock (Database)
            {
                foreach (var DatabaseEntry in Database.Values)
                {
                    if (_PrivateIP == DatabaseEntry.PrivateIP)
                    {
                        bInstanceFound = true;

                        DatabaseEntry.RemoveUser(_OwnerRequestHandler);

                        InstanceName = DatabaseEntry.InstanceName;
                        InstanceZone = DatabaseEntry.Zone;
                        InstanceVMService = DatabaseEntry.RelevantService;

                        break;
                    }
                }
            }

            if (!bInstanceFound)
            {
                _ErrorMessageAction?.Invoke($"Complete_AuthenticatedUserSession_Request: Instance not found with private ip: {_PrivateIP}");
                return false;
            }

            Increment_ActiveDBToVMProcesses_ThreadSafe(InstanceName);
            try
            {
                return ExecuteSSHCommands(
                        _PrivateIP,
                        new string[] {
                            DOCKER_STOP_COMMAND_TEMPLATE
                                .Replace("[[USER_ID]]", _UserId)
                        },
                        _ErrorMessageAction);
            }
            finally
            {
                Decrement_ActiveDBToVMProcesses_ThreadSafe(InstanceName);
            }
        }

        private readonly Random PortRandomizer = new Random();
        private enum EFindUnusedPortFor
        {
            HTTP,
            Streamer,
            TURN
        }
        private int FindUnusedPort(EFindUnusedPortFor _For)
        {
            int FoundPort = -1;
            if (_For == EFindUnusedPortFor.HTTP)
            {
                lock (HTTP_PortsUsedBefore_NotThreadSafe)
                {
                    do
                    {
                        FoundPort = PortRandomizer.Next(8000, 8999);
                    } while (HTTP_PortsUsedBefore_NotThreadSafe.ContainsKey(FoundPort));
                    HTTP_PortsUsedBefore_NotThreadSafe.Add(FoundPort, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds());
                }
            }
            else if (_For == EFindUnusedPortFor.Streamer)
            {
                lock (Streamer_PortsUsedBefore_NotThreadSafe)
                {
                    do
                    {
                        FoundPort = PortRandomizer.Next(9000, 9999);
                    } while (Streamer_PortsUsedBefore_NotThreadSafe.ContainsKey(FoundPort));
                    Streamer_PortsUsedBefore_NotThreadSafe.Add(FoundPort, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds());
                }
            }
            else
            {
                lock (TURN_PortsUsedBefore_NotThreadSafe)
                {
                    do
                    {
                        FoundPort = PortRandomizer.Next(10000, 10999);
                    } while (TURN_PortsUsedBefore_NotThreadSafe.ContainsKey(FoundPort));
                    TURN_PortsUsedBefore_NotThreadSafe.Add(FoundPort, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds());
                }
            }
            return FoundPort;
        }
        private void CleanExpiredPorts(Dictionary<int, long> _Dictionary)
        {
            lock (_Dictionary)
            {
                var PortsToBeRemoved = new List<int>();

                foreach (var CurrentPair in _Dictionary)
                {
                    if (DateTimeOffset.FromUnixTimeSeconds(CurrentPair.Value).AddMinutes(Controller_Security.CAPTCHA_SESSION_SECRET_COOKIE_EXPIRES_AFTER_MINUTES) <= new DateTimeOffset(DateTime.UtcNow))
                    {
                        PortsToBeRemoved.Add(CurrentPair.Key);
                    }
                }

                foreach (var PortToRemove in PortsToBeRemoved)
                {
                    _Dictionary.Remove(PortToRemove);
                }
            }
        }
        private readonly Dictionary<int, long> HTTP_PortsUsedBefore_NotThreadSafe = new Dictionary<int, long>();
        private readonly Dictionary<int, long> Streamer_PortsUsedBefore_NotThreadSafe = new Dictionary<int, long>();
        private readonly Dictionary<int, long> TURN_PortsUsedBefore_NotThreadSafe = new Dictionary<int, long>();

        //Cleanup thread
        private void TickerThreadRunnable()
        {
            Thread.CurrentThread.IsBackground = true;

            while (bRunning)
            {
                Thread.Sleep(10000); //Every 10 secs

                CleanExpiredPorts(HTTP_PortsUsedBefore_NotThreadSafe);
                CleanExpiredPorts(Streamer_PortsUsedBefore_NotThreadSafe);
                CleanExpiredPorts(TURN_PortsUsedBefore_NotThreadSafe);

                var MustBeStoppedInstances = new List<VM_Database_Entry>();

                lock (Database)
                {
                    foreach (var DatabaseEntry in Database.Values)
                    {
                        var NumberOfUsers = DatabaseEntry.GetNumberOfUsers();
                        if (NumberOfUsers <= 0 
                            || NumberOfUsers > MaxUserSessionPerInstance/*Error, this should not happen*/)
                        {
                            DatabaseEntry.ResetActiveUsers();
                            MustBeStoppedInstances.Add(DatabaseEntry);
                        }
                    }
                }

                foreach (var MustBeStoppedInstance in MustBeStoppedInstances)
                {
                    bool bActive;
                    do
                    {
                        Thread.Sleep(1000);

                        Lock_ActiveDBToVMProcesses(MustBeStoppedInstance.InstanceName);
                        bActive = ActiveDBToVMProcesses_NotThreadSafe[MustBeStoppedInstance.InstanceName].Item2 > 0;
                        if (bActive)
                        {
                            Unlock_ActiveDBToVMProcesses(MustBeStoppedInstance.InstanceName);
                        }
                    }
                    while (bActive);

                    try
                    {
                        //Check if still has no users.
                        if (!DoesVMHaveAssignedUsers(MustBeStoppedInstance.InstanceName))
                        {
                            if (MustBeStoppedInstance.RelevantService.GetInstanceStatus(MustBeStoppedInstance.InstanceName, out EBVMInstanceStatus _Status, TickerThread_ErrorMessageAction))
                            {
                                if (_Status == EBVMInstanceStatus.PreparingToRun || _Status == EBVMInstanceStatus.Running)
                                {
                                    if (_Status == EBVMInstanceStatus.PreparingToRun)
                                    {
                                        if (!MustBeStoppedInstance.RelevantService.WaitUntilInstanceStatus(
                                            MustBeStoppedInstance.InstanceName,
                                            new EBVMInstanceStatus[]
                                            {
                                                EBVMInstanceStatus.Running,
                                                EBVMInstanceStatus.Stopped,
                                                EBVMInstanceStatus.Stopping
                                            },
                                            TickerThread_ErrorMessageAction))
                                        {
                                            continue;
                                        }
                                    }

                                    try
                                    {
                                        ExecuteSSHCommands(
                                            MustBeStoppedInstance.PrivateIP,
                                            new string[] {
                                                DOCKER_PRUNE_COMMAND //Cleanup
                                            },
                                            TickerThread_ErrorMessageAction);
                                    }
                                    catch (Exception) { }

                                    PerformLatentVMOperation(
                                        new PerformLatentVMOperationDescription_StopInstance()
                                        {
                                            VMName = MustBeStoppedInstance.InstanceName,
                                            VMService = MustBeStoppedInstance.RelevantService
                                        },
                                        TickerThread_ErrorMessageAction);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Unlock_ActiveDBToVMProcesses(MustBeStoppedInstance.InstanceName);
                    }
                }
            }
        }
        private Thread TickerThread;
        public Action<string> TickerThread_ErrorMessageAction = null;

        private class VM_Database_Entry
        {
            public string InstanceName;
            public string Zone;
            public string PrivateIP;
            public IVMServiceInterface RelevantService;

            private readonly List<WeakReference<WebAndWebSocketServiceBase>> ActiveUsers = new List<WeakReference<WebAndWebSocketServiceBase>>();
            public void AddUser(WeakReference<WebAndWebSocketServiceBase> _OwnerRequestHandler)
            {
                ActiveUsersCleanup();
                ActiveUsers.Add(_OwnerRequestHandler);
            }
            public void RemoveUser(WeakReference<WebAndWebSocketServiceBase> _OwnerRequestHandler)
            {
                ActiveUsersCleanup();
                ActiveUsers.Remove(_OwnerRequestHandler);
            }
            public int GetNumberOfUsers()
            {
                ActiveUsersCleanup();
                return ActiveUsers.Count;
            }
            private void ActiveUsersCleanup()
            {
                for (var i = ActiveUsers.Count - 1; i >= 0; i--)
                {
                    var CurrentWeak = ActiveUsers[i];

                    if (!CurrentWeak.TryGetTarget(out WebAndWebSocketServiceBase _Out) || _Out == null || !_Out.IsWebSocketOpen())
                    {
                        ActiveUsers.RemoveAt(i);
                    }
                }
            }
            public void ResetActiveUsers()
            {
                ActiveUsers.Clear();
            }
        }
        private readonly Dictionary<string, VM_Database_Entry> Database = new Dictionary<string, VM_Database_Entry>();
        internal bool DoesVMHaveAssignedUsers(string _InstanceName)
        {
            lock (Database)
            {
                return Database[_InstanceName].GetNumberOfUsers() > 0;
            }
        }
    }
}

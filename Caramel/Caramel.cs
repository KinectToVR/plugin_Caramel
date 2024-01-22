using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Net;
using System.Numerics;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Amethyst.Plugins.Contract;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Enum = System.Enum;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using Windows.Services.Maps;

// To learn more about Amethyst, plugins and the plugin API,
// see https://docs.k2vr.tech/ or https://github.com/KinectToVR

namespace Caramel
{
    // This is the main class of your plugin
    // Metadata includes the 'Name' and 'Guid'
    // You can also add a 'Publisher' and a 'Website'
    [Export(typeof(ITrackingDevice))]
    [ExportMetadata("Name", "Caramel")]
    [ExportMetadata("Guid", "HNOAKAYA-AME2-APII-DVCE-CARAMLAMTHST")]
    [ExportMetadata("Publisher", "公彦赤屋先, MochiDoesVR")]
    [ExportMetadata("Version", "1.0.0.0")]
    [ExportMetadata("Website", "https://github.com/KimihikoAkayasaki/plugin_Caramel")]
    public class Caramel : ITrackingDevice
    {
        public enum HandlerStatus
        {
            ServiceNotStarted = -1, // Not initialized
            ServiceSuccess = 0, // Success, everything's fine!
            ConnectionDead = 10, // No connection
            ErrorGRPC = 11, // No data received
            ErrorInitFailed = 12, // Init failed
            ErrorPortsTaken = 13 // Ports taken
        }

        private RpcException gRpcException;

        private static Dictionary<IJointType, TrackedJointType> JointTypeDictionary { get; } = new()
        {
            { IJointType.JointHead, TrackedJointType.JointHead },
            { IJointType.JointNeck, TrackedJointType.JointNeck },
            { IJointType.JointSpineShoulder, TrackedJointType.JointSpineShoulder },
            { IJointType.JointShoulderLeft, TrackedJointType.JointShoulderLeft },
            { IJointType.JointElbowLeft, TrackedJointType.JointElbowLeft },
            { IJointType.JointWristLeft, TrackedJointType.JointWristLeft },
            { IJointType.JointHandLeft, TrackedJointType.JointHandLeft },
            { IJointType.JointHandTipLeft, TrackedJointType.JointHandTipLeft },
            { IJointType.JointThumbLeft, TrackedJointType.JointThumbLeft },
            { IJointType.JointShoulderRight, TrackedJointType.JointShoulderRight },
            { IJointType.JointElbowRight, TrackedJointType.JointElbowRight },
            { IJointType.JointWristRight, TrackedJointType.JointWristRight },
            { IJointType.JointHandRight, TrackedJointType.JointHandRight },
            { IJointType.JointHandTipRight, TrackedJointType.JointHandTipRight },
            { IJointType.JointThumbRight, TrackedJointType.JointThumbRight },
            { IJointType.JointSpineMiddle, TrackedJointType.JointSpineMiddle },
            { IJointType.JointSpineWaist, TrackedJointType.JointSpineWaist },
            { IJointType.JointHipLeft, TrackedJointType.JointHipLeft },
            { IJointType.JointKneeLeft, TrackedJointType.JointKneeLeft },
            { IJointType.JointFootLeft, TrackedJointType.JointFootLeft },
            { IJointType.JointFootTipLeft, TrackedJointType.JointFootTipLeft },
            { IJointType.JointHipRight, TrackedJointType.JointHipRight },
            { IJointType.JointKneeRight, TrackedJointType.JointKneeRight },
            { IJointType.JointFootRight, TrackedJointType.JointFootRight },
            { IJointType.JointFootTipRight, TrackedJointType.JointFootTipRight }
        };

        [Import(typeof(IAmethystHost))] public IAmethystHost Host { get; set; }

        // This is the root of your settings UI
        private Page InterfaceRoot { get; set; }

        private List<IPAddress> Addresses { get; set; }
        private Server GrpServer { get; set; }
        private bool PluginLoaded { get; set; }

        public bool IsPositionFilterBlockingEnabled => false;
        public bool IsPhysicsOverrideEnabled => false;
        public bool IsSelfUpdateEnabled => true;
        public bool IsFlipSupported => false;
        public bool IsAppOrientationSupported => true;
        public bool IsSettingsDaemonSupported => true;

        // Settings UI root / MUST BE OF TYPE Microsoft.UI.Xaml.Controls.Page
        // Return new() of your implemented Page, and that's basically it!
        public object SettingsInterfaceRoot => InterfaceRoot;

        // Is the device connected/started?
        public bool IsInitialized { get; set; }

        // This should be updated on every frame,
        // along with joint devices
        // -> will lead to global tracking loss notification
        // if set to false at runtime some-when
        public bool IsSkeletonTracked { get; set; }

        // These will indicate the device's status [OK is (int)0]
        // Both should be updated either on call or as frequent as possible
        public int DeviceStatus { get; set; } = -1;

        // The link to launch when 'View Docs' is clicked while
        // this device is reporting a status error.
        // Supports custom protocols, e.g. "host://link"
        // [Note: launched via Launcher.LaunchUriAsync]
        public Uri ErrorDocsUri => null;

        // Device status string: to get your resources, use RequestLocalizedString
        public string DeviceStatusString => PluginLoaded
            ? DeviceStatus switch
            {
                (int)HandlerStatus.ServiceNotStarted => Host.RequestLocalizedString("/Statuses/NotStarted"),
                (int)HandlerStatus.ServiceSuccess => Host.RequestLocalizedString("/Statuses/Success"),
                (int)HandlerStatus.ConnectionDead => Host.RequestLocalizedString("/Statuses/ConnectionDead"),
                (int)HandlerStatus.ErrorInitFailed => Host.RequestLocalizedString("/Statuses/InitFailure"),
                (int)HandlerStatus.ErrorPortsTaken => Host.RequestLocalizedString("/Statuses/NoPorts"),
                (int)HandlerStatus.ErrorGRPC => Host.RequestLocalizedString("/Statuses/gRPC")
                    .Replace("{0}", gRpcException?.StatusCode.ToString())
                    .Replace("{1}", gRpcException?.Status.Detail),
                _ => $"Undefined: {DeviceStatus}\nE_UNDEFINED\nSomething weird has happened, though we can't tell what."
            }
            : $"Undefined: {DeviceStatus}\nE_UNDEFINED\nSomething weird has happened, though we can't tell what.";

        // Joints' list / you need to (should) update at every update() call
        // Each must have its own role or _Manual to force user's manual set
        // Adding and removing trackers will cause an automatic UI refresh
        // note: only when the device HAS BEEN initialized WASN'T shut down
        public ObservableCollection<TrackedJoint> TrackedJoints { get; } =
            // Prepend all supported joints to the joints list
            new(Enum.GetValues<IJointType>()
                .Select(x => new TrackedJoint { Name = x.ToString(), Role = JointTypeDictionary[x] }));

        // This is called after the app loads the plugin
        public void OnLoad()
        {
            Host?.Log("Loading Caramel now!");
            Host?.Log("Checking the local IP address...");
            RefreshIPs();

            // Settings UI setup
            IpTextBlock = new TextBlock
            {
                Text = Addresses.Count > 1 // Format as list if found multiple IPs!
                    ? $"[ {string.Join(", ", Addresses)} ]" // Or show a placeholder
                    : Addresses.FirstOrDefault()?.ToString() ?? "127.0.0.1",
                Margin = new Thickness { Left = 5, Top = 3, Right = 3, Bottom = 3 }
            };

            IpLabelTextBlock = new TextBlock
            {
                Text = Host!.RequestLocalizedString(Addresses.Count > 1
                    ? "/Settings/Labels/LocalIP/Multiple"
                    : "/Settings/Labels/LocalIP/One"),
                Margin = new Thickness(3),
                Opacity = 0.5
            };

            PortTextBlock = new TextBlock
            {
                Text = "9648", // Don't allow any changes
                Margin = new Thickness { Left = 5, Top = 3, Right = 3, Bottom = 3 }
            };

            PortLabelTextBlock = new TextBlock
            {
                Text = Host.RequestLocalizedString("/Settings/Labels/Port"),
                Margin = new Thickness(3),
                Opacity = 0.5
            };

            InterfaceRoot = new Page
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Children = { IpLabelTextBlock, IpTextBlock }
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Children = { PortLabelTextBlock, PortTextBlock },
                            Margin = new Thickness { Bottom = 10 }
                        }
                    }
                }
            };

            // Mark the plugin as loaded
            PluginLoaded = true;
            UpdateSettingsInterface();
        }

        // This initializes/connects the device
        private CancellationTokenSource AdvertiseToken { get; set; }

        public void Initialize()
        {
            try
            {
                IsInitialized = false; // Mark as not initialized yet
                Host?.Log("Checking the local IP address...");
                RefreshIPs();

                AdvertiseToken = new CancellationTokenSource();
                Task.Run(() => AdvertiseService(AdvertiseToken.Token));

                Host?.Log("Setting up the gRPC server...");
                GrpServer = new Server
                {
                    Services =
                    {
                        Caramethyst.BindService(new CaramethystService(this))
                    },
                    Ports =
                    {
                        new ServerPort(Addresses.FirstOrDefault()?.ToString() ?? "127.0.0.1", 8649,
                            ServerCredentials.Insecure)
                    }
                };

                Host?.Log("Starting the gRPC server...");
                GrpServer.Start();
                IsInitialized = true;
            }
            catch (RpcException e)
            {
                Host?.Log($"Tried to initialize Caramel with error: {e}");
                gRpcException = e;
                DeviceStatus = (int)HandlerStatus.ErrorGRPC;
                UpdateSettingsInterface();
            }
            catch (Exception e)
            {
                Host?.Log($"Tried to initialize Caramel with error: {e}");
            }

            Host?.Log("Tried to initialize Caramel!");
            DeviceStatus = (int)HandlerStatus.ConnectionDead;
        }

        // This is called when the device is closed
        public void Shutdown()
        {
            Host?.Log("Tried to shut down Caramel!");
            IsInitialized = false;
            DeviceStatus = (int)HandlerStatus.ServiceNotStarted;

            AdvertiseToken.Cancel();
            GrpServer.ShutdownAsync();
        }

        // This is called to update the device (each loop)
        public void Update()
        {
            // Update all prepended joints with the pose of the HMD
            // Or if not available for some reason, .Zero and .Identity
            TrackedJoints.ToList().ForEach(x =>
            {
                x.TrackingState = TrackedJointState.StateTracked;
                x.Position = Host?.HmdPose.Position ?? System.Numerics.Vector3.Zero;
                x.Orientation = Host?.HmdPose.Orientation ?? Quaternion.Identity;
            });
        }

        // Signal the joint eg psm_id0 that it's been selected
        public void SignalJoint(int jointId)
        {
            Host?.Log($"Tried to signal joint with ID: {jointId}!");
        }

        public void PushJointPoses(List<JointPose> poses)
        {
            IsSkeletonTracked = false;
            foreach (var jointPose in poses)
            {
                IsSkeletonTracked = true; // There has to be smth
                TrackedJoints[(int)jointPose.Role].TrackingState =
                    TrackedJointState.StateTracked;

                TrackedJoints[(int)jointPose.Role].Position =
                    new System.Numerics.Vector3
                    {
                        X = (float)jointPose.Position.X,
                        Y = (float)jointPose.Position.Y,
                        Z = (float)jointPose.Position.Z
                    };

                TrackedJoints[(int)jointPose.Role].Orientation =
                    Quaternion.CreateFromYawPitchRoll(
                        (float)jointPose.Rotation.Y,
                        (float)jointPose.Rotation.X,
                        (float)jointPose.Rotation.Z);
            }
        }

        private async Task AdvertiseService(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                    var udpServer = new UdpClient(8649);
                    var clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    udpServer.Receive(ref clientEndPoint); // Receive the request
                    var responseData = Encoding.ASCII.GetBytes(
                        Addresses.FirstOrDefault()?.ToString() ?? "127.0.0.1");
                    await udpServer.SendAsync(responseData, responseData.Length, clientEndPoint);
                    udpServer.Close();
                    await Task.Delay(500, token);
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void RefreshIPs()
        {
            Addresses = NetworkInformation.GetHostNames().Where(hostName =>
                    hostName.IPInformation?.NetworkAdapter?.NetworkAdapterId == NetworkInformation
                        .GetInternetConnectionProfile().NetworkAdapter.NetworkAdapterId &&
                    hostName.Type == HostNameType.Ipv4)
                .Where(x => IPAddress.TryParse(x.CanonicalName, out _))
                .Select(x => IPAddress.Parse(x.CanonicalName)).ToList();
        }

        public void UpdateSettingsInterface()
        {
            IsSkeletonTracked = DeviceStatus == 0 && IsInitialized;
            Host?.RefreshStatusInterface();
        }

        #region UI Elements

        private TextBlock IpTextBlock { get; set; }
        private TextBlock IpLabelTextBlock { get; set; }
        private TextBlock PortTextBlock { get; set; }
        private TextBlock PortLabelTextBlock { get; set; }

        #endregion
    }
}

public class CaramethystService(Caramel.Caramel parent) : Caramethyst.CaramethystBase
{
    private Caramel.Caramel Host { get; } = parent;
    private System.Timers.Timer TimeoutTimer { get; set; }

    public override Task<PingReply> Ping(PingRequest request, ServerCallContext context)
    {
        try
        {
            Host?.Host?.Log($"{request.Name} pinged the service!");
            var statusBackup = Host!.DeviceStatus;
            Host!.DeviceStatus = (int)Caramel.Caramel.HandlerStatus.ServiceSuccess;
            if (statusBackup != Host!.DeviceStatus) Host!.UpdateSettingsInterface();
            RestartTimeoutTimerWatchdog(); // Re-initialize the timer and restart
        }
        catch (Exception ex)
        {
            Host?.Host?.Log(ex);
        }

        return Task.FromResult(new PingReply
        {
            Message = $"Saying hello to {request.Name}!"
        });
    }

    public override async Task<Empty> SendPoses(IAsyncStreamReader<JointPose> requestStream,
        ServerCallContext context)
    {
        try
        {
            // Download all joint messages and send them to the handler
            Host?.PushJointPoses(await requestStream.ToListAsync());
            var statusBackup = Host!.DeviceStatus;
            Host!.DeviceStatus = (int)Caramel.Caramel.HandlerStatus.ServiceSuccess;
            if (statusBackup != Host!.DeviceStatus) Host!.UpdateSettingsInterface();
            RestartTimeoutTimerWatchdog(); // Re-initialize the timer and restart
        }
        catch (Exception ex)
        {
            Host?.Host?.Log(ex);
        }

        return new Empty();
    }

    private void RestartTimeoutTimerWatchdog()
    {
        try
        {
            TimeoutTimer.Elapsed -= ServerTimeoutHandler;
            TimeoutTimer.Enabled = false;
        }
        catch
        {
            // ignored
        }

        TimeoutTimer = new System.Timers.Timer(TimeSpan.FromSeconds(3));
        TimeoutTimer.Elapsed += ServerTimeoutHandler;
        TimeoutTimer.AutoReset = false;
        TimeoutTimer.Enabled = true;
    }

    private void ServerTimeoutHandler(object sender, ElapsedEventArgs args)
    {
        try
        {
            Host?.Host?.Log("Client timed out, marking as E_DEAD!");
            Host!.DeviceStatus = (int)Caramel.Caramel.HandlerStatus.ConnectionDead;
            Host.UpdateSettingsInterface();
        }
        catch (Exception)
        {
            // ignored
        }
    }
}
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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Enum = System.Enum;
using System.Timers;
using Makaretu.Dns;

namespace Caramel;

// This is the main class of your plugin
// Metadata includes the 'Name' and 'Guid'
// You can also add a 'Publisher' and a 'Website'
[Export(typeof(ITrackingDevice))]
[ExportMetadata("Name", "Caramel")]
[ExportMetadata("Guid", "K2VRTEAM-AME2-APII-DVCE-CARAMLAMTHST")]
[ExportMetadata("Publisher", "K2VR Team, MochiDoesVR")]
[ExportMetadata("Version", "1.0.1.1")]
[ExportMetadata("Website", "https://github.com/KinectToVR/plugin_Caramel")]
public class Caramel : ITrackingDevice
{
    public enum HandlerStatus
    {
        ServiceNotStarted = -1, // Not initialized
        ServiceSuccess = 0, // Success, everything's fine!
        ConnectionDead = 10, // No connection
        ErrorGrpc = 11, // No data received
        ErrorInitFailed = 12, // Init failed
        ErrorPortsTaken = 13 // Ports taken
    }

    private RpcException _gRpcException;

    [Import(typeof(IAmethystHost))] public IAmethystHost Host { get; set; }

    // This is the root of your settings UI
    private Page InterfaceRoot { get; set; }
    public static IAmethystHost HostStatic { get; set; }

    private List<IPAddress> Addresses { get; set; }
    private Server GrpServer { get; set; }
    private bool PluginLoaded { get; set; }

    public bool IsPositionFilterBlockingEnabled => false;
    public bool IsPhysicsOverrideEnabled => false;
    public bool IsSelfUpdateEnabled => true;
    public bool IsFlipSupported => false;
    public bool IsAppOrientationSupported => true;
    public bool IsSettingsDaemonSupported => true;
    public object SettingsInterfaceRoot => InterfaceRoot;
    public bool IsInitialized { get; set; }
    public bool IsSkeletonTracked { get; set; }
    public int DeviceStatus { get; set; } = -1;

    // ReSharper disable once AssignNullToNotNullAttribute
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
            (int)HandlerStatus.ErrorGrpc => Host.RequestLocalizedString("/Statuses/gRPC")
                .Replace("{0}", _gRpcException?.StatusCode.ToString())
                .Replace("{1}", _gRpcException?.Status.Detail),
            _ => $"Undefined: {DeviceStatus}\nE_UNDEFINED\nSomething weird has happened, though we can't tell what."
        }
        : $"Undefined: {DeviceStatus}\nE_UNDEFINED\nSomething weird has happened, though we can't tell what.";

    // Joints' list / you need to (should) update at every update() call
    // Each must have its own role or _Manual to force user's manual set
    // Adding and removing trackers will cause an automatic UI refresh
    // note: only when the device HAS BEEN initialized WASN'T shut down
    public ObservableCollection<TrackedJoint> TrackedJoints { get; } =
        // Prepend all supported joints to the joints list
        new(Enum.GetValues<TrackedJointType>()
            .Where(x => x is not TrackedJointType.JointManual)
            .Select(x => new TrackedJoint
            {
                Name = HostStatic?.RequestLocalizedString($"/JointsEnum/{x.ToString()}") ?? x.ToString(),
                Role = x,
                SupportedInputActions = []
            }));

    // This is called after the app loads the plugin
    public void OnLoad()
    {
        // Backup the plugin host
        HostStatic = Host;

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
            Text = "8649", // Don't allow any changes
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
            AdvertiseService(AdvertiseToken.Token);

            Host?.Log("Setting up the gRPC server...");
            GrpServer = new Server
            {
                Services =
                {
                    DataHost.BindService(new CaramelService(this))
                },
                Ports =
                {
                    new ServerPort("0.0.0.0", 8649, ServerCredentials.Insecure)
                }
            };

            Host?.Log("Starting the gRPC server...");
            GrpServer.Start();
            IsInitialized = true;
        }
        catch (RpcException e)
        {
            Host?.Log($"Tried to initialize Caramel with error: {e}");
            _gRpcException = e;
            DeviceStatus = (int)HandlerStatus.ErrorGrpc;
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
            x.Position = Host?.HmdPose.Position ?? Vector3.Zero;
            x.Orientation = Host?.HmdPose.Orientation ?? Quaternion.Identity;
        });
    }

    // Signal the joint eg psm_id0 that it's been selected
    public void SignalJoint(int jointId)
    {
        Host?.Log($"Tried to signal joint with ID: {jointId}!");
    }

    public void PushJointPose(DataJoint joint, TrackedJointType? typeOverride = null)
    {
        IsSkeletonTracked = true; // There has to be smth
        TrackedJoints[(int)(typeOverride ?? CaramelService.JointDictionary[joint.Name])]
            .TrackingState = TrackedJointState.StateTracked;

        TrackedJoints[(int)(typeOverride ?? CaramelService.JointDictionary[joint.Name])]
            .Position = new Vector3
        {
            X = joint.Position.X,
            Y = joint.Position.Y,
            Z = joint.Position.Z
        };

        TrackedJoints[(int)(typeOverride ?? CaramelService.JointDictionary[joint.Name])]
            .Orientation = new Quaternion
        {
            W = joint.Orientation.W,
            X = joint.Orientation.X,
            Y = joint.Orientation.Y,
            Z = joint.Orientation.Z
        };
    }

    private void AdvertiseService(CancellationToken token)
    {
        try
        {
            var service = new ServiceProfile(
                "Amethyst Caramel Plugin",
                "_caramel._tcp", 8648);

            service.AddProperty("port", "8649");
            service.AddProperty("ip",
                Addresses.FirstOrDefault()?.ToString() ??
                (service.Resources.LastOrDefault(x => x.Class is
                    DnsClass.IN && x.Type is DnsType.A) as ARecord)?
                .Address?.ToString() ?? "0.0.0.0");

            var sd = new ServiceDiscovery();
            sd.Unadvertise();
            sd.Advertise(service);
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

public class CaramelService(Caramel parent) : DataHost.DataHostBase
{
    private int RefreshCounter { get; set; } = -1;
    private Caramel Host { get; } = parent;
    private System.Timers.Timer TimeoutTimer { get; set; }

    public static List<IPAddress> Addresses => NetworkInformation.GetHostNames().Where(hostName =>
            hostName.IPInformation?.NetworkAdapter?.NetworkAdapterId == NetworkInformation
                .GetInternetConnectionProfile().NetworkAdapter.NetworkAdapterId &&
            hostName.Type == HostNameType.Ipv4)
        .Where(x => IPAddress.TryParse(x.CanonicalName, out _))
        .Select(x => IPAddress.Parse(x.CanonicalName)).ToList();

    public static readonly Dictionary<string, TrackedJointType> JointDictionary = new()
    {
        { "head_joint", TrackedJointType.JointHead },
        { "neck_1_joint", TrackedJointType.JointNeck },
        { "spine_7_joint", TrackedJointType.JointSpineShoulder },
        { "left_shoulder_1_joint", TrackedJointType.JointShoulderLeft },
        { "left_forearm_joint", TrackedJointType.JointElbowLeft },
        { "left_hand_joint", TrackedJointType.JointHandLeft },
        { "left_handMid_3_joint", TrackedJointType.JointHandTipLeft },
        { "left_handThumb_2_joint", TrackedJointType.JointThumbLeft },
        { "right_shoulder_1_joint", TrackedJointType.JointShoulderRight },
        { "right_forearm_joint", TrackedJointType.JointElbowRight },
        { "right_hand_joint", TrackedJointType.JointHandRight },
        { "right_handMid_3_joint", TrackedJointType.JointHandTipRight },
        { "right_handThumb_2_joint", TrackedJointType.JointThumbRight },
        { "spine_4_joint", TrackedJointType.JointSpineMiddle },
        { "hips_joint", TrackedJointType.JointSpineWaist },
        { "left_upLeg_joint", TrackedJointType.JointHipLeft },
        { "left_leg_joint", TrackedJointType.JointKneeLeft },
        { "left_foot_joint", TrackedJointType.JointFootLeft },
        { "left_toes_joint", TrackedJointType.JointFootTipLeft },
        { "right_upLeg_joint", TrackedJointType.JointHipRight },
        { "right_leg_joint", TrackedJointType.JointKneeRight },
        { "right_foot_joint", TrackedJointType.JointFootRight },
        { "right_toes_joint", TrackedJointType.JointFootTipRight }
    };

    public static readonly Dictionary<TrackedJointType, string> JointDictionaryReverse = new()
    {
        { TrackedJointType.JointHead, "head_joint" },
        { TrackedJointType.JointNeck, "neck_1_joint" },
        { TrackedJointType.JointSpineShoulder, "spine_7_joint" },
        { TrackedJointType.JointShoulderLeft, "left_shoulder_1_joint" },
        { TrackedJointType.JointElbowLeft, "left_forearm_joint" },
        { TrackedJointType.JointWristLeft, "left_hand_joint" },
        { TrackedJointType.JointHandLeft, "left_hand_joint" },
        { TrackedJointType.JointHandTipLeft, "left_handMid_3_joint" },
        { TrackedJointType.JointThumbLeft, "left_handThumb_2_joint" },
        { TrackedJointType.JointShoulderRight, "right_shoulder_1_joint" },
        { TrackedJointType.JointElbowRight, "right_forearm_joint" },
        { TrackedJointType.JointWristRight, "right_hand_joint" },
        { TrackedJointType.JointHandRight, "right_hand_joint" },
        { TrackedJointType.JointHandTipRight, "right_handMid_3_joint" },
        { TrackedJointType.JointThumbRight, "right_handThumb_2_joint" },
        { TrackedJointType.JointSpineMiddle, "spine_4_joint" },
        { TrackedJointType.JointSpineWaist, "hips_joint" },
        { TrackedJointType.JointHipLeft, "left_upLeg_joint" },
        { TrackedJointType.JointKneeLeft, "left_leg_joint" },
        { TrackedJointType.JointFootLeft, "left_foot_joint" },
        { TrackedJointType.JointFootTipLeft, "left_toes_joint" },
        { TrackedJointType.JointHipRight, "right_upLeg_joint" },
        { TrackedJointType.JointKneeRight, "right_leg_joint" },
        { TrackedJointType.JointFootRight, "right_foot_joint" },
        { TrackedJointType.JointFootTipRight, "right_toes_joint" }
    };

    public override Task<StatusResponse> PingDriverService(Empty request, ServerCallContext context)
    {
        try
        {
            Host?.Host?.Log("Client pinged the service!");
            RefreshCounter = RefreshCounter < 0 ? 100 : RefreshCounter + 1;
            var statusBackup = Host!.DeviceStatus;
            Host!.DeviceStatus = (int)Caramel.HandlerStatus.ServiceSuccess;
            if (statusBackup != Host!.DeviceStatus) Host!.UpdateSettingsInterface();
            RestartTimeoutTimerWatchdog(); // Re-initialize the timer and restart
        }
        catch (Exception ex)
        {
            Host?.Host?.Log(ex);
        }

        return Task.FromResult(new StatusResponse { Status = 1 });
    }

    public override async Task PublishJointData(IAsyncStreamReader<DataJoint> requestStream,
        IServerStreamWriter<JointsResponse> responseStream, ServerCallContext context)
    {
        try
        {
            // Download all joint messages and send them to the handler
            await foreach (var data in requestStream.ReadAllAsync())
            {
                // Debug.WriteLine($"Received data for {data.Name}");
                Host?.PushJointPose(data);

                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (data.Name is "left_hand_joint")
                    Host?.PushJointPose(data, TrackedJointType.JointWristLeft);
                if (data.Name is "right_hand_joint")
                    Host?.PushJointPose(data, TrackedJointType.JointWristRight);

                if (data.Name is not "head_joint" || RefreshCounter < 3) continue;

                // Set Status to true when the preview is active and we need all joints
                var requestedJointsResponse = new JointsResponse();

                try
                {
                    var joints = ((Host!.Host as dynamic)?.GetEnabledJoints() as List<TrackedJointType>)!;
                    joints.ForEach(x => requestedJointsResponse.Names.Add(JointDictionaryReverse[x]));

                    Enum.GetValues<TrackedJointType>() // Mark all unused joints as not tracked
                        .Where(x => !joints.Contains(x) && x is not TrackedJointType.JointManual)
                        .ToList().ForEach(x => Host.PushJointPose(new DataJoint { IsTracked = false }, x));
                }
                catch (Exception ex)
                {
                    requestedJointsResponse.Names.Clear();
                    requestedJointsResponse.Names.Add("head_joint");
                }

                await responseStream.WriteAsync(requestedJointsResponse);
                RefreshCounter = 0; // No need to stream those again
            }
        }
        catch (Exception ex)
        {
            Host?.Host?.Log(ex);
        }
    }

    private void RestartTimeoutTimerWatchdog()
    {
        try
        {
            TimeoutTimer.Elapsed -= ServerTimeoutHandler;
            TimeoutTimer.Enabled = false;
        }
        catch (Exception)
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
            Host!.DeviceStatus = (int)Caramel.HandlerStatus.ConnectionDead;
            Host.UpdateSettingsInterface();
        }
        catch (Exception)
        {
            // ignored
        }
    }
}
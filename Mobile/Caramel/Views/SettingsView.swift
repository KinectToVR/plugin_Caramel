import ARKit
import Foundation
import GRPC
import NIOCore
import NIOPosix
import Network
import RealityKit
import SwiftProtobuf
import SwiftUI

struct SettingsView: View {
    @AppStorage("SettingsIpAddress") public var ipAddress: String = ""
    @AppStorage("SettingsPort") public var port: Int = 8649

    @State public var dataChannel: GRPCChannel? = nil
    @State public var dataClient: Caramel_DataHostAsyncClient? = nil

    @State public var isConnected: Bool = false
    @State public var blockInteractions: Bool = false
    @State private var isAnimating: Bool = false
    @State private var connectionErrorText: String = ""

    public var streamManager = JointDataStreamManager()

    var hasValidAddress: Bool {
        var socketAddress = sockaddr_in()
        return ipAddress.withCString({ cstring in
            inet_pton(AF_INET, cstring, &socketAddress.sin_addr)
        }) == 1
    }

    var body: some View {
        ExpandableView(
            thumbnail: ThumbnailView {
                ZStack {
                    if isConnected && !blockInteractions {
                        HStack(spacing: 10) {
                            Image(systemName: "gearshape").scaleEffect(
                                x: 1.2, y: 1.2, anchor: .center)
                            Text("Settings").font(.title2)
                        }
                        .padding()
                        .transition(
                            AnyTransition.opacity.animation(
                                .easeInOut(duration: 0.5)))
                    }
                    if !(isConnected || blockInteractions) {
                        HStack(spacing: 10) {
                            Image(systemName: "personalhotspot.slash")
                                .scaleEffect(1.2)
                                .symbolEffect(.bounce, value: isAnimating)
                            Text("Not connected").font(.title2)
                        }
                        .padding()
                        .transition(
                            AnyTransition.opacity.animation(
                                .easeInOut(duration: 0.5)))
                    }
                    if blockInteractions {
                        HStack(spacing: 10) {
                            Image(systemName: "shareplay")
                                .scaleEffect(1.2)
                                .symbolEffect(.bounce, value: isAnimating)
                            Text("Searching...").font(.title2)
                        }
                        .padding()
                        .transition(
                            AnyTransition.opacity.animation(
                                .easeInOut(duration: 0.5)))
                    }
                }.frame(height: 60)
                    .padding(.horizontal)
                    .background(.ultraThinMaterial)
                    .cornerRadius(50)
                    .overlay(
                        RoundedRectangle(cornerRadius: 50)
                            .stroke(.gray, lineWidth: 1)
                    )
                    .animation(
                        .easeInOut(duration: 0.5),
                        value: (isConnected ? 1 : 0)
                            + (blockInteractions ? 1 : 0))
            },
            expanded: ExpandedView {
                ZStack {
                    VStack {
                        List {
                            Section {
                                VStack {
                                    ZStack {
                                        Text("Caramel").font(.largeTitle)
                                            .fontWeight(.semibold)
                                            .frame(
                                                maxWidth: .infinity,
                                                alignment: .center
                                            )
                                            .padding()
                                    }.overlay(
                                        Image("Icon")
                                            .resizable()
                                            .frame(
                                                width: 50, height: 50,
                                                alignment: .center
                                            )
                                            .padding(), alignment: .trailing
                                    )
                                    .overlay(
                                        Image("Icon")
                                            .resizable()
                                            .frame(
                                                width: 50, height: 50,
                                                alignment: .center
                                            )
                                            .padding(), alignment: .leading)
                                }
                            }
                            .listRowBackground(Color.clear)
                            Section(
                                footer: Text(connectionErrorText)
                                    .foregroundStyle(.red)
                            ) {
                                HStack {
                                    Text("Status")
                                    Spacer()
                                    Text(
                                        blockInteractions
                                            ? "Searching..."
                                            : (isConnected
                                                ? "Connected" : "Not connected")
                                    )
                                    .foregroundStyle(
                                        .secondary)
                                }
                                HStack {
                                    Text("IP Address")
                                    Spacer()
                                    TextField("Not set", text: $ipAddress)
                                        .onChange(of: ipAddress) {
                                            oldValue, newValue in
                                            validateIPAddress(newValue)
                                        }
                                        .submitLabel(.done)
                                        .onSubmit {
                                            if ipAddress != "" {
                                                Task {
                                                    await setupClient()
                                                }
                                            }
                                        }
                                        .multilineTextAlignment(.trailing)
                                        .foregroundStyle(.secondary)
                                        .fixedSize(
                                            horizontal: true,
                                            vertical: false
                                        )
                                        .disabled(blockInteractions)
                                    Button {
                                        Task {
                                            await runDiscovery()
                                        }
                                    } label: {
                                        Image(systemName: "bonjour")
                                    }
                                    .buttonStyle(BorderlessButtonStyle())
                                    .foregroundStyle(.secondary)
                                    .disabled(blockInteractions)
                                }
                                .contentShape(Rectangle())
                                HStack {
                                    Text("Port")
                                    Spacer()
                                    TextField(
                                        String(port), value: $port,
                                        formatter: NumberFormatter()
                                    )
                                    .onChange(of: port) { oldValue, newValue in
                                        port = min(max(newValue, 0), 65535)
                                    }
                                    .submitLabel(.done)
                                    .onSubmit {
                                        if ipAddress != "" {
                                            Task {
                                                await setupClient()
                                            }
                                        }
                                    }
                                    .foregroundStyle(.secondary)
                                    .fixedSize(
                                        horizontal: true,
                                        vertical: false
                                    )
                                    .disabled(blockInteractions)
                                }
                            }
                            Section {
                                HStack {
                                    Text("Mode")
                                    Spacer()
                                    Text("Amethyst").foregroundStyle(
                                        .secondary)
                                }
                                HStack {
                                    Text("Version")
                                    Spacer()
                                    Text("1.0.1").foregroundStyle(
                                        .secondary)
                                }
                            }
                        }
                        .listStyle(.insetGrouped)
                        .scrollContentBackground(.hidden)
                        .background(Color.clear)
                    }.frame(height: 490)
                        .background(.ultraThinMaterial)
                        .cornerRadius(20)
                        .overlay(
                            RoundedRectangle(cornerRadius: 20)
                                .stroke(.gray, lineWidth: 1))
                }.padding()
            }
        ).onAppear(perform: {
            Task {
                if !(await setupClient()) { _ = await runDiscovery() }
            }
        })
    }

    private func updateConnectionDetails(_ _ip: String?, _ _port: String?) async
    {
        if (_ip?.isEmpty ?? true) || (_port?.isEmpty ?? true) { return }

        ipAddress = _ip ?? ipAddress
        port = Int(_port ?? String(port)) ?? port
        _ = await setupClient()
    }

    private func runDiscovery() async -> Bool {

        blockInteractions = true
        print("Setting up discovery")

        let bonjourParms = NWParameters.init()
        bonjourParms.allowLocalEndpointReuse = true
        bonjourParms.acceptLocalOnly = true
        bonjourParms.allowFastOpen = true

        let browser = NWBrowser(
            for: .bonjourWithTXTRecord(
                type: "_caramel._tcp.", domain: nil), using: bonjourParms)

        browser.stateUpdateHandler = { [browser] newState in
            switch newState {
            case .failed(let error):
                print("NW: now in Error state: \(error)")
                browser.cancel()
            case .ready:
                print("NW: new bonjour discovery - ready")
            default:
                break
            }
        }

        browser.browseResultsChangedHandler = {
            [self] (results, changes) in
            for device in results {
                switch device.metadata {
                case .bonjour(let record):
                    print("Record: \(record.dictionary)")
                    do {
                        Task {
                            await updateConnectionDetails(
                                record.dictionary["ip"],
                                record.dictionary["port"])
                        }
                    }
                case .none:
                    print("Record: none")
                @unknown default:
                    print("Record: default")
                }
            }
        }

        print("Starting discovery")
        browser.start(queue: DispatchQueue.main)

        for _ in 1...3 {
            isAnimating = false
            try! await Task.sleep(
                nanoseconds: UInt64(1.5 * Double(NSEC_PER_SEC)))

            isAnimating = true
            try! await Task.sleep(
                nanoseconds: UInt64(1.5 * Double(NSEC_PER_SEC)))
        }

        print("Stopping discovery")
        browser.cancel()

        blockInteractions = false
        return await setupClient()
    }

    func setupClient() async -> Bool {
        connectionErrorText = ""
        isConnected = false
        blockInteractions = true

        try! await dataChannel?.close().get()

        isAnimating = false
        try! await Task.sleep(
            nanoseconds: UInt64(1.5 * Double(NSEC_PER_SEC)))

        isAnimating = true
        try! await Task.sleep(
            nanoseconds: UInt64(1.5 * Double(NSEC_PER_SEC)))

        do {
            let group = MultiThreadedEventLoopGroup(numberOfThreads: 1)
            dataChannel = try GRPCChannelPool.with(
                target: .host(ipAddress, port: port),
                transportSecurity: .plaintext,
                eventLoopGroup: group
            )

            dataClient = Caramel_DataHostAsyncClient(
                channel: try GRPCChannelPool.with(
                    target: .host(ipAddress, port: port),
                    transportSecurity: .plaintext,
                    eventLoopGroup: group
                ))
        } catch {
            print("Failed: \(error)")
            connectionErrorText = error.localizedDescription
            blockInteractions = false
            return false
        }

        do {
            let data = try await dataClient!.pingDriverService(
                Google_Protobuf_Empty(),
                callOptions: CallOptions(timeLimit: .timeout(.seconds(1))))

            if data.status == 1 {
                streamManager.finishStream()
                streamManager.resetStream()

                let responseStream = dataClient!.publishJointData(
                    streamManager.dataJointStream)

                Task {
                    for try await response in responseStream {
                        streamManager.requestedJoints = response.names
                    }
                }
            }

            print("Received: \(data.status)")
            isConnected = data.status == 1
            blockInteractions = false

            return data.status == 1
        } catch {
            print("Failed: \(error)")
            connectionErrorText = error.localizedDescription
        }

        blockInteractions = false
        return false
    }

    private func validateIPAddress(_ newValue: String) {
        let filteredValue = newValue.filter { $0.isNumber || $0 == "." }

        if isValidIPAddress(filteredValue) {
            ipAddress = filteredValue
        } else if filteredValue.isEmpty {
            ipAddress = ""
        } else {
            ipAddress = filteredValue
        }
    }

    private func isValidIPAddress(_ ip: String) -> Bool {
        let components = ip.split(separator: ".")
        guard components.count == 4 else { return false }

        for component in components {
            guard let number = Int(component), number >= 0, number <= 255 else {
                return false
            }
        }
        return true
    }

    public func streamJointData(
        name: String, isTracked: Bool,
        position: simd_float3, orientation: simd_quatf
    ) async {
        var joint = Caramel_DataJoint()

        joint.name = name
        joint.isTracked = isTracked

        joint.position = Caramel_DataVector()
        joint.position.x = Double(position.x)
        joint.position.y = Double(position.y)
        joint.position.z = Double(position.z)

        joint.orientation = Caramel_DataQuaternion()
        joint.orientation.x = Double(orientation.vector.x)
        joint.orientation.y = Double(orientation.vector.y)
        joint.orientation.z = Double(orientation.vector.z)
        joint.orientation.w = Double(orientation.vector.w)

        streamManager.sendData(joint)
    }
}

struct SettingsView_Previews: PreviewProvider {
    static var previews: some View {
        SettingsView()
    }
}

extension NumberFormatter {
    static func integerFormatter() -> NumberFormatter {
        let formatter = NumberFormatter()
        formatter.numberStyle = .none  // No decimals allowed
        formatter.minimum = 0 as NSNumber
        formatter.maximum = 65535 as NSNumber
        return formatter
    }
}

class NetServiceDelegateImpl: NSObject, NetServiceDelegate {
    func netServiceDidResolveAddress(_ sender: NetService) {
        print("Resolved service: \(sender)")
        if let addresses = sender.addresses {
            for address in addresses {
                if let ipAddress = getIPAddress(from: address) {
                    print("Resolved IP Address: \(ipAddress)")
                }
            }
        }
    }

    func netService(
        _ sender: NetService, didNotResolve errorDict: [String: NSNumber]
    ) {
        print("Failed to resolve service: \(errorDict)")
    }

    private func getIPAddress(from address: Data) -> String? {
        var hostname = [CChar](repeating: 0, count: Int(NI_MAXHOST))
        address.withUnsafeBytes { (pointer: UnsafeRawBufferPointer) in
            let sockaddrPointer = pointer.baseAddress!.assumingMemoryBound(
                to: sockaddr.self)
            getnameinfo(
                sockaddrPointer, socklen_t(address.count), &hostname,
                socklen_t(hostname.count), nil, 0, NI_NUMERICHOST)
        }
        return String(cString: hostname)
    }
}

class JointDataStreamManager {
    @AppStorage("SettingsJoints") private var joints: String = "head_joint"

    public var requestedJoints: [String] {
        get { return joints.components(separatedBy: ",") }
        set { joints = newValue.joined(separator: ",") }
    }

    private(set) var jointDataContinuation:
        AsyncStream<Caramel_DataJoint>.Continuation?
    private(set) var dataJointStream: AsyncStream<Caramel_DataJoint> =
        AsyncStream { _ in }

    func resetStream() {
        dataJointStream = AsyncStream<Caramel_DataJoint> { continuation in
            self.jointDataContinuation = continuation
        }
    }

    func sendData(_ newData: Caramel_DataJoint) {
        jointDataContinuation?.yield(newData)
    }

    func finishStream() {
        jointDataContinuation?.finish()
    }
}

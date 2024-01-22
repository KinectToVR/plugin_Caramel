using System;
using System.Collections.Generic;
using System.Threading;
using ARKit;
using Foundation;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using OpenTK;
using SceneKit;
using UIKit;

namespace Caramel.Client;

public class ArKitSkeletonDelegate : ARSCNViewDelegate
{
    public static readonly object GrpLocker = new();

    private static readonly Dictionary<IJointType, string> JointDictionary = new()
    {
        { IJointType.JointHead, "head_joint" },
        { IJointType.JointNeck, "neck_1_joint" },
        { IJointType.JointSpineShoulder, "spine_7_joint" },
        { IJointType.JointShoulderLeft, "left_shoulder_1_joint" },
        { IJointType.JointElbowLeft, "left_forearm_joint" },
        { IJointType.JointWristLeft, "left_hand_joint" },
        { IJointType.JointHandLeft, "left_hand_joint" },
        { IJointType.JointHandTipLeft, "left_handMid_3_joint" },
        { IJointType.JointThumbLeft, "left_handThumb_2_joint" },
        { IJointType.JointShoulderRight, "right_shoulder_1_joint" },
        { IJointType.JointElbowRight, "right_forearm_joint" },
        { IJointType.JointWristRight, "right_hand_joint" },
        { IJointType.JointHandRight, "right_hand_joint" },
        { IJointType.JointHandTipRight, "right_handMid_3_joint" },
        { IJointType.JointThumbRight, "right_handThumb_2_joint" },
        { IJointType.JointSpineMiddle, "spine_4_joint" },
        { IJointType.JointSpineWaist, "hips_joint" },
        { IJointType.JointHipLeft, "left_upLeg_joint" },
        { IJointType.JointKneeLeft, "left_leg_joint" },
        { IJointType.JointFootLeft, "left_foot_joint" },
        { IJointType.JointFootTipLeft, "left_toes_joint" },
        { IJointType.JointHipRight, "right_upLeg_joint" },
        { IJointType.JointKneeRight, "right_leg_joint" },
        { IJointType.JointFootRight, "right_foot_joint" },
        { IJointType.JointFootTipRight, "right_toes_joint" }
    };

    private readonly Dictionary<string, SCNNode> _joints = new();
    public static Channel GrpChannel { get; set; }
    public static Caramethyst.CaramethystClient Client { get; set; }

    public override void DidAddNode(ISCNSceneRenderer renderer, SCNNode node, ARAnchor anchor)
    {
        if (anchor is not ARBodyAnchor bodyAnchor)
            return;

        foreach (var jointName in ARSkeletonDefinition.DefaultBody3DSkeletonDefinition.JointNames)
        {
            var jointNode = MakeJointGeometry(jointName);
            var jointPosition = GetDummyJointNode(bodyAnchor, jointName).Position;

            jointNode.Position = jointPosition;
            if (_joints.ContainsKey(jointName)) continue;

            node.AddChildNode(jointNode);
            _joints.Add(jointName, jointNode);
        }
    }

    public override async void DidUpdateNode(ISCNSceneRenderer renderer, SCNNode node, ARAnchor anchor)
    {
        if (anchor is not ARBodyAnchor bodyAnchor) return;

        var jointNames = ARSkeletonDefinition.DefaultBody3DSkeletonDefinition.JointNames;
        foreach (var jointName in jointNames)
        {
            var jointPosition = GetDummyJointNode(bodyAnchor, jointName).Position;
            if (_joints.TryGetValue(jointName, out var joint)) joint.Position = jointPosition;
        }

        if (Client is null) return;
        try
        {
            AsyncClientStreamingCall<JointPose, Empty> clientStreamingCall;
            lock (GrpLocker)
            {
                clientStreamingCall =
                    Client.SendPoses(cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
            }

            // Send all positions, rotations for each joint
            foreach (var trackerType in JointDictionary.Keys)
                await clientStreamingCall.RequestStream.WriteAsync(GetJointPose(bodyAnchor, trackerType));

            // Complete the loop
            await clientStreamingCall.RequestStream.CompleteAsync();
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private SCNNode GetDummyJointNode(ARBodyAnchor bodyAnchor, string jointName)
    {
        var jointTransform = bodyAnchor.Skeleton.GetModelTransform((NSString)jointName);

        var node = new SCNNode
        {
            Transform = jointTransform.ToScnMatrix4(),
            Position = new SCNVector3(jointTransform.Column3)
        };

        return node;
    }

    private JointPose GetJointPose(ARBodyAnchor anchor, IJointType type)
    {
        using var node = GetDummyJointNode(anchor, JointDictionary[type]);
        return new JointPose
        {
            Role = type,
            Position = new Vector3
            {
                X = -node.Position.X,
                Y = node.Position.Y,
                Z = node.Position.Z
            },
            Rotation = new Vector3
            {
                X = node.EulerAngles.X,
                Y = -node.EulerAngles.Y,
                Z = -node.EulerAngles.Z
            }
        };
    }

    private SCNNode MakeJointGeometry(string jointName)
    {
        var jointNode = new SCNNode();

        jointNode.Geometry = SCNSphere.Create(0.01f);
        ((SCNSphere)jointNode.Geometry).SegmentCount = 3;

        var material = new SCNMaterial();
        material.Diffuse.Contents = UIColor.Purple;
        jointNode.Geometry.FirstMaterial = material;

        return jointNode;
    }
}

public static class SceneKitExtensions
{
    public static SCNMatrix4 ToScnMatrix4(this NMatrix4 mtx)
    {
        return SCNMatrix4.CreateFromColumns(mtx.Column0, mtx.Column1, mtx.Column2, mtx.Column3);
    }
}
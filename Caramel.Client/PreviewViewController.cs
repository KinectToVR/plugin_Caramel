using System;
using ARKit;
using Foundation;
using UIKit;

namespace Caramel.Client
{
    public partial class PreviewViewController : UIViewController
    {
        private NSObject _cameraContents;

        public PreviewViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            ARKitView.Delegate = new ArKitSkeletonDelegate();

#if DEBUG
        // Enables ARKit device statistics to assist with debugging and stuff.
        ARKitView.ShowsStatistics = true;
#endif

            UIApplication.SharedApplication.IdleTimerDisabled = true;
            UIDevice.CurrentDevice.ProximityMonitoringEnabled = true;

            if (_cameraContents is null)
                _cameraContents = ARKitView.Scene.Background.Contents;

            ARKitView.Scene.Background.Contents = UIColor.Black;
            PreviewSelector.ValueChanged += SetPreviewMode;
        }

        private void SetPreviewMode(object sender, EventArgs e)
        {
            if (_cameraContents is null)
                _cameraContents = ARKitView.Scene.Background.Contents;

            // ReSharper disable once ConvertSwitchStatementToSwitchExpression
            switch (PreviewSelector.SelectedSegment)
            {
                case 0:
                    ARKitView.Scene.Background.Contents = UIColor.Black;
                    break;
                case 1:
                    ARKitView.Scene.Background.Contents = _cameraContents;
                    break;
            }
        }

        public override void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);

            var bodyTrackingConfiguration = new ARBodyTrackingConfiguration
            {
                WorldAlignment = ARWorldAlignment.Gravity
            };

            ARKitView.Session.Run(bodyTrackingConfiguration);
        }

        public override void ViewDidDisappear(bool animated)
        {
            base.ViewDidDisappear(animated);
            ARKitView.Session.Pause();
        }
    }
}
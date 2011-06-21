﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GalaSoft.MvvmLight;
using System.Windows.Media.Media3D;
using GalaSoft.MvvmLight.Command;
using System.Windows.Input;
using System.Threading;
using Kinect.Common;
using System.Windows.Media;
using GalaSoft.MvvmLight.Threading;
using System.Windows.Media.Imaging;
using System.Windows;
using Kinect.Common;
using System.Diagnostics;
using Kinect.Core;
using log4net;

namespace Kinect.Plugins.Common.ViewModels
{
    public abstract class PowerpointOverlayViewModelBase : ViewModelBase
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(PowerpointOverlayViewModelBase));
        private static object _syncRoot = new object();

        private bool _mouseVisible = false;

        private Size ScreenResolution = new Size(1280, 1024);

        private KinectManager KinectManager = KinectManager.Instance;

        private int _laserOwner = -1;
        private Point3D _laser = new Point3D(0, 0, 0);
        public Point3D Laser
        {
            get { return _laser; }
            set
            {
                if (Math.Abs(_laser.X - value.X) > (value.Z / 2) ||
                    Math.Abs(_laser.Y - value.Y) > (value.Z / 2))
                {
                    _laser = value;
                    RaisePropertyChanged("Laser");
                }
            }
        }

        private string _debugMessage = string.Empty;
        public string DebugMessage
        {
            get { return _debugMessage; }
            set
            {
                if (!value.Equals(_debugMessage))
                {
                    var old = _debugMessage;
                    _debugMessage = value;
                    RaisePropertyChanged("DebugMessage", old, value, true);
                    ShowMessage = false;
                    ShowMessage = true;
                }
            }
        }

        private bool _showMessage = false;
        public bool ShowMessage
        {
            get { return _showMessage; }
            set
            {
                _showMessage = value;
                RaisePropertyChanged("ShowMessage");
            }
        }

        private Visibility _laserVisible = Visibility.Hidden;
        public Visibility LaserVisible
        {
            get { return _laserVisible; }
            set
            {
                if (value != _laserVisible)
                {
                    _laserVisible = value;
                    RaisePropertyChanged("LaserVisible");
                }
            }
        }

        private CameraView _imageType = Kinect.Core.CameraView.None;
        private ImageSource _cameraView;
        public ImageSource CameraView
        {
            get { return _cameraView; }
            set
            {
                lock (_syncRoot)
                {
                    if (value != _cameraView)
                    {
                        _cameraView = value;
                        //TODO: Change to RaisePropertyChanged(() => CameraView)
                        //when MvvMLight V4 is released
                        RaisePropertyChanged("CameraView");
                    }
                }
            }
        }

        public RelayCommand<KeyEventArgs> KeyDownCommand { get; protected set; }
        public RelayCommand<RoutedEventArgs> WindowLoaded { get; protected set; }

        public PowerpointOverlayViewModelBase()
            : base()
        {

        }

        protected void InitializeKinect()
        {
            ScreenResolution = new Size(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            KinectManager.NoEventsForSomeSeconds(5);
            EnableCamera();
            KinectManager.LaserUpdated += LaserUpdated;
            KinectManager.NextSlide += KinectManager_NextSlide;
            KinectManager.PreviousSlide += KinectManager_PreviousSlide;
            KinectManager.TogglePointer += KinectManager_TogglePointer;
            KinectManager.UserLost += KinectManager_UserLost;
            KinectManager.UserFound += KinectManager_UserFound;
            SetDebugMessage("Kinect window initialized");
        }

        public override void Cleanup()
        {
            DissableCamera();
            KinectManager.LaserUpdated -= LaserUpdated;
            KinectManager.NextSlide -= KinectManager_NextSlide;
            KinectManager.PreviousSlide -= KinectManager_PreviousSlide;
            KinectManager.TogglePointer -= KinectManager_TogglePointer;
            KinectManager.UserLost -= KinectManager_UserLost;
            KinectManager.UserFound -= KinectManager_UserFound;
            base.Cleanup();
        }

        private void KinectManager_UserFound(object sender, UserEventArgs e)
        {
            SetDebugMessage(string.Format("User {0} found",e.UserID));
        }

        private void KinectManager_UserLost(object sender, UserEventArgs e)
        {
            if (_laserOwner == e.UserID && LaserVisible == Visibility.Visible)
            {
                ToggleLaser(e.UserID);
            }
            SetDebugMessage(string.Format("User {0} lost",e.UserID));
        }

        private void KinectManager_TogglePointer(object sender, UserEventArgs e)
        {
            SetDebugMessage(string.Format("Toggle Laser ({0})",e.UserID));
            ToggleLaser(e.UserID);
        }

        private void KinectManager_PreviousSlide(object sender, UserEventArgs e)
        {
            SetDebugMessage("Previous slide");
            PreviousSlide();
        }

        private void KinectManager_NextSlide(object sender, UserEventArgs e)
        {
            SetDebugMessage("Next slide");
            NextSlide();
        }

        private void Kinect_CameraDataUpdated(object sender, KinectEventArgs e)
        {
            SetCameraView();
        }

        private void SetDebugMessage(string message)
        {
            DispatcherHelper.CheckBeginInvokeOnUI(() =>
            {
                DebugMessage = message;
            });
        }

        protected virtual bool CameraCommand(KeyEventArgs e)
        {
            if (e.Key == Key.C)
            {
                switch (_imageType)
                {
                    case Kinect.Core.CameraView.Depth:
                        _imageType = Kinect.Core.CameraView.ColoredDepth;
                        break;
                    case Kinect.Core.CameraView.ColoredDepth:
                        _imageType = Kinect.Core.CameraView.Color;
                        break;
                    case Kinect.Core.CameraView.Color:
                        _imageType = Kinect.Core.CameraView.None;
                        break;
                    case Kinect.Core.CameraView.None:
                        _imageType = Kinect.Core.CameraView.Depth;
                        break;
                    default:
                        break;
                }

                if (_imageType == Kinect.Core.CameraView.None)
                {
                    DissableCamera();
                }
                else
                {
                    EnableCamera();
                }
                SetDebugMessage(string.Format("{0} Camera", _imageType.ToString()));
            }
            return e.Key == Key.C || e.Key == Key.Up || e.Key == Key.Down;
        }

        private void SetCameraView()
        {
            if (KinectManager.Kinect != null)
            {
                UpdateCameraView(_imageType);
            }
        }

        private void UpdateCameraView(CameraView view)
        {
            DispatcherHelper.CheckBeginInvokeOnUI(() =>
            {
                if (KinectManager.Kinect != null)
                {
                    CameraView = KinectManager.Kinect.GetCameraView(view);
                }
            });
        }

        private void EnableCamera()
        {
            if (KinectManager.Kinect != null && _imageType != Kinect.Core.CameraView.None)
            {
                KinectManager.Kinect.CameraDataUpdated -= Kinect_CameraDataUpdated;
                KinectManager.Kinect.CameraDataUpdated += Kinect_CameraDataUpdated;
            }

        }

        private void DissableCamera()
        {
            if (KinectManager.Kinect != null)
            {
                KinectManager.Kinect.CameraDataUpdated -= Kinect_CameraDataUpdated;
            }
            ÇlearCameraView();
        }

        private void ÇlearCameraView()
        {
            DispatcherHelper.CheckBeginInvokeOnUI(() =>
            {
                CameraView = null;
            });
        }

        private void ToggleLaser(int userId)
        {
            if (_laserOwner == -1 || _laserOwner == userId)
            {
                switch (LaserVisible)
                {
                    case Visibility.Hidden:
                        _laserOwner = (int)userId;
                        LaserVisible = Visibility.Visible;
                        break;
                    case Visibility.Visible:
                        _laserOwner = -1;
                        LaserVisible = Visibility.Collapsed;
                        break;
                    case Visibility.Collapsed:
                        _laserOwner = (int)userId;
                        LaserVisible = Visibility.Visible;
                        break;
                }
            }
        }

        protected abstract void NextSlide();
        protected abstract void PreviousSlide();

        private void LaserUpdated(object sender, SinglePointEventArgs e)
        {
            if (LaserVisible == Visibility.Visible && _laserOwner == e.UserID)
            {
                var point = e.Point.ToScreenPosition(new Size(640, 480), ScreenResolution, new Point(213, 160), new Size(213, 160));
                Laser = point;
            }
        }

        private void MouseUpdated(object sender, SinglePointEventArgs e)
        {
            if (_mouseVisible)
            {
                MouseHook.X = (int)e.Point.X;
                MouseHook.Y = (int)e.Point.Y;
            }
        }

        #region Simulation

        private Thread _mouseSimulationThread;
        private Thread _laserSimulationThread;

        protected void StartMouseAndLaserSimulation()
        {
            _mouseSimulationThread = new Thread(SimulateMouse);
            _mouseSimulationThread.Start();
            _laserSimulationThread = new Thread(SimulateLaser);
            _laserSimulationThread.Start();
        }

        protected void StopMouseAndLaserSimulation()
        {
            if (_mouseSimulationThread != null)
            {
                _mouseSimulationThread.Abort();
            }
            if (_laserSimulationThread != null)
            {
                _laserSimulationThread.Abort();
            }
        }

        private void SimulateMouse()
        {
            var rand = new Random();

            while (true)
            {
                Thread.Sleep(1000);
                MouseHook.X = rand.Next(800);
                MouseHook.Y = rand.Next(500);
                MouseHook.Show();
            }
        }

        private void SimulateLaser()
        {
            var rand = new Random();
            while (true)
            {
                Thread.Sleep(50);
                Laser = new Point3D(rand.NextDouble() * 400, rand.NextDouble() * 400, rand.NextDouble() * 30);
            }
        }

        #endregion
    }
}

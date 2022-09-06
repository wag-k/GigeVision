using GigeVision.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GigeVisionLibrary.Test.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private int fpsCount;
        private int width = 800;

        private int height = 600;

        private Camera camera;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            Setup();
        }

        public Camera Camera
        {
            get { return camera; }
            set { camera = value; }
        }

        private async void Setup()
        {
            camera = new Camera();
            var listOfDevices = await camera.Gvcp.GetAllGigeDevicesInNetworkAsnyc().ConfigureAwait(false);
            if (listOfDevices.Count > 0)
            {
                Camera.IP = listOfDevices.FirstOrDefault()?.IP;
            }
            //camera.Payload = 8000;
            camera.FrameReady += FrameReady;
            camera.Updates += Updates;
            camera.Gvcp.ElapsedOneSecond += UpdateFps;
        }

        private void Updates(object sender, string e)
        {
            list.Dispatcher.Invoke(() =>
            {
                list.Items.Add(e);
            });
        }

        private void UpdateFps(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Fps.Text = fpsCount.ToString();
                FrameNumber.Text = frameNumberDisplayed.ToString() + "/" + frameNumber.ToString() +
                " " + ((double)frameNumberDisplayed / frameNumber * 100.0).ToString("N2") + " %";
            }
            , System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            fpsCount = 0;
        }

        private ulong frameNumber;
        private ulong frameNumberDisplayed;

        private void FrameReady(object sender, byte[] e)
        {
            Dispatcher.Invoke(() => lightControl.RawBytes = e, System.Windows.Threading.DispatcherPriority.Send);
            fpsCount++;
            frameNumber = (ulong)sender;
            frameNumberDisplayed++;
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (camera.IsStreaming)
            {
                await camera.StopStream().ConfigureAwait(false);
            }
            else
            {
                width = (int)camera.Width;
                height = (int)camera.Height;
                Dispatcher.Invoke(() =>
                {
                    lightControl.WidthImage = width;
                    lightControl.HeightImage = height;
                    lightControl.IsColored = !camera.IsRawFrame;
                });
                await camera.StartStreamAsync().ConfigureAwait(false);
            }
        }
    }
}
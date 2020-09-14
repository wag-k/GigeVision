﻿using GigeVision.Core.Enums;
using GigeVision.Core.Interfaces;
using Stira.WpfCore;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GigeVision.Core.Models
{
    /// <summary>
    /// Camera class is responsible to initilize the stream and receive the stream
    /// </summary>
    public class Camera : BaseNotifyPropertyChanged, ICamera
    {
        /// <summary>
        /// Rx port
        /// </summary>
        public int port = 0;

        /// <summary>
        /// Raw bytes
        /// </summary>
        public byte[] rawBytes;

        /// <summary>
        /// frame ready action
        /// </summary>
        public Action<byte[]> frameReadyAction;

        private uint width, height, offsetX, offsetY, bytesPerPixel;
        private bool isStreaming;

        private IntPtr intPtr;

        private StreamReceiver streamReceiver;

        /// <summary>
        /// Camera constructor with initialized Gvcp Controller
        /// </summary>
        /// <param name="gvcp">GVCP Controller</param>
        public Camera(IGvcp gvcp)
        {
            Gvcp = gvcp;
            Task.Run(async () => await ReadParameters().ConfigureAwait(false));
            Init();
        }

        /// <summary>
        /// Default camera constructor initializes the controller
        /// </summary>
        public Camera()
        {
            Gvcp = new Gvcp();
            Init();
        }

        /// <summary>
        /// Camera stream status
        /// </summary>
        public bool IsStreaming { get => isStreaming; set { isStreaming = value; OnPropertyChanged(nameof(IsStreaming)); } }

        /// <summary>
        /// GVCP controller
        /// </summary>
        public IGvcp Gvcp { get; private set; }

        /// <summary>
        /// Event for frame ready
        /// </summary>
        public EventHandler<byte[]> FrameReady { get; set; }

        /// <summary>
        /// Event for general updates
        /// </summary>
        public EventHandler<string> Updates { get; set; }

        /// <summary>
        /// Payload size, if not provided it will be automatically set to one row, depending on resolution
        /// </summary>
        public uint Payload { get; set; } = 0;

        /// <summary>
        /// Camera width
        /// </summary>
        public uint Width
        {
            get => width;
            set
            {
                if (value != width)
                {
                    width = value;
                    OnPropertyChanged(nameof(Width));
                }
            }
        }

        /// <summary>
        /// Camera height
        /// </summary>
        public uint Height
        {
            get => height;
            set
            {
                if (value != height)
                {
                    height = value;
                    OnPropertyChanged(nameof(Height));
                }
            }
        }

        /// <summary>
        /// Camera offset X
        /// </summary>
        public uint OffsetX
        {
            get => offsetX;
            set
            {
                if (value != offsetX)
                {
                    offsetX = value;
                    OnPropertyChanged(nameof(OffsetX));
                }
            }
        }

        /// <summary>
        /// Camera offset Y
        /// </summary>
        public uint OffsetY
        {
            get => offsetY;
            set
            {
                if (value != offsetY)
                {
                    offsetY = value;
                    OnPropertyChanged(nameof(OffsetY));
                }
            }
        }

        /// <summary>
        /// Camera Pixel Format
        /// </summary>
        public PixelFormat PixelFormat { get; set; }

        /// <summary>
        /// Motor Controller for camera, zoom/focus/iris control if any
        /// </summary>
        public MotorControl MotorController { get; set; }

        /// <summary>
        /// Camera IP
        /// </summary>
        public string IP
        {
            get => Gvcp.CameraIp;
            set => Gvcp.CameraIp = value;
        }

        /// <summary>
        /// Multicast IP: it will be applied only when IsMulticast Property is true
        /// </summary>
        public string MulticastIP { get; set; } = "239.192.11.12";

        /// <summary>
        /// Multicast Option
        /// </summary>
        public bool IsMulticast { get; set; }

        /// <summary>
        /// Gets the raw data from the camera. Set false to get RGB frame instead of BayerGR8
        /// </summary>
        public bool IsRawFrame { get; set; } = true;

        /// <summary>
        /// If enabled library will use C++ native code for stream reception
        /// </summary>
        public bool IsUsingCppForRx { get; set; }

        /// <summary>
        /// This method will get current PC IP and Gets the Camera ip from Gvcp
        /// </summary>
        /// <param name="rxIP">If rxIP is not provided, method will detect system IP and use it</param>
        /// <param name="rxPort">It will set randomly when not provided</param>
        /// <param name="frameReady">If not null this event will be raised</param>
        /// <returns></returns>
        public async Task<bool> StartStreamAsync(string rxIP = null, int rxPort = 0, Action<byte[]> frameReady = null)
        {
            frameReadyAction = frameReady;
            if (string.IsNullOrEmpty(rxIP))
            {
                rxIP = GetMyIp();
            }
            if (IsMulticast)
            {
                rxIP = MulticastIP;
            }
            try
            {
                if (Gvcp.RegistersDictionary.Count == 0)
                {
                    await ReadParameters().ConfigureAwait(false);
                }
            }
            catch
            {
                if (Gvcp.RegistersDictionary.Count == 0)
                {
                    return false;
                }
            }
            if (rxPort == 0)
            {
                if (port == 0)
                {
                    port = new Random().Next(5000, 6000);
                }
            }
            else
            {
                port = rxPort;
            }
            if (Payload == 0)
            {
                CalculateSingleRowPayload();
            }
            if (Gvcp.RegistersDictionary.ContainsKey(nameof(RegisterName.AcquisitionStartReg)))
            {
                if (await Gvcp.TakeControl(true).ConfigureAwait(false))
                {
                    if (IsUsingCppForRx)
                    {
                        streamReceiver.StartRxCppThread();
                    }
                    else
                    {
                        streamReceiver.StartRxThread();
                    }
                    if ((await Gvcp.WriteRegisterAsync(GvcpRegister.SCPHostPort, (uint)port).ConfigureAwait(false)).Status == GvcpStatus.GEV_STATUS_SUCCESS)
                    {
                        await Gvcp.WriteRegisterAsync(GvcpRegister.SCDA, Converter.IpToNumber(rxIP)).ConfigureAwait(false);
                        await Gvcp.WriteRegisterAsync(GvcpRegister.SCPSPacketSize, Payload).ConfigureAwait(false);
                        string startReg = Gvcp.RegistersDictionary[nameof(RegisterName.AcquisitionStartReg)].Address;
                        if ((await Gvcp.WriteRegisterAsync(startReg, 1).ConfigureAwait(false)).Status == GvcpStatus.GEV_STATUS_SUCCESS)
                        {
                            IsStreaming = true;
                        }
                        else
                        {
                            await StopStream().ConfigureAwait(false);
                        }
                    }
                }
            }
            return IsStreaming;
        }

        /// <summary>
        /// Stops the camera stream and leave camera control
        /// </summary>
        /// <returns>Is streaming status</returns>
        public async Task<bool> StopStream()
        {
            if (IsUsingCppForRx)
            {
                if (Environment.Is64BitProcess)
                {
                    CvInterop64.Stop();
                }
                else
                {
                    CvInterop.Stop();
                }
            }

            await Gvcp.WriteRegisterAsync(GvcpRegister.SCDA, 0).ConfigureAwait(false);
            if (await Gvcp.LeaveControl().ConfigureAwait(false))
            {
                IsStreaming = false;
            }
            return IsStreaming;
        }

        /// <summary>
        /// Sets the resolution of camera
        /// </summary>
        /// <param name="width">Width to set</param>
        /// <param name="height">Height to set</param>
        /// <returns>Command Status</returns>
        public async Task<bool> SetResolutionAsync(uint width, uint height)
        {
            try
            {
                await Gvcp.TakeControl().ConfigureAwait(false);
                string[] registers = new string[2];
                registers[0] = Gvcp.RegistersDictionary[nameof(RegisterName.WidthReg)].Address;
                registers[1] = Gvcp.RegistersDictionary[nameof(RegisterName.HeightReg)].Address;
                uint[] valueToWrite = new uint[] { width, height };
                bool status = (await Gvcp.WriteRegisterAsync(registers, valueToWrite).ConfigureAwait(false)).Status == GvcpStatus.GEV_STATUS_SUCCESS;
                GvcpReply reply = await Gvcp.ReadRegisterAsync(registers).ConfigureAwait(false);
                if (reply.Status == GvcpStatus.GEV_STATUS_SUCCESS)
                {
                    Width = reply.RegisterValues[0];
                    Height = reply.RegisterValues[1];
                }
                await Gvcp.LeaveControl().ConfigureAwait(false);
                return status;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sets the resolution of camera
        /// </summary>
        /// <param name="width">Width to set</param>
        /// <param name="height">Height to set</param>
        /// <returns>Command Status</returns>
        public async Task<bool> SetResolutionAsync(int width, int height)
        {
            return await SetResolutionAsync((uint)width, (uint)height).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the offset of camera
        /// </summary>
        /// <param name="offsetX">Offset X to set</param>
        /// <param name="offsetY">Offset Y to set</param>
        /// <returns>Command Status</returns>
        public async Task<bool> SetOffsetAsync(int offsetX, int offsetY)
        {
            return await SetOffsetAsync((uint)offsetX, (uint)offsetY).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the offset of camera
        /// </summary>
        /// <returns>Command Status</returns>
        public async Task<bool> SetOffsetAsync()
        {
            return await SetOffsetAsync(OffsetX, OffsetY).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the Resolution of camera
        /// </summary>
        /// <returns>Command Status</returns>
        public async Task<bool> SetResolutionAsync()
        {
            return await SetResolutionAsync(Width, Height).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the offset of camera
        /// </summary>
        /// <param name="offsetX">Offset X to set</param>
        /// <param name="offsetY">Offset Y to set</param>
        /// <returns>Command Status</returns>
        public async Task<bool> SetOffsetAsync(uint offsetX, uint offsetY)
        {
            if (!IsStreaming)
            {
                await Gvcp.TakeControl().ConfigureAwait(false);
            }
            string[] registers = new string[2];
            registers[0] = Gvcp.RegistersDictionary[nameof(RegisterName.OffsetXReg)].Address;
            registers[1] = Gvcp.RegistersDictionary[nameof(RegisterName.OffsetYReg)].Address;
            uint[] valueToWrite = new uint[] { offsetX, offsetY };
            bool status = (await Gvcp.WriteRegisterAsync(registers, valueToWrite).ConfigureAwait(false)).Status == GvcpStatus.GEV_STATUS_SUCCESS;
            GvcpReply reply = await Gvcp.ReadRegisterAsync(registers).ConfigureAwait(false);
            if (reply.Status == GvcpStatus.GEV_STATUS_SUCCESS)
            {
                OffsetX = reply.RegisterValues[0];
                OffsetY = reply.RegisterValues[1];
            }
            if (!IsStreaming)
            {
                await Gvcp.LeaveControl().ConfigureAwait(false);
            }
            return status;
        }

        /// <summary>
        /// Bridge Command for motor controller, controls focus/zoom/iris operation
        /// </summary>
        /// <param name="command">Command to set</param>
        /// <param name="value">Value to set (Applicable for ZoomValue/FocusValue)</param>
        /// <returns>Command Status</returns>
        public async Task<bool> MotorControl(LensCommand command, uint value = 1)
        {
            return await MotorController.SendMotorCommand(Gvcp, command, value).ConfigureAwait(false);
        }

        /// <summary>
        /// Read register for camera
        /// </summary>
        /// <returns>Command Status</returns>
        public async Task<bool> ReadRegisters()
        {
            return await ReadParameters().ConfigureAwait(false);
        }

        private void Init()
        {
            MotorController = new MotorControl();
            streamReceiver = new StreamReceiver(this);
            Gvcp.CameraIpChanged += CameraIpChanged;
        }

        private void CalculateSingleRowPayload()
        {
            Payload = 8 + 28 + (Width * bytesPerPixel);
        }

        private async void CameraIpChanged(object sender, EventArgs e)
        {
            await ReadParameters().ConfigureAwait(false);
        }

        private async Task<bool> ReadParameters()
        {
            try
            {
                await Gvcp.ReadAllRegisterAddressFromCameraAsync().ConfigureAwait(false);
                if (Gvcp.RegistersDictionary.Count == 0)
                {
                    return false;
                }

                string[] registersToRead = new string[]
                {
                    Gvcp.RegistersDictionary[nameof(RegisterName.WidthReg)].Address,
                    Gvcp.RegistersDictionary[nameof(RegisterName.HeightReg)].Address,
                    Gvcp.RegistersDictionary[nameof(RegisterName.OffsetXReg)].Address,
                    Gvcp.RegistersDictionary[nameof(RegisterName.OffsetYReg)].Address,
                    Gvcp.RegistersDictionary[nameof(RegisterName.PixelFormatReg)].Address,
                };

                GvcpReply reply2 = await Gvcp.ReadRegisterAsync(registersToRead);
                if (reply2.Status == GvcpStatus.GEV_STATUS_SUCCESS)
                {
                    Width = reply2.RegisterValues[0];
                    Height = reply2.RegisterValues[1];
                    OffsetX = reply2.RegisterValues[2];
                    OffsetY = reply2.RegisterValues[3];
                    PixelFormat = (PixelFormat)reply2.RegisterValues[4];
                    bytesPerPixel = (uint)(reply2.Reply[reply2.Reply.Count - 3] / 8);
                    if (!IsRawFrame && PixelFormat.ToString().Contains("Bayer"))
                    {
                        rawBytes = new byte[Width * Height * 3];
                    }
                    else
                    {
                        rawBytes = new byte[Width * Height * bytesPerPixel];
                    }
                }
            }
            catch
            {
            }
            if (Gvcp.RegistersDictionary.Count > 0)
            {
                MotorController.CheckMotorControl(Gvcp.RegistersDictionary);
            }
            return true;
        }

        private string GetMyIp()
        {
            string localIP;
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                localIP = endPoint.Address.ToString();
            }
            return localIP;
        }
    }
}
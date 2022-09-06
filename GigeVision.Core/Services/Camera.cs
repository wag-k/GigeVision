﻿using GigeVision.Core.Enums;
using GigeVision.Core.Interfaces;
using Stira.WpfCore;
using System;
using System.Threading.Tasks;

namespace GigeVision.Core.Models
{
    /// <summary>
    /// Camera class is responsible to initialize the stream and receive the stream
    /// </summary>
    public class Camera : BaseNotifyPropertyChanged, ICamera
    {
        /// <summary>
        /// frame ready action
        /// </summary>
        public Action<byte[]> frameReadyAction;

        /// <summary>
        /// Raw bytes
        /// </summary>
        internal byte[] rawBytes;

        /// <summary>
        /// External buffer it has to set from externally using <see cref="SetBuffer(byte[])"/>
        /// </summary>
        internal IntPtr externalBuffer;

        private uint width, height, offsetX, offsetY, bytesPerPixel;

        private bool isStreaming;

        private IStreamReceiver streamReceiver;

        private bool isMulticast;
        private uint payload = 0;
        private string rxIP;
        private int portRx;

        /// <summary>
        /// Register dictionary of camera
        /// </summary>

        /// <summary>
        /// Camera constructor with initialized Gvcp Controller
        /// </summary>
        /// <param name="gvcp">GVCP Controller</param>
        public Camera(IGvcp gvcp)
        {
            Gvcp = gvcp;
            Task.Run(async () => await SyncParameters().ConfigureAwait(false));
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
        /// Rx port
        /// </summary>
        public int PortRx
        {
            get => portRx;
            set
            {
                if (portRx != value)
                {
                    portRx = value;
                    OnPropertyChanged(nameof(PortRx));
                }
            }
        }

        /// <summary>
        /// Camera stream status
        /// </summary>
        public bool IsStreaming
        { get => isStreaming; set { isStreaming = value; OnPropertyChanged(nameof(IsStreaming)); } }

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
        public uint Payload
        {
            get => payload;
            set
            {
                if (payload != value)
                {
                    payload = value;
                    OnPropertyChanged(nameof(Payload));
                }
            }
        }

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
            set
            {
                Gvcp.CameraIp = value;
                OnPropertyChanged(nameof(IP));
            }
        }

        /// <summary>
        /// Multi-Cast IP: it will be applied only when IsMulticast Property is true
        /// </summary>
        public string MulticastIP { get; set; } = "239.192.11.12";

        /// <summary>
        /// Multi-Cast Option
        /// </summary>
        public bool IsMulticast
        {
            get => isMulticast;
            set { isMulticast = value; OnPropertyChanged(nameof(IsMulticast)); }
        }

        /// <summary>
        /// Gets the raw data from the camera. Set false to get RGB frame instead of BayerGR8
        /// </summary>
        public bool IsRawFrame { get; set; } = true;

        /// <summary>
        /// If enabled library will use C++ native code for stream reception
        /// </summary>
        public bool IsUsingCppForRx { get; set; }

        /// <summary>
        /// If we set the external buffer using <see cref="SetBuffer(byte[])"/> this will be set
        /// true and software will copy stream on this buffer
        /// </summary>
        public bool IsUsingExternalBuffer { get; set; }

        public string RxIP
        {
            get => rxIP;
            set
            {
                if (rxIP != value)
                {
                    rxIP = value;
                    OnPropertyChanged(nameof(RxIP));
                }
            }
        }

        /// <summary>
        /// Tolernace for missing packet
        /// </summary>
        public int MissingPacketTolerance { get; set; } = 0;

        /// <summary>
        /// This method will get current PC IP and Gets the Camera ip from Gvcp
        /// </summary>
        /// <param name="rxIP">If rxIP is not provided, method will detect system IP and use it</param>
        /// <param name="rxPort">It will set randomly when not provided</param>
        /// <param name="frameReady">If not null this event will be raised</param>
        /// <returns></returns>
        public async Task<bool> StartStreamAsync(string rxIP = null, int rxPort = 0)
        {
            string ip2Send;
            if (string.IsNullOrEmpty(rxIP))
            {
                if (string.IsNullOrEmpty(RxIP) && !SetRxIP())
                {
                    return false;
                }
            }
            else
            {
                RxIP = rxIP;
            }
            ip2Send = RxIP;
            if (IsMulticast)
            {
                ip2Send = MulticastIP;
            }
            try
            {
                var status = await SyncParameters().ConfigureAwait(false);
                if (!status)
                    return status;
            }
            catch
            {
                return false;
            }
            if (rxPort == 0)
            {
                if (PortRx == 0)
                {
                    PortRx = new Random().Next(5000, 6000);
                }
            }
            else
            {
                PortRx = rxPort;
            }
            if (Payload == 0)
            {
                CalculateSingleRowPayload();
            }

            if (!IsUsingExternalBuffer)
            {
                SetRxBuffer();
            }
            if (Gvcp.RegistersDictionary.ContainsKey(nameof(RegisterName.AcquisitionStart)))
            {
                if (await Gvcp.TakeControl(true).ConfigureAwait(false))
                {
                    SetupRxThread();

                    if (((await Gvcp.RegistersDictionary[nameof(GvcpRegister.GevSCPHostPort)].SetValue((uint)PortRx).ConfigureAwait(false)) as GvcpReply).Status == GvcpStatus.GEV_STATUS_SUCCESS)
                    {
                        await Gvcp.RegistersDictionary[nameof(GvcpRegister.GevSCDA)].SetValue(Converter.IpToNumber(ip2Send)).ConfigureAwait(false);
                        await Gvcp.RegistersDictionary[nameof(GvcpRegister.GevSCPSPacketSize)].SetValue(Payload).ConfigureAwait(false);
                        await Gvcp.RegistersDictionary[nameof(RegisterName.AcquisitionStart)].SetValue(Payload).ConfigureAwait(false);
                        if (((await Gvcp.RegistersDictionary[nameof(RegisterName.AcquisitionStart)].SetValue(1).ConfigureAwait(false)) as GvcpReply).Status == GvcpStatus.GEV_STATUS_SUCCESS)
                        {
                            IsStreaming = true;
                        }
                        else
                        {
                            await StopStream().ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    if (IsMulticast)
                    {
                        SetupRxThread();
                        IsStreaming = true;
                    }
                }
            }
            else
            {
                Updates?.Invoke(this, "Fatal Error: Acquisition Start Register Not Found");
            }
            return IsStreaming;
        }

        /// <summary>
        /// Stops the camera stream and leave camera control
        /// </summary>
        /// <returns>Is streaming status</returns>
        public async Task<bool> StopStream()
        {
            await Gvcp.WriteRegisterAsync(GvcpRegister.GevSCDA, 0).ConfigureAwait(false);
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
                GvcpReply widthWriteReply = (await Gvcp.RegistersDictionary[nameof(RegisterName.Width)].SetValue(width).ConfigureAwait(false)) as GvcpReply;
                GvcpReply heightWriteReply = (await Gvcp.RegistersDictionary[nameof(RegisterName.Height)].SetValue(width).ConfigureAwait(false)) as GvcpReply;

                await Gvcp.RegistersDictionary[nameof(RegisterName.Height)].SetValue(height).ConfigureAwait(false);
                bool status = (widthWriteReply.Status == GvcpStatus.GEV_STATUS_SUCCESS && heightWriteReply.Status == GvcpStatus.GEV_STATUS_SUCCESS);
                if (status)
                {
                    long newWidth = (await Gvcp.RegistersDictionary[nameof(RegisterName.Width)].GetValue().ConfigureAwait(false));
                    long newHeight = (await Gvcp.RegistersDictionary[nameof(RegisterName.Height)].GetValue().ConfigureAwait(false));

                    Width = (uint)newWidth;
                    Height = (uint)newHeight;
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

        //ToDo: Error handle the following method.
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
            var offsetXStatus = ((await Gvcp.RegistersDictionary[nameof(RegisterName.OffsetX)].SetValue(offsetX).ConfigureAwait(false)) as GvcpReply).Status == GvcpStatus.GEV_STATUS_SUCCESS;
            var offsetYStatus = ((await Gvcp.RegistersDictionary[nameof(RegisterName.OffsetY)].SetValue(offsetY).ConfigureAwait(false)) as GvcpReply).Status == GvcpStatus.GEV_STATUS_SUCCESS;
            if (offsetYStatus && offsetYStatus)
            {
                OffsetX = (uint)await Gvcp.RegistersDictionary[nameof(RegisterName.OffsetX)].GetValue().ConfigureAwait(false);
                OffsetY = (uint)await Gvcp.RegistersDictionary[nameof(RegisterName.OffsetY)].GetValue().ConfigureAwait(false);
            }

            if (!IsStreaming)
            {
                await Gvcp.LeaveControl().ConfigureAwait(false);
            }
            return offsetYStatus && offsetXStatus;
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
            return await SyncParameters().ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the buffer from external source
        /// </summary>
        /// <param name="externalRawBytes"></param>
        public void SetBuffer(byte[] externalRawBytes)
        {
            if (!IsStreaming && externalRawBytes != default)
            {
                if (rawBytes != null)
                {
                    Array.Clear(rawBytes, 0, rawBytes.Length);
                }

                rawBytes = externalRawBytes;
                IsUsingExternalBuffer = true;
            }
        }

        /// <summary>
        /// It reads all the parameters from the camera
        /// </summary>
        /// <returns></returns>
        public async Task<bool> SyncParameters(int syncAttempts = 1)
        {
            try
            {
                if (Gvcp.IsLoadingXml)
                    return false;

                if (Gvcp.RegistersDictionary?.Count > 0)
                {
                    Width = (uint)await Gvcp.RegistersDictionary[nameof(RegisterName.Width)].GetValue().ConfigureAwait(false);
                    Height = (uint)await Gvcp.RegistersDictionary[nameof(RegisterName.Height)].GetValue().ConfigureAwait(false);
                    OffsetX = (uint)await Gvcp.RegistersDictionary[nameof(RegisterName.OffsetX)].GetValue().ConfigureAwait(false);
                    OffsetY = (uint)await Gvcp.RegistersDictionary[nameof(RegisterName.OffsetY)].GetValue().ConfigureAwait(false);
                    PixelFormat = (PixelFormat)(uint)await Gvcp.RegistersDictionary[nameof(RegisterName.PixelFormat)].GetValue().ConfigureAwait(false);
                    bytesPerPixel = 3;
                }
                else
                {
                    for (int i = 0; i < syncAttempts; i++)
                    {
                        if (Gvcp.RegistersDictionary?.Count > 0)
                        {
                            await SyncParameters(0);
                            return true;
                        }
                        await Gvcp.ReadAllRegisterAddressFromCameraAsync().ConfigureAwait(false);
                        await SyncParameters(0);
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                Updates?.Invoke(this, ex.Message);
            }
            return true;
        }

        private bool SetRxIP()
        {
            try
            {
                string ip = NetworkService.GetMyIp();
                if (string.IsNullOrEmpty(ip))
                {
                    return false;
                }
                RxIP = ip;
                return true;
            }
            catch (Exception ex)
            {
                Updates?.Invoke(this, ex.Message);
                return false;
            }
        }

        private void SetupRxThread()
        {
            streamReceiver.StartRxThread();
        }

        private void SetRxBuffer()
        {
            if (rawBytes != null)
            {
                Array.Clear(rawBytes, 0, rawBytes.Length);
            }

            if (!IsRawFrame && PixelFormat.ToString().Contains("Bayer"))
            {
                rawBytes = new byte[Width * Height * 3];
            }
            else
            {
                rawBytes = new byte[Width * Height * bytesPerPixel];
            }
        }

        private void Init()
        {
            MotorController = new MotorControl();
            streamReceiver = new StreamReceiverPipeline(this);
            SetRxIP();
            Gvcp.CameraIpChanged += CameraIpChanged;
        }

        private void CalculateSingleRowPayload()
        {
            Payload = 8 + 28 + (Width * bytesPerPixel);
        }

        private async void CameraIpChanged(object sender, EventArgs e)
        {
            Gvcp.RegistersDictionary?.Clear();
            await SyncParameters().ConfigureAwait(false);
        }
    }
}
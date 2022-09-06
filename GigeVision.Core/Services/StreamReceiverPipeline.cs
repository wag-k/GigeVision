using GigeVision.Core.Enums;
using GigeVision.Core.Interfaces;
using Microsoft.Toolkit.HighPerformance.Buffers;
using Stira.WpfCore;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GigeVision.Core.Models
{
    /// <summary>
    /// Receives the stream and switch the buffer when sending the data
    /// </summary>
    public class StreamReceiverPipeline : BaseNotifyPropertyChanged, IStreamReceiver
    {
        private readonly Camera Camera;
        private Socket socketRxRaw;
        private bool isDecodingAsVersion2;

        /// <summary>
        /// Receives the GigeStream
        /// </summary>
        /// <param name="camera"></param>
        public StreamReceiverPipeline(Camera camera)
        {
            Camera = camera;
            GvspInfo = new GvspInfo();
        }

        public GvspInfo GvspInfo { get; }

        /// <summary>
        /// If software read the GVSP stream as version 2
        /// </summary>
        public bool IsDecodingAsVersion2
        {
            get => isDecodingAsVersion2;
            set
            {
                if (isDecodingAsVersion2 != value)
                {
                    isDecodingAsVersion2 = value;
                    OnPropertyChanged(nameof(IsDecodingAsVersion2));
                }
            }
        }

        /// <summary>
        /// Start Rx thread using .Net
        /// </summary>
        public void StartRxThread()
        {
            //SetupSocketRxRaw();
            //DecodePacketsRawSocket();
            //return;
            Thread threadDecode = new(DecodePacketsRawSocket)
            {
                Priority = ThreadPriority.Highest,
                Name = "Decode Packets Thread",
                IsBackground = true
            };
            SetupSocketRxRaw();
            threadDecode.Start();
        }

        private void SetupSocketRxRaw()
        {
            try
            {
                if (socketRxRaw != null)
                {
                    socketRxRaw.Close();
                    socketRxRaw.Dispose();
                }
                socketRxRaw = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socketRxRaw.Bind(new IPEndPoint(IPAddress.Any, Camera.PortRx));
                if (Camera.IsMulticast)
                {
                    MulticastOption mcastOption = new(IPAddress.Parse(Camera.MulticastIP), IPAddress.Parse(Camera.RxIP));
                    socketRxRaw.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, mcastOption);
                }
                socketRxRaw.ReceiveTimeout = 1000;
                socketRxRaw.ReceiveBufferSize = (int)(Camera.Payload * Camera.Height * 5);
            }
            catch (Exception ex)
            {
                Camera.Updates?.Invoke(UpdateType.ConnectionIssue, ex.Message);
            }
        }

        private async void DecodePacketsRawSocket()
        {
            DetectGvspType();
            var options = new PipeOptions(minimumSegmentSize: 9000);
            var decodingPipeline = new Pipe(options);

            var readingPipe = ReceiverPipe(socketRxRaw, decodingPipeline.Writer);
            var decoderPipe = DecoderPipe(decodingPipeline.Reader);
            await Task.WhenAll(readingPipe, decoderPipe);
        }

        private async Task DecoderPipe(PipeReader reader)
        {
            int i;
            byte[] buffer = GC.AllocateArray<byte>(length: Camera.rawBytes.Length, pinned: true);

            //byte[][] buffer = new byte[2][];
            //buffer[0] = new byte[Camera.rawBytes.Length];
            //buffer[1] = new byte[Camera.rawBytes.Length];
            int bufferIndex = 0;
            int packetID = 0, bufferStart;
            int packetRxCount = 0;
            var listOfPacketIDs = new List<int>();
            var packetSize = GvspInfo.PayloadSize + GvspInfo.PayloadOffset;
            SequencePosition sequencePosition;
            while (Camera.IsStreaming)
            {
                ReadResult dataBuffer = await reader.ReadAsync();

                for (i = 0; i < dataBuffer.Buffer.Length - packetSize; i += packetSize)
                {
                    var singlePacket = dataBuffer.Buffer.Slice(i, packetSize);
                    if (singlePacket.FirstSpan.Slice(4, 1)[0] == GvspInfo.DataIdentifier) //Packet
                    {
                        packetRxCount++;
                        packetID = (singlePacket.FirstSpan.Slice(GvspInfo.PacketIDIndex, 1)[0] << 8) | singlePacket.FirstSpan.Slice(GvspInfo.PacketIDIndex + 1, 1)[0];
                        bufferStart = (packetID - 1) * GvspInfo.PayloadSize; //This use buffer length of regular packet
                        listOfPacketIDs.Add(packetID);
                        //singlePacket.Slice(GvspInfo.PayloadOffset, GvspInfo.PayloadSize).CopyTo(buffer.AsSpan().Slice(bufferStart, GvspInfo.PayloadSize));
                    }
                    if (packetID == GvspInfo.FinalPacketID)
                    {
                        //if (packetID - packetRxCount < 5)
                        {
                            // Camera.FrameReady?.Invoke((ulong)1, buffer);
                            bufferIndex = bufferIndex == 1 ? 0 : 1;
                            listOfPacketIDs.Clear();
                        }
                        packetRxCount = 0;
                    }
                }
                sequencePosition = dataBuffer.Buffer.GetPosition(dataBuffer.Buffer.Length);
                reader.AdvanceTo(sequencePosition);
            }
        }

        private async Task ReceiverPipe(Socket socketRxRaw, PipeWriter writer)
        {
            int count = 0;
            while (Camera.IsStreaming)
            {
                var spanPacket = writer.GetMemory(9000);
                int length = socketRxRaw.Receive(spanPacket.Span);
                //int length = await socketRxRaw.ReceiveAsync(spanPacket, SocketFlags.None);
                if (length > 100)
                {
                    writer.Advance(length);
                    if (count++ >= 8)
                    {
                        await writer.FlushAsync();
                        count = 0;
                    }
                }
            }
        }

        private void DetectGvspType()
        {
            Span<byte> singlePacket = new byte[9000];
            socketRxRaw.Receive(singlePacket);
            IsDecodingAsVersion2 = ((singlePacket[4] & 0xF0) >> 4) == 8;

            GvspInfo.BlockIDIndex = IsDecodingAsVersion2 ? 8 : 2;
            GvspInfo.BlockIDLength = IsDecodingAsVersion2 ? 8 : 2;
            GvspInfo.PacketIDIndex = IsDecodingAsVersion2 ? 18 : 6;
            GvspInfo.PayloadOffset = IsDecodingAsVersion2 ? 20 : 8;
            GvspInfo.TimeStampIndex = IsDecodingAsVersion2 ? 24 : 12;
            GvspInfo.DataIdentifier = IsDecodingAsVersion2 ? 0x83 : 0x03;
            GvspInfo.DataEndIdentifier = IsDecodingAsVersion2 ? 0x82 : 0x02;

            var packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
            if (packetID == 0)
            {
                GvspInfo.IsImageData = ((singlePacket[10] << 8) | singlePacket[11]) == 1;
                if (GvspInfo.IsImageData)
                {
                    GvspInfo.Width = (singlePacket[24] << 24) | (singlePacket[25] << 16) | (singlePacket[26] << 8) | (singlePacket[27]);
                    GvspInfo.Height = (singlePacket[28] << 24) | (singlePacket[29] << 16) | (singlePacket[30] << 8) | (singlePacket[31]);
                }
            }

            //Optimizing the array length for receive buffer
            int length = socketRxRaw.Receive(singlePacket);
            packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
            if (packetID > 0)
            {
                GvspInfo.PacketLength = length;
            }
            Camera.IsStreaming = length > 10;
            GvspInfo.PayloadSize = GvspInfo.PacketLength - GvspInfo.PayloadOffset;

            if (GvspInfo.Width > 0 && GvspInfo.Height > 0) //Now we can calculate the final packet ID
            {
                var totalBytesExpectedForOneFrame = GvspInfo.Width * GvspInfo.Height;
                GvspInfo.FinalPacketID = totalBytesExpectedForOneFrame / GvspInfo.PayloadSize;
                if (totalBytesExpectedForOneFrame % GvspInfo.PayloadSize != 0)
                {
                    GvspInfo.FinalPacketID++;
                }
            }
        }
    }
}
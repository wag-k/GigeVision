using GigeVision.Core.Enums;
using Microsoft.Toolkit.HighPerformance;
using Microsoft.Toolkit.HighPerformance.Buffers;
using Stira.WpfCore;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GigeVision.Core.Models
{
    /// <summary>
    /// Receives the stream
    /// </summary>
    public class StreamReceiver : BaseNotifyPropertyChanged
    {
        private readonly Camera Camera;
        private Socket socketRxRaw;
        private bool isDecodingAsVersion2;

        /// <summary>
        /// Receives the GigeStream
        /// </summary>
        /// <param name="camera"></param>
        public StreamReceiver(Camera camera)
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

        private void DecodePacketsRawSocket()
        {
            int packetID = 0, bufferIndex = 0, bufferLength = 0, bufferStart = 0, length = 0, packetRxCount = 1, packetRxCountClone, bufferIndexClone;
            ulong imageID, lastImageID = 0, lastImageIDClone, deltaImageID;
            byte[] blockID;
            byte[][] buffer = new byte[2][];
            buffer[0] = new byte[Camera.rawBytes.Length];
            buffer[1] = new byte[Camera.rawBytes.Length];
            int frameCounter = 0;
            try
            {
                DetectGvspType();
                Span<byte> singlePacket = stackalloc byte[GvspInfo.PacketLength];

                while (Camera.IsStreaming)
                {
                    length = socketRxRaw.Receive(singlePacket);
                    if (singlePacket[4] == GvspInfo.DataIdentifier) //Packet
                    {
                        packetRxCount++;
                        packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                        bufferStart = (packetID - 1) * GvspInfo.PayloadSize; //This use buffer length of regular packet
                        bufferLength = length - GvspInfo.PayloadOffset;  //This will only change for final packet
                        singlePacket.Slice(GvspInfo.PayloadOffset, bufferLength).CopyTo(buffer[bufferIndex].AsSpan().Slice(bufferStart, bufferLength));
                        continue;
                    }
                    if (singlePacket[4] == GvspInfo.DataEndIdentifier)
                    {
                        if (GvspInfo.FinalPacketID == 0)
                        {
                            packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                            GvspInfo.FinalPacketID = packetID - 1;
                        }

                        blockID = singlePacket.Slice(GvspInfo.BlockIDIndex, GvspInfo.BlockIDLength).ToArray();
                        Array.Reverse(blockID);
                        Array.Resize(ref blockID, 8);
                        imageID = BitConverter.ToUInt64(blockID);
                        packetRxCountClone = packetRxCount;
                        lastImageIDClone = lastImageID;
                        bufferIndexClone = bufferIndex;
                        bufferIndex = bufferIndex == 0 ? 1 : 0; //Swaping buffer
                        packetRxCount = 0;
                        lastImageID = imageID;

                        Task.Run(() =>
                        {
                            //Checking if we receive all packets
                            if (Math.Abs(packetRxCountClone - GvspInfo.FinalPacketID) <= Camera.MissingPacketTolerance)
                            {
                                ++frameCounter;
                                Camera.FrameReady?.Invoke(imageID, buffer[bufferIndex]);
                            }
                            else
                            {
                                Camera.Updates?.Invoke(UpdateType.FrameLoss, $"Image tx skipped because of {packetRxCountClone - GvspInfo.FinalPacketID} packet loss");
                            }

                            deltaImageID = imageID - lastImageIDClone;
                            //This <10000 is just to skip the overflow value when the counter (2 or 8 bytes) will complete it should not show false missing images
                            if (deltaImageID != 1 && deltaImageID < 10000)
                            {
                                Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID - lastImageIDClone - 1} Image missed between {lastImageIDClone}-{imageID}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (Camera.IsStreaming) // We didn't delibrately stop the stream
                {
                    Camera.Updates?.Invoke(UpdateType.StreamStopped, ex.Message);
                }
                _ = Camera.StopStream();
            }
        }

        private void DecodePacketsRawSocket21()
        {
            int packetID = 0, bufferIndex = 0, bufferLength = 0, bufferStart = 0, length = 0, packetRxCount = 1, packetRxCountClone, bufferIndexClone;
            ulong imageID, lastImageID = 0, lastImageIDClone, deltaImageID;
            byte[] blockID;
            buffer = MemoryOwner<byte>.Allocate(Camera.rawBytes.Length * 2);
            int frameCounter = 0;
            try
            {
                DetectGvspType();
                Span<byte> singlePacket = stackalloc byte[GvspInfo.PacketLength];

                while (Camera.IsStreaming)
                {
                    length = socketRxRaw.Receive(singlePacket);
                    if (singlePacket[4] == GvspInfo.DataIdentifier) //Packet
                    {
                        packetRxCount++;
                        packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                        bufferStart = (bufferIndex * Camera.rawBytes.Length) + (packetID - 1) * GvspInfo.PayloadSize; //This use buffer length of regular packet
                        bufferLength = length - GvspInfo.PayloadOffset;  //This will only change for final packet
                        singlePacket.Slice(GvspInfo.PayloadOffset, bufferLength).CopyTo(buffer.Span.Slice(bufferStart, bufferLength));
                        continue;
                    }
                    if (singlePacket[4] == GvspInfo.DataEndIdentifier)
                    {
                        if (GvspInfo.FinalPacketID == 0)
                        {
                            packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                            GvspInfo.FinalPacketID = packetID - 1;
                        }

                        blockID = singlePacket.Slice(GvspInfo.BlockIDIndex, GvspInfo.BlockIDLength).ToArray();
                        Array.Reverse(blockID);
                        Array.Resize(ref blockID, 8);
                        imageID = BitConverter.ToUInt64(blockID);
                        packetRxCountClone = packetRxCount;
                        lastImageIDClone = lastImageID;
                        bufferIndexClone = bufferIndex;
                        bufferIndex = bufferIndex == 0 ? 1 : 0; //Moving pointer
                        packetRxCount = 0;
                        lastImageID = imageID;

                        Task.Run(() =>
                        {
                            //Checking if we receive all packets
                            if (Math.Abs(packetRxCountClone - GvspInfo.FinalPacketID) <= Camera.MissingPacketTolerance)
                            {
                                ++frameCounter;
                                Task.Run(() =>
                                {
                                    Camera.FrameReady?.Invoke(imageID, buffer.DangerousGetArray().Slice(bufferIndexClone * Camera.rawBytes.Length, Camera.rawBytes.Length).Array);
                                });
                            }
                            else
                            {
                                Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID} Image tx skipped because of {packetRxCountClone - GvspInfo.FinalPacketID} packet loss");
                            }

                            deltaImageID = imageID - lastImageIDClone;
                            //This <10000 is just to skip the overflow value when the counter (2 or 8 bytes) will complete it should not show false missing images
                            if (deltaImageID != 1 && deltaImageID < 10000)
                            {
                                Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID - lastImageIDClone - 1} Image missed between {lastImageIDClone}-{imageID}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (Camera.IsStreaming) // We didn't delibrately stop the stream
                {
                    Camera.Updates?.Invoke(UpdateType.StreamStopped, ex.Message);
                }
                _ = Camera.StopStream();
            }
        }

        private void DecodePacketsRawSocket90()
        {
            int packetID = 0, bufferIndex = 0, bufferLength = 0, bufferStart = 0, length = 0, packetRxCount = 1, packetRxCountClone, bufferIndexClone;
            ulong imageID, lastImageID = 0, lastImageIDClone, deltaImageID;
            byte[] blockID;
            int frameCounter = 0;
            List<int> packetIDList = new();
            try
            {
                using MemoryOwner<byte> buffer = MemoryOwner<byte>.Allocate(Camera.rawBytes.Length);
                DetectGvspType();
                Span<byte> singlePacket = stackalloc byte[GvspInfo.PacketLength];
                while (Camera.IsStreaming)
                {
                    length = socketRxRaw.Receive(singlePacket);
                    if (singlePacket[4] == GvspInfo.DataIdentifier) //Packet
                    {
                        packetRxCount++;
                        packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                        bufferStart = (packetID - 1) * GvspInfo.PayloadSize; //This use buffer length of regular packet
                        bufferLength = length - GvspInfo.PayloadOffset;  //This will only change for final packet
                        singlePacket.Slice(GvspInfo.PayloadOffset, bufferLength).CopyTo(buffer.Span.Slice(bufferStart, bufferLength));
                        packetIDList.Add(packetID);
                        continue;
                    }
                    if (singlePacket[4] == GvspInfo.DataEndIdentifier)
                    {
                        if (GvspInfo.FinalPacketID == 0)
                        {
                            packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                            GvspInfo.FinalPacketID = packetID - 1;
                        }

                        blockID = singlePacket.Slice(GvspInfo.BlockIDIndex, GvspInfo.BlockIDLength).ToArray();
                        Array.Reverse(blockID);
                        Array.Resize(ref blockID, 8);
                        imageID = BitConverter.ToUInt64(blockID);
                        packetRxCountClone = packetRxCount;
                        lastImageIDClone = lastImageID;
                        bufferIndexClone = bufferIndex;
                        bufferIndex = bufferIndex == 0 ? 1 : 0; //Swaping buffer
                        packetRxCount = 0;
                        lastImageID = imageID;

                        Task.Run(() => //Send the image ready signal parallel, without breaking the reception
                        {
                            //Checking if we receive all packets
                            if (Math.Abs(packetRxCountClone - GvspInfo.FinalPacketID) <= Camera.MissingPacketTolerance)
                            {
                                ++frameCounter;
                                Camera.FrameReady?.Invoke(imageID, buffer.DangerousGetArray().Array);
                            }
                            else
                            {
                                Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID} Frame tx skipped because of {GvspInfo.FinalPacketID - packetRxCountClone} packet loss");
                            }

                            deltaImageID = imageID - lastImageIDClone;
                            //This <10000 is just to skip the overflow value when the counter (2 or 8 bytes) will complete it should not show false missing images
                            if (deltaImageID != 1 && deltaImageID < 10000)
                            {
                                Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID - lastImageIDClone - 1} Image missed between {lastImageIDClone}-{imageID}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (Camera.IsStreaming) // We didn't delibrately stop the stream
                {
                    Camera.Updates?.Invoke(UpdateType.StreamStopped, ex.Message);
                }
                _ = Camera.StopStream();
            }
        }

        private int bufferIndex = 0, singlePacketBufferIndex = 0, packetRxCountClone, bufferIndexClone;
        private ulong imageID, lastImageID = 0, lastImageIDClone, deltaImageID;
        private int frameCounter = 0;
        private MemoryOwner<byte> buffer;

        private void DecodePacketsRawSocket2()
        {
            int length;
            List<int> packetIDList = new();
            try
            {
                DetectGvspType();
                buffer = MemoryOwner<byte>.Allocate(Camera.rawBytes.Length);
                var bufferSinglePacket2 = MemoryOwner<byte>.Allocate(Camera.rawBytes.Length);
                int totalBuffers = 100;
                byte[][] bufferSinglePacket = new byte[totalBuffers][];
                //Span<byte> bufferSinglePacket = new Span<byte>();

                for (int i = 0; i < totalBuffers; i++)
                {
                    bufferSinglePacket[i] = new byte[GvspInfo.PacketLength];
                }

                while (Camera.IsStreaming)
                {
                    length = socketRxRaw.Receive(bufferSinglePacket[singlePacketBufferIndex]);
                    PlaceInImageMemory(bufferSinglePacket[singlePacketBufferIndex]);
                    if (singlePacketBufferIndex > totalBuffers - 5)
                    {
                        singlePacketBufferIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                if (Camera.IsStreaming) // We didn't delibrately stop the stream
                {
                    Camera.Updates?.Invoke(UpdateType.StreamStopped, ex.Message);
                }
                _ = Camera.StopStream();
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private void PlaceInImageMemory(Span<byte> singlePacket)
        {
            int packetID, bufferStart, bufferLength = 0;
            if (singlePacket[4] == GvspInfo.DataIdentifier) //Packet
            {
                packetRxCount++;
                packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                bufferStart = (packetID - 1) * GvspInfo.PayloadSize; //This use buffer length of regular packet
                bufferLength = singlePacket.Length - GvspInfo.PayloadOffset;  //This will only change for final packet
                singlePacket.Slice(GvspInfo.PayloadOffset, bufferLength).CopyTo(buffer.Span.Slice(bufferStart, bufferLength));
                return;
            }
            if (singlePacket[4] == GvspInfo.DataEndIdentifier)
            {
                if (GvspInfo.FinalPacketID == 0)
                {
                    packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                    GvspInfo.FinalPacketID = packetID - 1;
                }

                var blockID = singlePacket.Slice(GvspInfo.BlockIDIndex, GvspInfo.BlockIDLength).ToArray();
                Array.Reverse(blockID);
                Array.Resize(ref blockID, 8);
                imageID = BitConverter.ToUInt64(blockID);
                packetRxCountClone = packetRxCount;
                lastImageIDClone = lastImageID;
                bufferIndexClone = bufferIndex;
                bufferIndex = bufferIndex == 0 ? 1 : 0; //Swaping buffer
                packetRxCount = 0;
                lastImageID = imageID;

                //Checking if we receive all packets
                if (Math.Abs(packetRxCountClone - GvspInfo.FinalPacketID) <= Camera.MissingPacketTolerance)
                {
                    ++frameCounter;
                    Task.Run(() =>
                    {
                        Camera.FrameReady?.Invoke(imageID, buffer.DangerousGetArray().Array);
                    });
                }
                else
                {
                    Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID} Frame tx skipped because of {GvspInfo.FinalPacketID - packetRxCountClone} packet loss");
                }

                deltaImageID = imageID - lastImageIDClone;
                //This <10000 is just to skip the overflow value when the counter (2 or 8 bytes) will complete it should not show false missing images
                if (deltaImageID != 1 && deltaImageID < 10000)
                {
                    Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID - lastImageIDClone - 1} Image missed between {lastImageIDClone}-{imageID}");
                }
            }
        }

        private List<int> packetIDList;
        private int packetRxCount = 1;

        private void PlaceInImageMemory2(Span<byte> multiPacket)
        {
            int i;
            int packetID, bufferStart, bufferLength = 0;
            int packetRxCount = 0;
            Span<byte> singlePacket;
            for (i = 0; i < multiPacket.Length - (GvspInfo.PayloadSize + GvspInfo.PayloadOffset); i += (GvspInfo.PayloadSize + GvspInfo.PayloadOffset))
            {
                singlePacket = multiPacket.Slice(i, GvspInfo.PayloadSize + GvspInfo.PayloadOffset);
                if (singlePacket[4] == GvspInfo.DataIdentifier) //Packet
                {
                    packetRxCount++;
                    packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                    bufferStart = (packetID - 1) * GvspInfo.PayloadSize; //This use buffer length of regular packet
                    bufferLength = singlePacket.Length - GvspInfo.PayloadOffset;  //This will only change for final packet
                    singlePacket.Slice(GvspInfo.PayloadOffset, bufferLength).CopyTo(buffer2.AsSpan().Slice(bufferStart, bufferLength));
                }
            }
            if (multiPacket.Length - i - 16 > 0) // Last Packet
            {
                var lastPacketLength = multiPacket.Length - i - 16;
                var lastPacket = multiPacket.Slice(i, lastPacketLength);
                packetID = (lastPacket[GvspInfo.PacketIDIndex] << 8) | lastPacket[GvspInfo.PacketIDIndex + 1];
                bufferStart = (packetID - 1) * GvspInfo.PayloadSize;
                lastPacket.Slice(GvspInfo.PayloadOffset, lastPacketLength - GvspInfo.PayloadOffset).CopyTo(buffer2.AsSpan().Slice(bufferStart, lastPacketLength - GvspInfo.PayloadOffset));
                i = multiPacket.Length - 16;
            }
            singlePacket = multiPacket.Slice(i);
            if (singlePacket[4] == GvspInfo.DataEndIdentifier)
            {
                if (GvspInfo.FinalPacketID == 0)
                {
                    packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
                    GvspInfo.FinalPacketID = packetID - 1;
                }

                var blockID = singlePacket.Slice(GvspInfo.BlockIDIndex, GvspInfo.BlockIDLength).ToArray();
                Array.Reverse(blockID);
                Array.Resize(ref blockID, 8);
                imageID = BitConverter.ToUInt64(blockID);
                packetRxCountClone = packetRxCount;
                lastImageIDClone = lastImageID;
                bufferIndexClone = bufferIndex;
                bufferIndex = bufferIndex == 0 ? 1 : 0; //Swaping buffer
                packetRxCount = 0;
                lastImageID = imageID;

                //Checking if we receive all packets
                if (Math.Abs(packetRxCountClone - GvspInfo.FinalPacketID) <= Camera.MissingPacketTolerance + 1)
                {
                    ++frameCounter;
                    Task.Run(() =>
                    {
                        Camera.FrameReady?.Invoke(imageID, buffer2);
                    });
                }
                else
                {
                    Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID} Frame tx skipped because of {GvspInfo.FinalPacketID - packetRxCountClone} packet loss");
                }

                deltaImageID = imageID - lastImageIDClone;
                //This <10000 is just to skip the overflow value when the counter (2 or 8 bytes) will complete it should not show false missing images
                if (deltaImageID != 1 && deltaImageID < 10000)
                {
                    Camera.Updates?.Invoke(UpdateType.FrameLoss, $"{imageID - lastImageIDClone - 1} Image missed between {lastImageIDClone}-{imageID}");
                }
            }
        }

        private byte[] buffer2;

        private async void DecodePacketsRawSocket456()
        {
            try
            {
                DetectGvspType();
                buffer2 = GC.AllocateArray<byte>(length: Camera.rawBytes.Length, pinned: true);
                Memory<byte> bufferMem = buffer2.AsMemory();
                buffer = MemoryOwner<byte>.Allocate(Camera.rawBytes.Length);
                Memory<byte> bufferSinglePacket1 = new byte[Camera.rawBytes.Length * 4]; // MemoryOwner<byte>.Allocate(Camera.rawBytes.Length);

                int totalBuffers = 100;
                byte[][] bufferSinglePacket = new byte[totalBuffers][];
                //Span<byte> bufferSinglePacket = new Span<byte>();

                for (int i = 0; i < totalBuffers; i++)
                {
                    bufferSinglePacket[i] = new byte[GvspInfo.PacketLength];
                }
                int startIndex = 0;
                int bytesReceived = 0;
                packetIDList = new();
                bool togglePointer = false;
                while (Camera.IsStreaming)
                {
                    var reply = await socketRxRaw.ReceiveFromAsync(bufferSinglePacket1.Slice(startIndex + bytesReceived), SocketFlags.None, new IPEndPoint(IPAddress.Any, Camera.PortRx));
                    if (reply.ReceivedBytes > 100)
                    {
                        bytesReceived += reply.ReceivedBytes;
                    }
                    if (reply.ReceivedBytes == 16)
                    {
                        var totalBytes = bytesReceived + reply.ReceivedBytes;
                        //packetIDList.Add(totalBytes);
                        var index = togglePointer ? Camera.rawBytes.Length * 2 : 0;
                        togglePointer = !togglePointer;
                        startIndex = togglePointer ? Camera.rawBytes.Length * 2 : 0;
                        bytesReceived = 0;
                        _ = Task.Run(() =>
                        {
                            PlaceInImageMemory2(bufferSinglePacket1.Slice(index, totalBytes).Span);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (Camera.IsStreaming) // We didn't delibrately stop the stream
                {
                    Camera.Updates?.Invoke(UpdateType.StreamStopped, ex.Message);
                }
                _ = Camera.StopStream();
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private void DetectGvspType()
        {
            Span<byte> singlePacket = new byte[10000];
            socketRxRaw.Receive(singlePacket);
            IsDecodingAsVersion2 = ((singlePacket[4] & 0xF0) >> 4) == 8;

            GvspInfo.BlockIDIndex = IsDecodingAsVersion2 ? 8 : 2;
            GvspInfo.BlockIDLength = IsDecodingAsVersion2 ? 8 : 2;
            GvspInfo.PacketIDIndex = IsDecodingAsVersion2 ? 18 : 6;
            GvspInfo.PayloadOffset = IsDecodingAsVersion2 ? 20 : 8;
            GvspInfo.TimeStampIndex = IsDecodingAsVersion2 ? 24 : 12;
            GvspInfo.DataIdentifier = IsDecodingAsVersion2 ? 0x83 : 0x03;
            GvspInfo.DataEndIdentifier = IsDecodingAsVersion2 ? 0x82 : 0x02;

            //Optimizing the array length for receive buffer
            int length = socketRxRaw.Receive(singlePacket);
            int packetID = (singlePacket[GvspInfo.PacketIDIndex] << 8) | singlePacket[GvspInfo.PacketIDIndex + 1];
            if (packetID > 0)
            {
                GvspInfo.PacketLength = length;
            }
            Camera.IsStreaming = length > 10;
            GvspInfo.PayloadSize = GvspInfo.PacketLength - GvspInfo.PayloadOffset;
            GvspInfo.FinalPacketID = 0;
        }
    }
}
using GenICam;
using GigeVision.Core.Interfaces;
using GigeVision.Core.Models;
using System;
using System.Threading.Tasks;

namespace GigeVision.Core
{
    public class GenPort : IGenPort
    {
        public GenPort(IGvcp gvcp)
        {
            Gvcp = gvcp;
        }

        public IGvcp Gvcp { get; }

        public async Task<IReplyPacket> Read(long address, long length)
        {
            byte[] addressBytes = GetAddressBytes(address, length);
            Array.Reverse(addressBytes);

            GvcpReply reply = new GvcpReply();

            if (length < 4)
            {
                return reply;
            }

            if (length >= 8)
            {
                return await Gvcp.ReadMemoryAsync(Gvcp.CameraIp, addressBytes, (ushort)length).ConfigureAwait(false);
            }
            else
            {
                return await Gvcp.ReadRegisterAsync(addressBytes).ConfigureAwait(false);
            }
        }

        public async Task<IReplyPacket> Write(byte[] pBuffer, long address, long length)
        {
            await Gvcp.TakeControl(false).ConfigureAwait(false);

            byte[] addressBytes = GetAddressBytes(address, length);
            Array.Reverse(addressBytes);

            return await Gvcp.WriteRegisterAsync(addressBytes, BitConverter.ToUInt16(pBuffer)).ConfigureAwait(false);
        }

        private static byte[] GetAddressBytes(long address, long length)
        {
            switch (length)
            {
                case 2:
                    return BitConverter.GetBytes((short)address);

                case 4:
                    return BitConverter.GetBytes((int)address);

                default:
                    break;
            }
            return BitConverter.GetBytes((int)address);
        }
    }
}
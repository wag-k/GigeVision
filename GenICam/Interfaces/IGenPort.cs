using System;
using System.Threading.Tasks;

namespace GenICam
{
    public interface IGenPort
    {
        Task<IReplyPacket> Read(long address, long length);

        Task<IReplyPacket> Write(byte[] pBuffer, long address, long length);
    }
}
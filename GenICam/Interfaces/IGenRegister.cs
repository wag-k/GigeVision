using System;
using System.Threading.Tasks;

namespace GenICam
{
    public interface IGenRegister
    {
        Task<IReplyPacket> Get(long length);

        Task<IReplyPacket> Set(byte[] pBuffer, long length);

        long GetAddress();

        long GetLength();
    }
}
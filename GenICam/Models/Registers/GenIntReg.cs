using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GenICam
{
    public class GenIntReg : IRegister
    {
        public GenIntReg(long address, long length, GenAccessMode accessMode, Dictionary<string, IntSwissKnife> expressions, IGenPort genPort)
        {
            Address = address;
            Length = length;
            AccessMode = accessMode;
            Expressions = expressions;
            GenPort = genPort;
        }

        /// <summary>
        /// Register Address in hex format
        /// </summary>
        public long Address { get; }

        /// <summary>
        /// Register Length
        /// </summary>
        public long Length { get; }

        /// <summary>
        /// Register Access Mode
        /// </summary>
        public GenAccessMode AccessMode { get; }

        public Dictionary<string, IntSwissKnife> Expressions { get; set; }
        public IGenPort GenPort { get; }

        public async Task<IReplyPacket> Get(long length)
        {
            return await GenPort.Read(Address, Length).ConfigureAwait(false);
        }

        public async Task<IReplyPacket> Set(byte[] pBuffer, long length)
        {
            return await GenPort.Write(pBuffer, Address, length).ConfigureAwait(false);
        }

        public long GetAddress()
        {
            return Address;
        }

        public long GetLength()
        {
            return Length;
        }

        public async Task<long> GetValue()
        {
            IReplyPacket reply = await Get(Length).ConfigureAwait(false);
            long value;
            if (reply.MemoryValue != null)
            {
                value = Length switch
                {
                    2 => BitConverter.ToUInt16(reply.MemoryValue),
                    4 => BitConverter.ToUInt32(reply.MemoryValue),
                    8 => BitConverter.ToInt64(reply.MemoryValue),
                    _ => BitConverter.ToInt64(reply.MemoryValue),
                };
            }
            else
            {
                value = reply.RegisterValue;
            }

            return value;
        }
    }
}
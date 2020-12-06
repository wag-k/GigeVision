using System.Text;
using System.Threading.Tasks;

namespace GenICam
{
    public class GenStringReg : GenCategory, IGenString, IGenRegister
    {
        public GenStringReg(CategoryProperties categoryProperties, long address, ushort length, GenAccessMode accessMode, IGenPort genPort)
        {
            CategoryProperties = categoryProperties;
            Address = address;
            Length = length;
            AccessMode = accessMode;
            GenPort = genPort;
            SetupFeatures();
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

        public IGenPort GenPort { get; }
        public string Value { get; set; }

        public async Task<string> GetValue()
        {
            IReplyPacket replyPacket = await Get(Length).ConfigureAwait(false);
            Value = Encoding.ASCII.GetString(replyPacket.MemoryValue);
            return Value ?? "";
        }

        public async void SetValue(string value)
        {
            if (PValue is IRegister Register)
            {
                if (Register.AccessMode != GenAccessMode.RO)
                {
                    long length = Register.GetLength();
                    byte[] pBuffer = Encoding.ASCII.GetBytes(value);

                    IReplyPacket replyPacket = await Register.Set(pBuffer, length).ConfigureAwait(false);
                    if (replyPacket.IsSentAndReplyReceived && replyPacket.Reply[0] == 0 && replyPacket.MemoryValue != null)
                    {
                        Value = Encoding.ASCII.GetString(replyPacket.MemoryValue);
                    }
                }
            }
        }

        public long GetMaxLength()
        {
            return Length;
        }

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

        public async void SetupFeatures()
        {
            Value = await GetValue().ConfigureAwait(false);
        }
    }
}
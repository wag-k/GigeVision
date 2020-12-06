using System.Collections.Generic;

namespace GenICam
{
    public class EnumEntry
    {
        public uint Value { get; }
        public IIsImplemented IsImplemented { get; }

        public Dictionary<string, IPValue> Expressions { get; }

        public EnumEntry(uint value, IIsImplemented isImplemented)
        {
            Value = value;
            IsImplemented = isImplemented;
        }
    }
}
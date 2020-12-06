using Prism.Commands;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GenICam
{
    public class GenInteger : GenCategory, IGenInteger, IPValue
    {
        public bool PIsLocked { get; internal set; }

        public Representation Representation { get; internal set; }

        /// <summary>
        /// Integer Minimum Value
        /// </summary>
        public long Min { get; private set; } = long.MinValue;

        /// <summary>
        /// Integer Maximum Value
        /// </summary>
        public long Max { get; private set; } = long.MaxValue;

        /// <summary>
        /// Integer Increment Value
        /// </summary>
        public long Inc { get; } = 1;

        public IncMode IncMode { get; }

        public long Value
        {
            get;
            set;
        }

        public List<long> ListOfValidValue { get; }
        public string Unit { get; }
        public long ValueToWrite { get; set; }

        public GenInteger(CategoryProperties categoryProperties, long min, long max, long inc, IncMode incMode, Representation representation, long value, string unit, IPValue pValue, Dictionary<string, IntSwissKnife> expressions)
        {
            CategoryProperties = categoryProperties;
            Min = min;
            if (max == 0)
                max = int.MaxValue;

            Max = max;
            if (inc == 0)
                inc = 1;

            Inc = inc;
            IncMode = incMode;
            Representation = representation;
            Value = value;
            Unit = unit;
            PValue = pValue;
            Expressions = expressions;
            SetValueCommand = new DelegateCommand(ExecuteSetValueCommand);

            SetupFeatures();
        }

        public GenInteger(long value)
        {
            Value = value;
        }

        public async Task<long> GetValue()
        {
            long value = Value;

            if (PValue is IRegister Register)
            {
                if (Register.AccessMode != GenAccessMode.WO)
                {
                    var length = Register.GetLength();
                    var reply = await Register.Get(length).ConfigureAwait(false);

                    byte[] pBuffer;

                    if (reply.IsSentAndReplyReceived && reply.Reply[0] == 0)
                    {
                        if (reply.MemoryValue != null)
                            pBuffer = reply.MemoryValue;
                        else
                            pBuffer = BitConverter.GetBytes(reply.RegisterValue);

                        if (Representation == Representation.HexNumber)
                            Array.Reverse(pBuffer);

                        switch (length)
                        {
                            case 2:
                                value = BitConverter.ToUInt16(pBuffer); ;
                                break;

                            case 4:
                                value = BitConverter.ToUInt32(pBuffer);
                                break;

                            case 8:
                                value = BitConverter.ToInt64(pBuffer);
                                break;
                        }
                    }
                }
            }
            else if (PValue is IntSwissKnife intSwissKnife)
            {
                value = (long)intSwissKnife.Value;
            }

            return value;
        }

        public async void SetValue(long value)
        {
            if (PValue is IRegister Register)
            {
                if (Register.AccessMode != GenAccessMode.RO)
                {
                    if ((value % Inc) == 0)
                    {
                        var length = Register.GetLength();
                        byte[] pBuffer = new byte[length];

                        switch (length)
                        {
                            case 2:
                                pBuffer = BitConverter.GetBytes((ushort)value);
                                break;

                            case 4:
                                pBuffer = BitConverter.GetBytes((int)value);
                                break;

                            case 8:
                                pBuffer = BitConverter.GetBytes(value);
                                break;
                        }

                        var reply = await Register.Set(pBuffer, length).ConfigureAwait(false);
                        if (reply.IsSentAndReplyReceived && reply.Reply[0] == 0)
                            Value = value;
                    }
                }
            }
            ValueToWrite = Value;
            RaisePropertyChanged(nameof(ValueToWrite));
        }

        public long GetMin()
        {
            var pMin = ReadIntSwissKnife("pMin");
            if (pMin != null)
                return (long)pMin;

            return Min;
        }

        public long GetMax()
        {
            var pMax = ReadIntSwissKnife("pMax");
            if (pMax != null)
                return (long)pMax;

            return Max;
        }

        public long? GetInc()
        {
            if (IncMode == IncMode.fixedIncrement)
                return Inc;
            else
                return null;
        }

        public List<long> GetListOfValidValue()
        {
            if (IncMode == IncMode.listIncrement)
                return ListOfValidValue;
            else
                return null;
        }

        public IncMode GetIncMode()
        {
            return IncMode;
        }

        public Representation GetRepresentation()
        {
            return Representation;
        }

        public string GetUnit()
        {
            return Unit;
        }

        public void ImposeMin(long min)
        {
            throw new NotImplementedException();
        }

        public void ImposeMax(long max)
        {
            throw new NotImplementedException();
        }

        public IGenFloat GetFloatAlias()
        {
            throw new NotImplementedException();
        }

        private long? ReadIntSwissKnife(string pNode)
        {
            if (Expressions == null)
                return null;

            if (!Expressions.ContainsKey(pNode))
                return null;

            var pValueNode = Expressions[pNode];
            if (pValueNode is IntSwissKnife intSwissKnife)
            {
                return (long)intSwissKnife.Value;
            }

            return null;
        }

        public async void SetupFeatures()
        {
            Value = await GetValue().ConfigureAwait(false);
            Max = GetMax();
            Min = GetMin();
            ValueToWrite = Value;
        }

        private void ExecuteSetValueCommand()
        {
            if (Value != ValueToWrite)
                SetValue(ValueToWrite);
        }
    }
}
using Prism.Commands;
using System;
using System.Collections.Generic;

namespace GenICam
{
    public class GenCommand : GenCategory, IGenCommand
    {
        public long Value { get; set; }
        public long CommandValue { get; }

        public GenCommand(CategoryProperties categoryProperties, long commandValue, IPValue pValue, Dictionary<string, IntSwissKnife> expressions)
        {
            CategoryProperties = categoryProperties;
            CommandValue = commandValue;
            PValue = pValue;
            Expressions = expressions;

            SetValueCommand = new DelegateCommand(Execute);
        }

        public async void Execute()
        {
            if (PValue is IRegister Register)
            {
                var length = Register.Length;
                byte[] pBuffer = new byte[length];

                switch (length)
                {
                    case 2:
                        pBuffer = BitConverter.GetBytes((ushort)CommandValue);
                        break;

                    case 4:
                        pBuffer = BitConverter.GetBytes((int)CommandValue);
                        break;

                    case 8:
                        pBuffer = BitConverter.GetBytes(CommandValue);
                        break;
                }

                await Register.Set(pBuffer, length).ConfigureAwait(false);
            };
        }

        public bool IsDone()
        {
            throw new NotImplementedException();
        }
    }
}
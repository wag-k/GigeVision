using System;

namespace GenICam
{
    public interface IRegister : IPValue, IGenRegister
    {
        /// <summary>
        /// Register Address in hex format
        /// </summary>
        long Address { get; }

        /// <summary>
        /// Register Length
        /// </summary>
        long Length { get; }

        /// <summary>
        /// Register Access Mode
        /// </summary>
        GenAccessMode AccessMode { get; }
    }
}
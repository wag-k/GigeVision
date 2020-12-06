using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GenICam
{
    public interface IGenEnumeration
    {
        Task<long> GetIntValue();

        void SetIntValue(long value);

        Dictionary<string, EnumEntry> GetEntries();

        //ToDo: Look this method up
        void GetSymbolics(Dictionary<string, EnumEntry> entries);

        EnumEntry GetEntryByName(string entryName);

        EnumEntry GetEntry(long entryValue);

        //ToDo: Look this method up
        EnumEntry GetCurrentEntry(long entryValue);
    }
}
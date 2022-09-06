using GigeVision.Core.Models;

namespace GigeVision.Core.Interfaces
{
    public interface IStreamReceiver
    {
        GvspInfo GvspInfo { get; }
        bool IsDecodingAsVersion2 { get; set; }

        void StartRxThread();
    }
}
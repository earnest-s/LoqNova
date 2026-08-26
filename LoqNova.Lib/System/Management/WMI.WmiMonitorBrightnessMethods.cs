using System.Collections.Generic;
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
// ReSharper disable StringLiteralTypo

namespace LoqNova.Lib.System.Management;

public static partial class WMI
{
    public static class WmiMonitorBrightnessMethods
    {
        public static Task WmiSetBrightness(int brightness, int timeout) => CallAsync("root\\WMI",
            $"SELECT * FROM WmiMonitorBrightnessMethods",
            "WmiSetBrightness",
            new()
            {
                { "Brightness", brightness },
                { "Timeout", timeout }
            });
    }

    public static class WmiMonitorBrightnessReader
    {
        public static Task<IEnumerable<double>> ReadAsync() => ReadAsync("root\\WMI",
            $"SELECT * FROM WmiMonitorBrightness",
            pdc => Convert.ToDouble(pdc["CurrentBrightness"].Value));
    }
}

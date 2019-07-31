using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WifiTriangulationServer.Models
{
    public class BSSIDSignal
    {
        public long BSSID { get; set; }
        public int SignalStrength { get; set; }
        public int Frequency { get; set; }
    }
}
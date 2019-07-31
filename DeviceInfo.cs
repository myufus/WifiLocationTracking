using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WifiTriangulationServer.Models
{
    public class DeviceInfo
    {
        public int Id { get; set; }
        public string MacAddress { get; set; }
        public double LOSPropogationConstant2 { get; set; }
        public double LOSPropogationConstant5 { get; set; }
        public double NLOSPropogationConstant2 { get; set; }
        public double NLOSPropogationConstant5 { get; set; }
        public List<List<BSSIDSignal>> SignalSamples { get; set; }
    }
}
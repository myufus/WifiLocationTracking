using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WifiTriangulationServer.Models
{
    public class AccessPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
        public Point Location { get; set; }
        public byte[] MacAddress { get; set; }
        public double Distance { get; set; }
    }
}
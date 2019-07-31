using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WifiTriangulationServer.Models
{
    public class Estimate
    {
        public Point Location { get; set; }
        public int Confidence { get; set; }
    }
}
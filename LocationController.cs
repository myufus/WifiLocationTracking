using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using WifiTriangulationServer.Models;
using WifiTriangulationServer.DataAccess;

namespace WifiTriangulationServer.Controllers
{
    public class LocationController : ApiController
    {
        private static double LOSPathLossThreshold = 7.5;
        private static double NLOSPathLosThreshold = 16;
        private static int LowFrequencyThreshold = 4000;
        private static int OneMeterRssi = -30;
        private static double FeetInAMeter = 3.281;
        private static int CloseRssiThreshold = -45;
        private static int FarRssiThreshold = -80;
        private static int LowFrequencyMhz = 2400;
        private static int HighFrequencyMhz = 5000;

        private static double DistanceBetweenPoints(Point a, Point b)
        {
            return Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
        }
        private static Point CloserPointToTarget(Point a, Point b, Point target)
        {
            if (DistanceBetweenPoints(a, target) < DistanceBetweenPoints(b, target))
                return a;
            return b;
        }

        private static bool DoAccessPointRangesIntersect(AccessPoint ap1, AccessPoint ap2)
        {
            var d = DistanceBetweenPoints(ap1.Location, ap2.Location);
            return d > ap1.Distance && d > ap2.Distance && d < ap1.Distance + ap2.Distance;
        }
        private static bool DoesRangeContainAccessPoint(AccessPoint apLarger, AccessPoint apSmaller)
        {
            return DistanceBetweenPoints(apSmaller.Location, apLarger.Location) <= apLarger.Distance - apSmaller.Distance;
        }
        private static List<Point> GetAccessPointRangeIntersections(AccessPoint ap1, AccessPoint ap2)
        {
            var intersections = new List<Point>();

            var p0 = ap1.Location; var r0 = ap1.Distance;
            var p1 = ap2.Location; var r1 = ap2.Distance;
            var d = DistanceBetweenPoints(p0, p1);
            var a = (Math.Pow(r0, 2) - Math.Pow(r1, 2) + Math.Pow(d, 2)) / (2 * d);
            var h = Math.Sqrt(Math.Pow(r0, 2) - Math.Pow(a, 2));
            var x2 = p0.X + (h * (p1.X - p0.X)) / d;
            var y2 = p0.Y + (h * (p1.Y - p0.Y)) / d;
            var x3 = x2 + (h * (p1.Y - p0.Y)) / d;
            var y3 = y2 - (h * (p1.X - p0.X)) / d;
            var x4 = x2 - (h * (p1.Y - p0.Y)) / d;
            var y4 = y2 + (h * (p1.X - p0.X)) / d;

            intersections.Add(new Point() { X = (int)x3, Y = (int)y3 });
            intersections.Add(new Point() { X = (int)x4, Y = (int)y4 });
            return intersections;
        }
        private static Point ClosestPointOnRangeToTargetCenter(AccessPoint range, AccessPoint target)
        {
            var dx = target.X - range.X;
            var dy = target.Y - range.Y;
            var d = DistanceBetweenPoints(range.Location, target.Location);
            var offX = range.Distance * dx / d;
            var offY = range.Distance * dy / d;
            var newX = range.X + offX;
            var newY = range.Y + offY;
            return new Point() { X = (int)newX, Y = (int)newY };
        }
        private static Point ClosestPoiintOnRangeToTargetEdge(AccessPoint range, AccessPoint target)
        {
            var dx = range.X - target.X;
            var dy = range.Y - target.Y;
            var d = DistanceBetweenPoints(range.Location, target.Location);
            var offX = range.Distance * dx / d;
            var offY = range.Distance * dy / d;
            var newX = range.X + offX;
            var newY = range.Y + offY;
            return new Point() { X = (int)newX, Y = (int)newY };
        }
        private static Point WeightedCenterOfAccessPointRanges(AccessPoint ap1, AccessPoint ap2)
        {
            var d = DistanceBetweenPoints(ap1.Location, ap2.Location) * (ap1.Distance / (ap1.Distance + ap2.Distance));
            var range = new AccessPoint() { X = ap1.X, Y = ap1.Y, Location = ap1.Location, Distance = d };
            return ClosestPointOnRangeToTargetCenter(range, ap2);
        }

        private static bool IsSignalFromAccessPoint(BSSIDSignal signal, AccessPoint ap)
        {
            if (ap.MacAddress == null || ap.MacAddress.Length != 6)
                return false;
            var mac = BitConverter.GetBytes(signal.BSSID);
            for (int i = 0; i < 5; i++)
            {
                if (mac[i + 2] != ap.MacAddress[i])
                    return false;
            }
            return (ap.MacAddress[5] + 1) <= mac[7] && mac[7] <= (ap.MacAddress[5] + 16);
        }

        private static double GetPropogationConstant(DeviceInfo deviceInfo, int frequency, double pathLoss)
        {
            if (pathLoss <= LOSPathLossThreshold)
                return frequency < LowFrequencyThreshold ? deviceInfo.LOSPropogationConstant2 : deviceInfo.LOSPropogationConstant5;
            else if (pathLoss <= NLOSPathLosThreshold)
                return frequency < LowFrequencyThreshold ? deviceInfo.NLOSPropogationConstant2 : deviceInfo.NLOSPropogationConstant5;
            else
                return -1;
        }
        private static double CalculateDistance(DeviceInfo deviceInfo, int frequency, int signalStrength, double pathLoss)
        {
            var oneMeterSignalStrength = OneMeterRssi;
            var propogationConstant = GetPropogationConstant(deviceInfo, frequency, pathLoss);
            return Math.Pow(10, ((oneMeterSignalStrength - signalStrength) / (10 * propogationConstant))) * FeetInAMeter;
        }

        private static Point CenterOfEstimates(List<Estimate> estimates)
        {
            var x = estimates.Select(e => e.Location.X).Average();
            var y = estimates.Select(e => e.Location.Y).Average();
            return new Point() { X = (int)x, Y = (int)y };
        }

        public string Post(HttpRequestMessage requestContant)
        {
            try
            {
                string response = "";

                var accessPoints = Database.GetAccessPoints();
                string jsonString = requestContant.Content.ReadAsStringAsync().Result;
                var deviceInfo = System.Web.Helpers.Json.Decode<DeviceInfo>(jsonString);
                response += $"Device MAC: {deviceInfo.MacAddress}" + "\n\n";
                var estimates = new List<Estimate>();

                foreach (var signalList in deviceInfo.SignalSamples)
                {
                    var visibleAccessPoints = new List<AccessPoint>();

                    foreach (var ap in accessPoints)
                    {
                        var signals = signalList.FindAll(s => IsSignalFromAccessPoint(s, ap));
                        var lowFreqSignals = signals.FindAll(s => s.Frequency < LowFrequencyThreshold);
                        var highFreqSignals = signals.FindAll(s => s.Frequency > LowFrequencyThreshold);
                        double distance = -1, lowFreqDistance = -1, highFreqDistance = -1, lowFreqRssi = 0, highFreqRssi = 0, pathLoss = -1;

                        if (lowFreqSignals.Count > 0 && highFreqSignals.Count > 0)
                        {
                            lowFreqRssi = lowFreqSignals.Select(s => s.SignalStrength).Average();
                            highFreqRssi = highFreqSignals.Select(s => s.SignalStrength).Average();
                            pathLoss = lowFreqRssi - highFreqRssi;

                            if (pathLoss <= NLOSPathLosThreshold)
                            {
                                if (highFreqRssi <= FarRssiThreshold && pathLoss < LOSPathLossThreshold)
                                    pathLoss = NLOSPathLosThreshold;
                                if (lowFreqRssi > CloseRssiThreshold)
                                    pathLoss = 0;

                                lowFreqDistance = CalculateDistance(deviceInfo, LowFrequencyMhz, (int)lowFreqRssi, pathLoss);
                                lowFreqDistance = CalculateDistance(deviceInfo, HighFrequencyMhz, (int)highFreqRssi, pathLoss);
                                distance = (lowFreqDistance + highFreqDistance) / 2;

                                visibleAccessPoints.Add(new AccessPoint() { X = ap.X, Y = ap.Y, Location = ap.Location, Distance = distance });
                            }
                        }

                        if(distance >= 0)
                        {
                            response += $"({ap.X}, {ap.Y}) [{(int)Math.Floor(lowFreqRssi)}, {(int)Math.Floor(highFreqRssi)}, {(int)Math.Floor(pathLoss)}]" + "\n" + $"{distance} ft ({lowFreqSignals.Count}: {Math.Floor(lowFreqDistance)}, {highFreqSignals.Count}: {Math.Floor(highFreqDistance)})" + "\n";
                        }
                    }

                    if(visibleAccessPoints.Count >= 3)
                    {
                        visibleAccessPoints.Sort((ap1, ap2) => ap1.Distance.CompareTo(ap2.Distance));
                        var nearestAp = visibleAccessPoints.First();
                        visibleAccessPoints.Remove(nearestAp);
                        var intersectors = visibleAccessPoints.FindAll(ap => DoAccessPointRangesIntersect(nearestAp, ap));
                        var containers = visibleAccessPoints.FindAll(ap => DoesRangeContainAccessPoint(ap, nearestAp));
                        var disjoint = visibleAccessPoints.FindAll(ap => !(intersectors.Contains(ap) || containers.Contains(ap)));

                        Point estimate = null;
                        int confidence = -1;
                        if(intersectors.Count > 0)
                        {
                            var nearestIntersector = intersectors.First();
                            visibleAccessPoints.Remove(nearestIntersector);
                            var nextNearestAp = visibleAccessPoints.First();

                            var intersections = GetAccessPointRangeIntersections(nearestAp, nearestIntersector);
                            estimate = CloserPointToTarget(intersections.First(), intersections.Last(), nextNearestAp.Location);
                            confidence = 3;
                        }
                        else if(containers.Count > 0)
                        {
                            var nearestContainer = containers.First();
                            estimate = ClosestPoiintOnRangeToTargetEdge(nearestAp, nearestContainer);
                            confidence = 2;
                        }
                        else
                        {
                            var nextNearestAp = visibleAccessPoints.First();
                            estimate = WeightedCenterOfAccessPointRanges(nearestAp, nextNearestAp);
                            confidence = 1;
                        }

                        if(estimate != null)
                        {
                            response += $"Estiamted location: ({estimate.X}, {estimate.Y}) [{confidence}]" + "\n\n";
                            estimates.Add(new Estimate() { Location = estimate, Confidence = confidence });
                        }
                    }
                    else
                    {
                        response += "Not enough usable data to estimate locaiton\n\n";
                    }
                }

                if(estimates.Count > 0)
                {
                    estimates.Sort((e1, e2) => e2.Confidence.CompareTo(e1.Confidence));
                    var highestConfidence = estimates.First().Confidence;
                    var bestEstimates = estimates.FindAll(e => e.Confidence == highestConfidence);
                    var finalEstimate = CenterOfEstimates(bestEstimates);

                    response += $"Final estimate: ({finalEstimate.X}, {finalEstimate.Y}) [{highestConfidence}, {bestEstimates.Count}]" + "\n";

                    deviceInfo = Database.FindScanner(deviceInfo);
                    if(deviceInfo.Id != -1)
                    {

                        Database.UpdateDeviceLocation(deviceInfo.Id, finalEstimate, System.DateTime.Now);
                        response += "Device updated in database";
                    }
                    else
                    {
                        response += "Couldn't update device in database";
                    }
                }
                else
                {
                    response += "No estimates of location could be made";
                }

                return response;
            }
            catch (Exception e)
            {
                return e.Message + "\n" + e.StackTrace + "\n" + e.Data;
            }
        }
    }
}
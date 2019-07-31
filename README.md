# Scanner Wi-Fi Tracking Server Application
This document provides an overview of the wi-fi location techniques used in this application and its overall design.

##### Table of Contents

###### How It Works
- [Overview](#technical-overview)
- [Identifying Access Points](#identifying-access-points)
- [Estimating Distance](#estimating-distance)
- [Estimating Location](#estimating-location)

###### Application Design and Implementation
- [Overview](#design-and-implementation-overview)
- [Directories and Files](#notable-directories-and-files)
- [Under the Hood: Estimating Scanner Location](#what-happens-when-estimating-scanner-locations)

## Technical Overview
Because all the scanners on the factory floor are wi-fi enabled, knowing the locations of access points on the factory floor and what access points are visible from a scanner is enough to estimate the location of the scanner. A Google search will show that plenty of research has been done on the feasibility and accuracy of wi-fi positioning systems, with many different techniques used to try and achieve a high level of accuracy on these location estimates. However, the vast majority of published academic research results in this domain are from experiments on small, controlled spaces, meaning the same levels of accuracy aren't achievable using those techniques in the ever-changing conditions of the factory floor.

This application uses a technique published by researchers at Chungbuk National University to take the received signal strength reported by the scanners and estimate the distance to the access point the signal came from. These distance estimates are then used to estimate location through an algorithm that takes the information on the location of and distance from all visible access points and generates an estimate of the scanner’s location.

## Identifying Access Points
When an access point is set up to broadcast a specific network, it broadcasts two identifiers: an SSID, which is the name of the network, and a BSSID, which is a 6-byte hexadecimal number that resembles a MAC address. The BSSID, however, isn't the MAC address of the access point. A network's BSSID is derived from the MAC address of the access point, which in this case means that it one of o the first 16 numbers after the access point's MAC address. For example, if an access point's MAC address is 00:00:00:00:00:20, then the signals that come from it will have values between 00:00:00:00:00:21 and 00:00:00:00:00:30.

Therefore, in order to determine which access point a specific signal is coming from, we must compare its BSSID with the MAC address of all the access points. If we know the location of the access point that matches the BSSID, then we know where the signal is being broadcast from and can use the strength of the signal to get some sense of where our device is.

## Estimating Distance
Wi-Fi is broadcast through radio waves, which means that, in open space, the signal gets exponentially weaker with distance. In fact, in a perfectly open space with a direct line of sight between an access point and scanner, measuring the signal strength and frequency of a Wi-Fi signal can give an accurate estimate of the distance between the access point and scanner.

However, out on the factory floor there is almost never a situation where a scanner has a clear line of sight to all the access points around it. This means that, by using the simple estimation technique used in free space, the distance estimates will almost always be farther than the actual distance between the access point and scanner, meaning the location estimates will be extremely inaccurate.

To counter this, the application uses a technique developed by Hyeon Jeong Jo and Seungku Kim at Chungbuk National University and published in November 2018 (their paper can be found [here](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC6263745/)). They realized that, since 5 GHz Wi-Fi signals will be weakened more by interference than 2.4 GHz signals, the difference between them can be used to determine whether there is a clear line of sight to the access point or not. Knowing whether there is a clear line of sight to the access point allows for a more accurate estimate of distance by changing the exponent in the estimation equation to more accurately reflect the correlation between distance and signal strength.

## Estimating Location
Typically, a Wi-Fi positioning system that uses signal strength to estimate distance from access points with known locations will use a technique called trilateration to guess its location. However, the nature of the interference on the factory floor means that there isn't always enough data to make trilateration mathematically possible, which means a different technique had to be developed for this application.

For estimating the location of a scanner, the distance to all access points are calculated, and each estimate is treated geometrically as a circle centered on the access point's coordinates with a radius equal to the estimated distance. This means that, once all the possible distance estimates are made, we are left with a bunch of circles on a plane. If we have three or more, we know we can make some sort of estimate.

We begin by isolating the smallest circle. We know since this has the smallest distance estimate that it is most likely going to be the closest access point to the scanner. We then classify the remaining circle into three groups:
- intersectors: circles that intersect with the smallest circle
- containers: circles with surround the smallest circle
- disjoint: circles that neither intersect nor contain the smallest circle

Classifying the remaining circles like this allows us to determine how we should make the estimate of the scanner's location as well as how confident we can be in this estimate.

If there are any intersecting circles, we know we can make a very accurate estimate. We do this by finding the two points at which the circles intersect, then choosing the one that is closer to the next nearest access point.

If there are no intersecting circles but some container circles, then we know our estimate won't be as good but will still be decent. We make this estimate by simply taking the point on the smallest circle that is closest to the edge of its smallest container.

If every circle is disjoint, we know our estimate won't be as good but will still be good enough to fall within our desired accuracy. To make our estimate we take a weighted average of sorts of the two circles: we pick a point along the line between the two circles' centers whose distance from the smallest circles is proportional to the ratio of the two circles' radii. In practice, this means that our estimate for two circles whose radii are the same will be the point equidistant from both circles, and as the difference in the circles' radii increases, the estimate will shift closer to the smaller circle, since we assume that one is indicative of the closer access point.

## Design and Implementation Overview
This application is built as a simple web service that takes HTTP POST requests with data on visible wi-fi signals and tries to estimate the client's location from the given information. If it can, it looks to see if the client is in the device map database and updates its location.

The application is implemented as a .NET Web API Application, with one method to call via HTTP request and classes to handle communication with the device map database.

## Notable Directories and Files
- Controllers
 - LocationController.cs: contains methods for location estimation calculation and method that runs whenever a request is received by the server
- DataAccess
 - Database.cs: contains methods to send/receive data to/from the database pf device locations
- Models: contains classes representing data that gets sent from scanners and to/from the database
- Web.config: contains variables used by the application, such as connection strings for databases

## What Happens When Estimating Scanner Locations
When the server receives a POST request to the Location API, it takes the request data and begins to prepare to make an estimate. It starts by importing all the necessary access point information form the device map database. Then it serializes the data in the POST request, which comes as JSON, into an object containing the MAC address of the device requesting its location,  constants specific to the device to use for calculating distances, and multiple lists of signal reading, with each list representing a sample of signal strength readings.

For each sample it goes through all the access points, finding the ones that having matching 2.4 GHz and 5 GHz signals in the sample and estimating their distance from the device. It then checks whether it has enough access points with estimated distances to make a location estimate and, if it does, calculates the estimated location along with an indicator of the confidence in that estimate.

Once it has gone through all the samples, the application looks at all the estimates made. The estimates that have the highest confidence level are averaged, and that average is then denoted as the final estimate. The application then tries to update the device map database with this final location estimate if it finds a matching device in the database.

***(Last updated 7/30/2019 by Muhammad Yusuf)***
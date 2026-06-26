using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MovieMap.Core
{
    public static class CoordinateAnalyzer
    {
        // Return the frame number closest to coordinate within the path data.
        public static int CalcNearestFrame(string path, Coordinate coordinate)
        {
            var trajectory = GlobalInfo.CoordinateDict[path];

            var nearestIndex = -1;
            var nearestDistance = 999999f;

            // Avoid scanning every frame by narrowing from 100-step to 10-step to 1-step intervals.

            // 100-step interval.
            for(var i = 0; i <= (trajectory.Count - 1) / 100; i++)
            {
                var coord = trajectory[i * 100].Item1;
                var d = Coordinate.Distance(coordinate, coord);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearestIndex = i * 100;
                }
            }

            // 10-step interval.
            var tmpIndex = nearestIndex;
            for(var i = -10; i < 10; i++)
            {
                if(tmpIndex + i * 10 < 0 || trajectory.Count <= tmpIndex + i * 10) { continue; }
                var coord = trajectory[tmpIndex + i * 10].Item1;
                var d = Coordinate.Distance(coordinate, coord);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearestIndex = tmpIndex + i * 10;
                }
            }

            // 1-step interval.
            tmpIndex = nearestIndex;
            for (var i = -10; i < 10; i++)
            {
                if (tmpIndex + i < 0 || trajectory.Count <= tmpIndex + i) { continue; }
                var coord = trajectory[tmpIndex + i].Item1;
                var d = Coordinate.Distance(coordinate, coord);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearestIndex = tmpIndex + i;
                }
            }

            return nearestIndex;
        }
    }
}

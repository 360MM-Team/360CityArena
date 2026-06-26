using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MovieMap.Core
{
    public class Coordinate
    {
        float _latitude;
        float _longitude;
        public float Latitude => _latitude;
        public float Longitude => _longitude;

        public Coordinate(float latitude, float longitude)
        {
            _latitude = latitude;
            _longitude = longitude;
        }

        static public float Distance(Coordinate coord1, Coordinate coord2)
        {
            return Mathf.Sqrt(Mathf.Pow(coord1.Latitude - coord2.Latitude, 2) + Mathf.Pow(coord1.Longitude - coord2.Longitude, 2));
        }

        public override bool Equals(object obj)
        {
            return obj is Coordinate coordinate && Longitude == coordinate.Longitude && Latitude == coordinate.Latitude;
        }

        public override int GetHashCode()
        {
            return Longitude.GetHashCode() ^ Latitude.GetHashCode();
        }
    }

    public class FramePoint
    {
        string _path;
        int _frame;
        public string Path => _path;
        public int Frame => _frame;

        public FramePoint(string path, int frame)
        {
            _path = path;
            _frame = frame;
        }
        public override bool Equals(object obj)
        {
            return obj is FramePoint framePoint && Path == framePoint.Path && Frame == framePoint.Frame;
        }

        public override int GetHashCode()
        {
            return Path.GetHashCode() ^ Frame.GetHashCode();
        }
    }

    public class LargeIntersection
    {
        FramePoint[] _points;
        Coordinate _coordinate;
        public FramePoint[] Points => _points;
        public Coordinate Coordinate => _coordinate;

        // For initialization.
        public LargeIntersection()
        {

        }

        public LargeIntersection(FramePoint[] points, Coordinate coordinate)
        {
            _points = points;
            _coordinate = coordinate;
        }

        // Return the path ID from this intersection to the target, or an empty string if they are not connected.
        public string GetPathToLargeIntersection(LargeIntersection to)
        {
            foreach(var fp in Points)
            {
                foreach(var tp in to.Points)
                {
                    if(fp.Path == tp.Path)
                    {
                        if(fp.Frame != -1 && tp.Frame != -1)
                        {
                            if(fp.Frame < tp.Frame)
                            {
                                return fp.Path;
                            }
                        }
                    }
                }
            }
            return "";
        }

        public override bool Equals(object obj)
        {
            return obj is LargeIntersection largeIntersection && Points.SequenceEqual(largeIntersection.Points) && Equals(Coordinate, largeIntersection.Coordinate);
        }
        public override int GetHashCode()
        {
            var _hash = 0;
            foreach(var _point in _points)
            {
                _hash ^= _point.GetHashCode();
            }
            return _hash ^ Coordinate.GetHashCode();
        }
    }
}

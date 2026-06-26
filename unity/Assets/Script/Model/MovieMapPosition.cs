
using UnityEngine;

namespace MovieMap.Core
{
    public class MovieMapPosition
    {
        private Segment _segment;
        public Segment Segment => _segment;
        private Quaternion _rotation;
        public Quaternion Rotation => _rotation;
        private Coordinate _coordinate;
        public Coordinate Coordinate => _coordinate;

        public MovieMapPosition(Coordinate coordinate, Quaternion quaternion, Segment segment)
        {
            _coordinate = coordinate;
            _rotation = quaternion;
            _segment = segment;
        }

        public void ChangeSegment(LargeIntersection fromLarge, LargeIntersection toLarge)
        {
            _segment.ChangeSegment(fromLarge, toLarge);
        }

        public void ChangeSegment(Segment segment)
        {
            _segment = segment;
        }

        public void UpdatePosition(Coordinate coordinate)
        {
            _coordinate = coordinate;
        }

        public void UpdateRotation(Quaternion quaternion)
        {
            _rotation = quaternion;
        }

        public void UpdateToNextSegment(LargeIntersection nextLargeIntersection)
        {
            Segment nextSegment = new Segment(_segment.ToLarge, nextLargeIntersection);
            ChangeSegment(nextSegment);
        }

        public void UpdateToReverseNextSegment(LargeIntersection nextLargeIntersection)
        {
            Segment nextSegment = new Segment(_segment.FromLarge, nextLargeIntersection);
            ChangeSegment(nextSegment);
        }

        public Vector3 CurrentUnityPosition(Vector3 baseCoordinate, Vector3 scale)
        {
            Vector3 currentCoordinate = new Vector3(_coordinate.Longitude, 0f, _coordinate.Latitude);
            return Vector3.Scale(currentCoordinate - baseCoordinate, scale);
        }
    }
}
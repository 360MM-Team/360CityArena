using System.Linq;

using MovieMap.Core;

using Unity.Burst;

using UnityEngine;
namespace MovieMap.Service
{
    public class UserLocationService
    {
        private readonly MovieMapPosition _movieMapPosition;

        public UserLocationService(MovieMapPosition movieMapPosition)
        {
            _movieMapPosition = movieMapPosition;
        }
        public void UpdateCurrentPosition(int currentFrame)
        {
            if (currentFrame < 0)
            {
                return;
            }
            var path = _movieMapPosition.Segment.Path;
            if (GlobalInfo.CoordinateDict.ContainsKey(path))
            {
                var movieFrame = currentFrame;
                // TODO: offsetFrame only changes when the video changes, so this does not need to be checked every time.
                var offsetFrame = _movieMapPosition.Segment.OffsetFrame;
                if (GlobalInfo.CoordinateDict[path].Count() > offsetFrame + movieFrame + 1)
                {
                    _movieMapPosition.UpdatePosition(GlobalInfo.CoordinateDict[path][offsetFrame + movieFrame].Item1);
                    _movieMapPosition.UpdateRotation(GlobalInfo.CoordinateDict[path][offsetFrame + movieFrame].Item2);
                }
            }
        }

        public (Vector3, Quaternion) CurrentPositionState()
        {
            Vector3 baseCoordinate = new Vector3(GlobalInfo.CenterPosition.x, 0f, GlobalInfo.CenterPosition.z);
            return (CalculateCurrentPosition(baseCoordinate, GlobalInfo.CoordinateScale), _movieMapPosition.Rotation);
        }

        public Segment CurrentSegment()
        {
            return _movieMapPosition.Segment;
        }

        public Coordinate CurrentCoordinate()
        {
            return _movieMapPosition.Coordinate;
        }

        public Vector3 CalculateCurrentPosition(Vector3 baseCorrdinate, Vector3 scale)
        {
            return _movieMapPosition.CurrentUnityPosition(baseCorrdinate, scale);
        }

        public void UpdateToNextSegment(LargeIntersection nextLargeIntersection)
        {
            _movieMapPosition.UpdateToNextSegment(nextLargeIntersection);
        }

        public void UpdateToReverseNextSegment(LargeIntersection nextLargeIntersection)
        {
            _movieMapPosition.UpdateToReverseNextSegment(nextLargeIntersection);
        }

        public void UpdateSegment(Segment segment)
        {
            _movieMapPosition.ChangeSegment(segment);
        }

        public (float, float) PositionRatio()
        {
            var (pos, _) = CurrentPositionState();
            return (pos.x / GlobalInfo.PositionDistance.x,
                    pos.z / GlobalInfo.PositionDistance.z);
        }

    }
}

using System.Linq;

namespace MovieMap.Core
{
    public class Segment
    {
        public LargeIntersection FromLarge { get; set; }
        public LargeIntersection ToLarge { get; set; }

        // First frame of this segment in the original unsplit video.
        public int OffsetFrame { get; set; }

        public string Path { get; set; }

        public Segment(LargeIntersection _fromLarge, LargeIntersection _toLarge)
        {
            ChangeSegment(_fromLarge, _toLarge);
        }

        public void ChangeSegment(LargeIntersection _fromLarge, LargeIntersection _toLarge)
        {
            FromLarge = _fromLarge;
            ToLarge = _toLarge;
            Path = PathUrl();
            OffsetFrame = SegmentFirstFrame();
        }

        public Coordinate StartCoordinate()
        {
            return FromLarge.Coordinate;
        }

        public string PathUrl()
        {
            return FromLarge.GetPathToLargeIntersection(ToLarge);
        }

        public int SegmentFirstFrame()
        {
            if (FromLarge?.Points == null || string.IsNullOrEmpty(Path))
            {
                return 0;
            }

            var point = FromLarge.Points.FirstOrDefault(p => p != null && p.Path == Path);
            return point?.Frame ?? 0;
        }

        public Segment ReverseSegment()
        {
            return new(ToLarge, FromLarge);
        }
    }
}

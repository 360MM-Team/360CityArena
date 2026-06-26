using Cysharp.Threading.Tasks;

using System.Collections.Generic;
using System.Threading;

using UnityEngine;

namespace MovieMap.Core
{
    public static class IntersectionAnalyzer
    {
        // Return possible video URLs for entering an intersection from fromLarge to toLarge.
        public async static UniTask<List<string>> CreateCandidateUrl(LargeIntersection fromLarge, LargeIntersection toLarge, CancellationToken token)
        {
            var result = new List<string>();

            var nextLargeCandidates = CalcNextLargeCandidates(fromLarge, toLarge);

            var mainPath = fromLarge.GetPathToLargeIntersection(toLarge);

            foreach (var candidate in nextLargeCandidates)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                var urls = await MovieUrlMaker.GetRouteMovieURL(toLarge, candidate, token);
                if (urls[0] != "") { result.Add(urls[0]); }
                if (urls[1] != "") { result.Add(urls[1]); }

                // Do not use intersection videos.
                var nextPath = toLarge.GetPathToLargeIntersection(candidate);
                if (mainPath != nextPath)
                {
                    var url = MovieUrlMaker.GetIntersectionMovieURL(mainPath, nextPath);
                    if (await FileReader.CheckFileExist(url, token))
                    {
                        result.Add(url);
                    }
                }
            }

            // T-junctions may require revisiting the segment already traveled.
            // This may be unnecessary.
            if (!token.IsCancellationRequested)
            {
                var fromUrls = await MovieUrlMaker.GetRouteMovieURL(fromLarge, toLarge, token);
                if (fromUrls[2] != "" && !result.Contains(fromUrls[2])) { result.Add(fromUrls[2]); }
            }


            return result;
        }

        // Fetch from the prebuilt dictionary.
        // Unlike the await-based path above, this excludes tiny clips cut from round-trip videos.
        // Example: A_XX-YY/A_27-28_A_28-27.mp4.
        public static List<string> CreateCandidateUrl(LargeIntersection fromLarge, LargeIntersection toLarge)
        {
            var result = new List<string>();

            var nextLargeCandidates = CalcNextLargeCandidates(fromLarge, toLarge);

            foreach (var candidate in nextLargeCandidates)
            {
                var url = MovieUrlMaker.GetRouteMovieURL(toLarge, candidate);
                result.Add(url);
            }
            return result;
        }

        public static List<string> CreateCandidateUrl(Segment segment)
        {
            return CreateCandidateUrl(segment.FromLarge, segment.ToLarge);
        }

        // Return the video URL sequence for moving from fromLarge to middleLarge to toLarge.
        public async static UniTask<List<string>> CreateSequenceUrl(LargeIntersection fromLarge, LargeIntersection middleLarge, LargeIntersection nextLarge, CancellationToken token)
        {
            var result = new List<string>();

            var fromPath = fromLarge.GetPathToLargeIntersection(middleLarge);
            var toPath = middleLarge.GetPathToLargeIntersection(nextLarge);

            // No turn.
            if (fromPath == toPath)
            {
                var urls = await MovieUrlMaker.GetRouteMovieURL(middleLarge, nextLarge, token);
                if (urls[0] != "") { result.Add(urls[0]); }
                if (urls[1] != "") { result.Add(urls[1]); }

            }

            // Has a turn.
            else
            {
                var fromPartUrls = await MovieUrlMaker.GetRouteMovieURL(fromLarge, middleLarge, token);
                var toPartUrls = await MovieUrlMaker.GetRouteMovieURL(middleLarge, nextLarge, token);

                if (CheckRequireRouteMovie(fromPath, toPath, fromPartUrls[2]))
                {
                    result.Add(fromPartUrls[2]);
                }

                var intersectUrl = MovieUrlMaker.GetIntersectionMovieURL(fromPath, toPath);
                if (await FileReader.CheckFileExist(intersectUrl, token))
                {
                    result.Add(intersectUrl);
                }

                if (CheckRequireRouteMovie(fromPath, toPath, toPartUrls[0]))
                {
                    result.Add(toPartUrls[0]);
                }

                if (toPartUrls[1] != "") { result.Add(toPartUrls[1]); }
            }

            return result;
        }

        public static List<LargeIntersection> CalcNextLargeCandidates(LargeIntersection fromLarge, LargeIntersection toLarge)
        {
            var result = new List<LargeIntersection>();

            foreach (var key in GlobalInfo.LargeIntersectionReferenceDict[toLarge].Keys)
            {
                LargeIntersection candidate = GlobalInfo.LargeIntersectionReferenceDict[toLarge][key];
                if (candidate != null && candidate != fromLarge)
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        static bool CheckRequireRouteMovie(string fromPath, string toPath, string url)
        {
            if (url == "") { return false; }

            var splitUrl = url.Split("/");

            // The URL is a partial video on fromPath.
            if (splitUrl[splitUrl.Length - 2] == fromPath)
            {
                // If the URL ends at the intersection with toPath.
                if (splitUrl[splitUrl.Length - 1].IndexOf(toPath) > 0)
                {
                    return true;
                }
            }

            // The URL is a partial video on toPath.
            if (splitUrl[splitUrl.Length - 2] == toPath)
            {
                // If the URL starts at the intersection with fromPath.
                if (splitUrl[splitUrl.Length - 1].IndexOf(fromPath) == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

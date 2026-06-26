using System.Linq;
using Cysharp.Threading.Tasks;
using System.Threading;


namespace MovieMap.Core
{
    public static class MovieUrlMaker
    {
        // Return the URL expected to contain video from where movePath crosses fromPath to where it crosses toPath.
        public static string GetRouteMovieURL(string movePath, string fromPath, string toPath)
        {
            if (movePath.IsNullOrEmpty() || fromPath.IsNullOrEmpty() || toPath.IsNullOrEmpty())
            {
                return null;
            }
            return GlobalInfo.AreaUrl + "/movie_part/" + movePath + "/" + fromPath + "_" + toPath + ".mp4";
        }
        public static string GetRouteJsonURL(string movePath, string fromPath, string toPath)
        {
            if (movePath.IsNullOrEmpty() || fromPath.IsNullOrEmpty() || toPath.IsNullOrEmpty())
            {
                return null;
            }
            return GlobalInfo.AreaUrl + "/unity_data/jsons/" + movePath + "/" + fromPath + "_" + toPath + ".json";
        }
        public static string ChangeURLMovieToJson(string MoviePath)
        {
            if (MoviePath.IsNullOrEmpty())
            {
                return null;
            }
            int ind = MoviePath.LastIndexOf("movie_part") + 10;
            string jsonUrl = GlobalInfo.AreaUrl + "/unity_data/jsons/" + MoviePath.Substring(ind, MoviePath.Length - ind - 3) + "json";
            return jsonUrl;
        }

        // Return the URL expected to contain the intersection video transitioning from fromPath to toPath.
        public static string GetIntersectionMovieURL(string fromPath, string toPath)
        {
            return GlobalInfo.AreaUrl + "/movie_part/i_" + fromPath + "_" + toPath + ".mp4";
        }

        // Return the route video URL, including partial intersection video, when moving from fromLarge to toLarge.
        public async static UniTask<string[]> GetRouteMovieURL(LargeIntersection fromLarge, LargeIntersection toLarge, CancellationToken token)
        {
            var result = new string[3] { "", "", "" };
            var mainPath = fromLarge.GetPathToLargeIntersection(toLarge);
            if (mainPath == "") { return result; }

            var reversePath = toLarge.GetPathToLargeIntersection(fromLarge);
            if (reversePath == "") { return result; }

            var fromAcrossPaths = fromLarge.Points.Where(p => p.Path != mainPath && p.Path != reversePath).Select(p => p.Path);
            var toAcrossPaths = toLarge.Points.Where(p => p.Path != mainPath && p.Path != reversePath).Select(p => p.Path);

            if (fromAcrossPaths.Count() != 2 || toAcrossPaths.Count() != 2) { return result; }

            // Determine AcrossPath order by checking which video file exists.
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var fromAcross = fromAcrossPaths.ElementAt(i);
                    var toAcross = toAcrossPaths.ElementAt(j);
                    var urlMain = GetRouteMovieURL(mainPath, fromAcross, toAcross);

                    if (await FileReader.CheckFileExist(urlMain, token))
                    {
                        result[1] = urlMain;

                        var urlFrom = GetRouteMovieURL(mainPath, fromAcrossPaths.ElementAt(1 - i), fromAcross);
                        if (await FileReader.CheckFileExist(urlFrom, token))
                        {
                            result[0] = urlFrom;
                        }

                        var urlTo = GetRouteMovieURL(mainPath, toAcross, toAcrossPaths.ElementAt(1 - j));
                        if (await FileReader.CheckFileExist(urlTo, token))
                        {
                            result[2] = urlTo;
                        }

                        return result;
                    }
                }
            }

            return result;
        }
        public static string GetRouteMovieURL(LargeIntersection fromLarge, LargeIntersection toLarge)
        {
            if (GlobalInfo.LargeIntersectionPathDict.ContainsKey(fromLarge))
            {
                if (GlobalInfo.LargeIntersectionPathDict[fromLarge].ContainsKey(toLarge))
                {
                    return GlobalInfo.LargeIntersectionPathDict[fromLarge][toLarge];
                }
            }
            return "";
        }
    }
}

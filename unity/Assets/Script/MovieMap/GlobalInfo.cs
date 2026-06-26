using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;


namespace MovieMap.Core
{
    public static class GlobalInfo
    {
        static string _areaUrl;
        public static string AreaUrl => _areaUrl;
        private static string _areaName;
        public static string AreaName => _areaName;

        static List<LargeIntersection> _largeIntersectionList;
        public static List<LargeIntersection> LargeIntersectionList => _largeIntersectionList;
        static Dictionary<LargeIntersection, Dictionary<string, LargeIntersection>> _largeIntersectionReferenceDict;
        // Return the LargeIntersection reached by each video extending from a LargeIntersection.
        static public Dictionary<LargeIntersection, Dictionary<string, LargeIntersection>> LargeIntersectionReferenceDict => _largeIntersectionReferenceDict;

        static public Dictionary<LargeIntersection, Dictionary<LargeIntersection, string>> _largeIntersectionPathDict;
        // Return the video segment between LargeIntersections.
        static public Dictionary<LargeIntersection, Dictionary<LargeIntersection, string>> LargeIntersectionPathDict => _largeIntersectionPathDict;

        static Dictionary<string, List<(Coordinate, Quaternion)>> _coordinateDict;
        // Store coordinates and quaternions for each video.
        static public Dictionary<string, List<(Coordinate, Quaternion)>> CoordinateDict => _coordinateDict;

        static Vector3 _referenceStartPoint, _referenceEndPoint;
        public static Vector3 ReferenceStartPoint => _referenceStartPoint;
        public static Vector3 ReferenceEndPoint => _referenceEndPoint;
        static Vector3 _centerPosition, _positionDistance;
        public static Vector3 CenterPosition => _centerPosition;
        public static Vector3 PositionDistance => _positionDistance;
        public static Vector3 CoordinateScale => _coordinateScale;

        private static Vector3 _coordinateScale = new Vector3(400000f, 1f, 400000f);
        public static string playerName = "player3";
        public static string playerNickName = "";
        private static bool _initialized = false;
        public static bool Initialized => _initialized;
        
        // Track initialization progress.
        private static bool _baseDataInitialized = false;
        public static bool BaseDataInitialized => _baseDataInitialized;
        
        // Store data from intersection_path.csv.
        private static string[][] _intersectionPathText;
        public static string[][] IntersectionPathText => _intersectionPathText;
        static string _url, _jsonUrl;
        public static string FirstUrl => _url;
        public static string FirstJsonUrl => _jsonUrl;
        static LargeIntersection _fromLarge, _toLarge;
        public static LargeIntersection FromLarge => _fromLarge;
        public static LargeIntersection ToLarge => _toLarge;
        
        // Variable for specifying the start point.
        private static int _startIndex = -1; // -1 means random selection.
        public static int StartIndex => _startIndex;

        private static bool _uploaded;


        public static bool doDisconnect
        {
            get => _doDisconnect;
            set => _doDisconnect = value;
        }

        public static int playerViewID
        {
            get => _playerViewID;
            set => _playerViewID = value;
        }

        private static string _playerUrl;

        public static string playerUrl

        {
            get => _playerUrl;
            set => _playerUrl = value;
        }
        private static int _playerViewID = 0;
        private static bool _isConnected = false;
        private static bool _doDisconnect = false;

        public static bool IsConnected => _isConnected;

        // Set the start point.
        public static void SetStartIndex(int index)
        {
            _startIndex = index;
            Debug.Log($"Start index set to: {index}");
        }
        
        // Get the index from a segment (FromLarge to ToLarge).
        public static int GetIndexFromSegment(LargeIntersection fromLarge, LargeIntersection toLarge)
        {
            if (_intersectionPathText == null)
            {
                Debug.LogWarning("Intersection path data is not loaded yet");
                return -1;
            }
            
            for (int i = 0; i < _intersectionPathText.Length; i++)
            {
                // Check whether coordinates match FromLarge.
                float fromLat = float.Parse(_intersectionPathText[i][8]);
                float fromLon = float.Parse(_intersectionPathText[i][9]);
                
                // Check whether coordinates match ToLarge.
                float toLat = float.Parse(_intersectionPathText[i][18]);
                float toLon = float.Parse(_intersectionPathText[i][19]);
                
                // Compare coordinates while allowing a small tolerance.
                if (Mathf.Abs(fromLarge.Coordinate.Latitude - fromLat) < 0.000001f &&
                    Mathf.Abs(fromLarge.Coordinate.Longitude - fromLon) < 0.000001f &&
                    Mathf.Abs(toLarge.Coordinate.Latitude - toLat) < 0.000001f &&
                    Mathf.Abs(toLarge.Coordinate.Longitude - toLon) < 0.000001f)
                {
                    return i;
                }
            }
            
            Debug.LogWarning($"Segment not found in intersection_path.csv: From({fromLarge.Coordinate.Latitude}, {fromLarge.Coordinate.Longitude}) -> To({toLarge.Coordinate.Latitude}, {toLarge.Coordinate.Longitude})");
            return -1;
        }



        public async static UniTask InitializeConfig(string areaName)
        {
            _areaUrl = "https://moviemap.jp/area/" + areaName;
            var configCsvArray = await CsvReader.DownloadCsv(GlobalInfo.AreaUrl + "/config.csv", ',');
            _referenceStartPoint = new Vector3(float.Parse(configCsvArray[0][1]), 0, float.Parse(configCsvArray[0][2]));
            _referenceEndPoint = new Vector3(float.Parse(configCsvArray[0][3]), 0, float.Parse(configCsvArray[0][0]));
            _centerPosition = _referenceStartPoint;
            ApplyMeterScale();
            _positionDistance = Vector3.Scale(_referenceEndPoint - _centerPosition, _coordinateScale);
            _initialized = true;
        }

        // Initialize only base data, excluding start point setup.
        public async static UniTask InitializeBaseData(string areaName)
        {
            _areaName = areaName;
            _areaUrl = "https://moviemap.jp/area/" + areaName;
            var configCsvArray = await CsvReader.DownloadCsv(_areaUrl + "/config.csv", ',');
            _referenceStartPoint = new Vector3(float.Parse(configCsvArray[0][1]), 0, float.Parse(configCsvArray[0][2]));
            _referenceEndPoint = new Vector3(float.Parse(configCsvArray[0][3]), 0, float.Parse(configCsvArray[0][0]));
            SetCoordinateDict().Forget();
            _centerPosition = _referenceStartPoint;
            ApplyMeterScale();
            _positionDistance = Vector3.Scale(_referenceEndPoint - _centerPosition, _coordinateScale);
            var intersectionText = await CsvReader.DownloadCsv(_areaUrl + "/unity_data/large_intersection.csv", ',');
            _largeIntersectionList = CsvReader.TransToLargeIntersectionList(intersectionText);
            Debug.Log(_areaUrl + "/unity_data/large_intersection.csv is downloaded!");
            _intersectionPathText = await CsvReader.DownloadCsv(_areaUrl + "/unity_data/intersection_path.csv", ',');
            _largeIntersectionPathDict = CsvReader.TransToLargeIntersectionDictionary(_intersectionPathText, _areaUrl + "/movie_part/");
            Debug.Log(_areaUrl + "/unity_data/intersection_path.csv is downloaded!");
            
            // Base data initialization complete.
            _baseDataInitialized = true;
            Debug.Log("Base data initialization completed!");
        }
        
        // Set the start point and complete initialization.
        public static void SetStartLocationAndFinalize()
        {
            if (!_baseDataInitialized)
            {
                Debug.LogError("Base data must be initialized before setting start location!");
                return;
            }
            
            if (_intersectionPathText == null)
            {
                Debug.LogError("Intersection path data is not available!");
                return;
            }
            
            int rndInd;
            if (_startIndex >= 0 && _startIndex < _intersectionPathText.Length)
            {
                rndInd = _startIndex;
                Debug.Log($"Using specified start index: {rndInd}");
            }
            else
            {
                rndInd = Random.Range(0, _intersectionPathText.Length);
                Debug.Log($"Using random start index: {rndInd}");
            }
            _url = _areaUrl + "/movie_part/" + _intersectionPathText[rndInd][20];
            _url = _url.TrimEnd();
            Debug.Log($"Selected start location - Index: {rndInd}, Video: {_intersectionPathText[rndInd][20]}");
            _fromLarge = new LargeIntersection(
                new FramePoint[] { new FramePoint(_intersectionPathText[rndInd][0], int.Parse(_intersectionPathText[rndInd][1])), new FramePoint(_intersectionPathText[rndInd][2], int.Parse(_intersectionPathText[rndInd][3])), new FramePoint(_intersectionPathText[rndInd][4], int.Parse(_intersectionPathText[rndInd][5])), new FramePoint(_intersectionPathText[rndInd][6], int.Parse(_intersectionPathText[rndInd][7])) },
                new Coordinate(float.Parse(_intersectionPathText[rndInd][8]), float.Parse(_intersectionPathText[rndInd][9])));
            _toLarge = new LargeIntersection(
                new FramePoint[] { new FramePoint(_intersectionPathText[rndInd][10], int.Parse(_intersectionPathText[rndInd][11])), new FramePoint(_intersectionPathText[rndInd][12], int.Parse(_intersectionPathText[rndInd][13])), new FramePoint(_intersectionPathText[rndInd][14], int.Parse(_intersectionPathText[rndInd][15])), new FramePoint(_intersectionPathText[rndInd][16], int.Parse(_intersectionPathText[rndInd][17])) },
                new Coordinate(float.Parse(_intersectionPathText[rndInd][18]), float.Parse(_intersectionPathText[rndInd][19])));
            _jsonUrl = MovieUrlMaker.ChangeURLMovieToJson(_url);
            
            // Final initialization complete.
            FinalizeLargeIntersectionDict();
            _initialized = true;
            Debug.Log("GlobalInfo initialization fully completed!");
        }

        public async static UniTask Initialize(string areaName)
        {
            await InitializeBaseData(areaName);
            SetStartLocationAndFinalize();
        }
        
        // Separate LargeIntersectionReferenceDict initialization.
        private static void FinalizeLargeIntersectionDict()
        {
            // var partList = _largeIntersectionList.Where(p => p.Points.Where(q => q.Path == "hongo_44-43").Count() > 0).ToArray();
            _largeIntersectionReferenceDict = new Dictionary<LargeIntersection, Dictionary<string, LargeIntersection>>();
            foreach (var large in _largeIntersectionList)
            {
                _largeIntersectionReferenceDict[large] = new Dictionary<string, LargeIntersection>();
                foreach (var point in large.Points)
                {
                    var frame = point.Frame;
                    if (frame == -1)
                    {
                        _largeIntersectionReferenceDict[large][point.Path] = null;
                    }
                    else
                    {
                        var afters = _largeIntersectionList.Where(p => p.Points.Where(q => q.Path == point.Path && q.Frame > frame).Count() > 0);
                        var nearest = afters.OrderBy(p => p.Points.Where(q => q.Path == point.Path).FirstOrDefault().Frame).FirstOrDefault();
                        // nearest can be null.
                        _largeIntersectionReferenceDict[large][point.Path] = nearest;
                    }
                }
            }
            Debug.Log("LargeIntersectionReferenceDict setting finished!");
        }
        private async static UniTask SetCoordinateDict()
        {
            _coordinateDict = new Dictionary<string, List<(Coordinate, Quaternion)>>();

            // TODO: Update file names on the server.
            var csvFileUrl = _areaUrl + "/unity_data/path_analyze.csv";
            var coordinateText = await CsvReader.DownloadCsv(csvFileUrl, ',');
            var path = "";
            for (var i = 0; i < coordinateText.Length; ++i)
            {
                if (coordinateText[i][0][0] == '-')
                {
                    path = coordinateText[i][0].Substring(1);
                    _coordinateDict[path] = new List<(Coordinate, Quaternion)>();
                }
                else
                {
                    var coord = new Coordinate(float.Parse(coordinateText[i][1]), float.Parse(coordinateText[i][2]));
                    var q = new Quaternion(float.Parse(coordinateText[i][3]), float.Parse(coordinateText[i][4]), float.Parse(coordinateText[i][5]), float.Parse(coordinateText[i][6]));
                    _coordinateDict[path].Add((coord, q));
                }
            }
        }

        // Apply coordinate scaling in real meters (x=longitude direction, z=latitude direction).
        private static void ApplyMeterScale()
        {
            // Calculate at the center latitude because distance per degree of longitude depends on latitude.
            float latDeg = _centerPosition.z;
            var metersPerDeg = GetMetersPerDegree(latDeg);
            _coordinateScale = new Vector3(metersPerDeg.x, 1f, metersPerDeg.y);
        }

        // Given a latitude, approximate meters per degree for longitude and latitude.
        private static Vector2 GetMetersPerDegree(float latitudeDeg)
        {
            float latRad = latitudeDeg * Mathf.Deg2Rad;
            float mPerDegLat = 111132.954f - 559.822f * Mathf.Cos(2f * latRad) + 1.175f * Mathf.Cos(4f * latRad) - 0.0023f * Mathf.Cos(6f * latRad);
            float mPerDegLon = 111412.84f * Mathf.Cos(latRad) - 93.5f * Mathf.Cos(3f * latRad) + 0.118f * Mathf.Cos(5f * latRad);
            return new Vector2(mPerDegLon, mPerDegLat);
        }

    }
}

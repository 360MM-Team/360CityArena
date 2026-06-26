using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.IO;
using System.Linq;
using System.Net;
using UnityEngine;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace MovieMap.Core
{
    public static class FileReader
    {
        // Check whether the file exists.
        public async static UniTask<bool> CheckFileExist(string url, CancellationToken token)
        {
            try
            {
                UnityWebRequest request = UnityWebRequest.Get(url);
                await request.SendWebRequest().WithCancellation(token);
                if (request.result == UnityWebRequest.Result.Success)
                {
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public async static UniTask<byte[]> LoadFileAsByte(string url)
        {
            try
            {
                UnityWebRequest request = UnityWebRequest.Get(url);
                await request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    return request.downloadHandler.data;
                }
            }
            catch
            {

            }
            return null;
        }
    }

    public static class CsvReader
    {
        // Read the CSV file at the specified URL and return it as a 2D string array.
        public async static UniTask<string[][]> DownloadCsv(string url, char delimiter)
        {
            UnityWebRequest req = UnityWebRequest.Get(url);
            Debug.Log($"Downloading CSV from: {url}");
            try
            {
                await req.SendWebRequest();
            }
            catch (Exception ex)
            {
                Debug.Log($"Error: {ex.Message}. Target URL: {url}");
                return null;
            }
            string txt = req.downloadHandler.text;
            var lines = txt.Split('\n').Where(p => p.Split(delimiter).Length > 0 && p.Split(delimiter)[0] != "").ToArray();
            var result = new string[lines.Length][];
            for (int i = 0; i < lines.Length; i++)
            {
                var words = lines[i].Split(delimiter);
                result[i] = words;
            }
            return result;
        }

        // Convert the 2D string array read from large_intersection.csv into a LargeIntersection array.
        public static List<LargeIntersection> TransToLargeIntersectionList(string[][] csvText)
        {
            var result = new List<LargeIntersection>();
            for (var i = 0; i < csvText.Length; i++)
            {
                result.Add(new LargeIntersection(
                    new FramePoint[] { new FramePoint(csvText[i][0], int.Parse(csvText[i][1])), new FramePoint(csvText[i][2], int.Parse(csvText[i][3])), new FramePoint(csvText[i][4], int.Parse(csvText[i][5])), new FramePoint(csvText[i][6], int.Parse(csvText[i][7])) },
                    new Coordinate(float.Parse(csvText[i][8]), float.Parse(csvText[i][9]))));
            }
            return result;
        }
        // Convert strings read from assigned_intersection.csv into an intersection dictionary.
        public static Dictionary<LargeIntersection, Dictionary<LargeIntersection, string>> TransToLargeIntersectionDictionary(string[][] csvText, string video_path)
        {
            var result = new Dictionary<LargeIntersection, Dictionary<LargeIntersection, string>>();
            for (var i = 0; i < csvText.Length; i++)
            {
                var fromLarge = new LargeIntersection(
                    new FramePoint[] { new FramePoint(csvText[i][0], int.Parse(csvText[i][1])), new FramePoint(csvText[i][2], int.Parse(csvText[i][3])), new FramePoint(csvText[i][4], int.Parse(csvText[i][5])), new FramePoint(csvText[i][6], int.Parse(csvText[i][7])) },
                    new Coordinate(float.Parse(csvText[i][8]), float.Parse(csvText[i][9])));
                var toLarge = new LargeIntersection(
                    new FramePoint[] { new FramePoint(csvText[i][10], int.Parse(csvText[i][11])), new FramePoint(csvText[i][12], int.Parse(csvText[i][13])), new FramePoint(csvText[i][14], int.Parse(csvText[i][15])), new FramePoint(csvText[i][16], int.Parse(csvText[i][17])) },
                    new Coordinate(float.Parse(csvText[i][18]), float.Parse(csvText[i][19])));
                string url = csvText[i][20];
                url = url.TrimEnd();
                if (result.ContainsKey(fromLarge))
                {
                    result[fromLarge][toLarge] = video_path + url;
                }
                else
                {
                    result[fromLarge] = new Dictionary<LargeIntersection, string>();
                    result[fromLarge][toLarge] = video_path + url;
                }
            }
            return result;
        }
    }
}

using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.UI;
using MovieMap.Core;

namespace MovieMap.UI
{
    public class ArrowButton: MonoBehaviour 
    {
        public List<string> Urls = new List<string>();
        public List<string> Jsons = new List<string>();
        [System.NonSerialized]
        public LargeIntersection FromLarge, ToLarge;
    }
}

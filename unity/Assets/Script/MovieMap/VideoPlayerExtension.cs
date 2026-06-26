using System.Collections;
using System.Collections.Generic;
using UnityEngine;


// Workaround for Unity VideoPlayer sometimes rendering the first few frames as black.
public class VideoPlayerExtension : MonoBehaviour
{
    public int StartFrame;
}

using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using System.Threading;

public static class Extension
{
    // Play and wait until playback starts.
    public static async UniTask PlayAsync(this VideoPlayer videoPlayer, CancellationToken token)
    {
        videoPlayer.Play();

        await UniTask.WaitWhile(() => !videoPlayer.isPlaying, cancellationToken: token);
        // await UniTask.WaitWhile(() => videoPlayer.isPrepared);
    }
}

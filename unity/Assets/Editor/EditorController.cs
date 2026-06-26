// Assets/Editor/ExternalControlServer.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

[InitializeOnLoad]
public static class EditorController
{
    private static HttpListener _listener;
    private static Thread _thread;
    private static volatile bool _stop;
    private static readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();

    static EditorController()
    {
        StartServer();
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        EditorApplication.quitting += OnQuit;
        EditorApplication.update += PumpMainThreadActions;
    }

    [MenuItem("External Control/Restart Server")]
    public static void RestartMenu()
    {
        Shutdown();
        StartServer();
    }

    private static void StartServer()
    {
        try
        {
            _stop = false;
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:5005/");
            _listener.Prefixes.Add("http://localhost:5005/");
            _listener.Start();

            _thread = new Thread(ServerLoop) { IsBackground = true, Name = "EditorController" };
            _thread.Start();

            Debug.Log("[EditorController] Server started: http://localhost:5005/");
        }
        catch (Exception e)
        {
            Debug.LogError("[EditorController] Failed to start server: " + e);
        }
    }

    private static void ServerLoop()
    {
        while (!_stop)
        {
            HttpListenerContext ctx = null;
            try
            {
                ctx = _listener.GetContext();
            }
            catch
            {
                if (_stop) break;
                continue;
            }

            try
            {
                var req = ctx.Request;
                var res = ctx.Response;
                var path = req.Url.AbsolutePath;

                // Start the game on play.
                if (req.HttpMethod == "POST" && path == "/play")
                {
                    EnqueueOnMain(() =>
                    {
                        AutoPlayGame.PlayGame();
                    });
                    WriteText(res, 200, "ok\n");
                }
                // Stop the game on stop.
                else if (req.HttpMethod == "POST" && path == "/stop")
                {
                    EnqueueOnMain(() => EditorApplication.isPlaying = false);
                    WriteText(res, 200, "ok\n");
                }
                else
                {
                    WriteText(res, 404, "not found\n");
                }
                res.Close();
            }
            catch { }
        }
    }

    private static void EnqueueOnMain(Action a) => _mainThread.Enqueue(a);

    private static void PumpMainThreadActions()
    {
        while (_mainThread.TryDequeue(out var a))
        {
            try { a(); } catch (Exception e) { Debug.LogError(e); }
        }
    }

    // Helper method for writing responses.
    private static void WriteText(HttpListenerResponse res, int code, string body, string contentType = "text/plain; charset=utf-8")
    {
        res.StatusCode = code;
        res.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(body);
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static void OnBeforeReload() => Shutdown();
    private static void OnQuit() => Shutdown();

    private static void Shutdown()
    {
        _stop = true;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        if (_thread != null && _thread.IsAlive)
        {
            try { _thread.Join(200); } catch { }
            _thread = null;
        }
        Debug.Log("[EditorController] Server stopped");
    }
}
#endif

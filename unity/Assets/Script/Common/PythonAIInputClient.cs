using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Video;
using MovieMap.Service;
using UnityEngine.ProBuilder.Shapes;
using MovieMap.UI;

namespace MovieMap.Core
{
	public class PythonAIInputClient : MonoBehaviour
	{
		[Header("Python Server Settings")]
		[SerializeField] private bool enable = true;
		[SerializeField] private string host = "127.0.0.1";
		[SerializeField] private int port = 8765;
		[SerializeField] private float requestInterval = 0.8f; // seconds between AI queries
		[SerializeField] private Camera sourceCamera;
		[SerializeField] private int width = 640;
		[SerializeField] private int height = 360;
		[SerializeField] private MoviePlayer moviePlayer; // Optional reference for progress monitoring
		[SerializeField] private MovieChanger movieChanger; // Optional reference for coordinate lookup
		[SerializeField] private ArrowManager arrowManager; // Optional reference for selection completion monitoring
		[SerializeField] private CameraRotator cameraRotator;
		[SerializeField] private float minMoveHoldSeconds = 5.0f;
		[SerializeField] private float minSelectHoldSeconds = 0.5f;
		[SerializeField] private float maxActionWaitSeconds = 5f;
		[SerializeField] private int maxRetryAttempts = 3;
		[SerializeField] private float probeRetryInterval = 10.0f; // seconds between server probe attempts
		[Header("Map Integration")]
		[SerializeField] private bool includeMapImage = true; // Whether to include the map image
		[SerializeField] private int mapWidth = 640; // Map image width
		[SerializeField] private int mapHeight = 640; // Map image height
		[SerializeField] private bool useHighQualityMapCapture = true; // Use high-quality capture
		[SerializeField] private int playerIconSize = 25; // Player arrow size in pixels
		[Header("Trajectory")]
		[SerializeField] [Range(1f, 20f)] private float trajectoryLineWidth = 4f; // Trajectory line width in pixels
		[Header("Debug")]
		[SerializeField] private bool verboseLog = false;
		[SerializeField] private bool logLocationInfo = false; // Log location info regardless of enable
		[SerializeField] private float locationLogInterval = 2.0f; // Location log interval in seconds
		[SerializeField] private float trajectorySampleInterval = 0.5f; // Sampling interval when Python is unused
		[SerializeField] private bool preferProjectLogsDirectory = true; // Prefer saving under the Logs folder
		[Header("Readiness Gates")]
		[SerializeField] private bool waitForServerReady = true;
		[SerializeField] private bool waitForGlobalInitialized = true; // Wait for MovieMap import completion (GlobalInfo.Initialized)
		[SerializeField] private bool waitForVideoReady = true; // Wait until the first video is ready
		[SerializeField] private bool requestStartIndexFromPython = true; // Request the initial position when connecting to Python
		
		[Header("Start Location Settings")]
		[SerializeField]
		[Tooltip("Python無効時の開始位置（-1でランダム選択、0-606で指定地点）")]
		private int startIndex = -1; // -1: random selection, 0 or greater: specified location
		
		[Header("Per-Action Hold Durations")]
		[SerializeField] private bool usePerActionHoldSeconds = true;
		[SerializeField] private float holdWSeconds = 2.0f; // Seconds to hold forward
		[SerializeField] private float holdSSeconds = 0.8f;
		[SerializeField] private float holdASeconds = 0.6f;
		[SerializeField] private float holdDSeconds = 0.6f;
		[SerializeField] private float holdLeftSeconds = 0.5f;
		[SerializeField] private float holdRightSeconds = 0.5f;
		[SerializeField] private float holdUpSeconds = 0.5f;
		[SerializeField] private float holdQSeconds = 0.7f;
		[SerializeField] private float holdESeconds = 0.7f;
		[SerializeField] private float holdEnterSeconds = 0.3f;

		private float _nextTime;
		private Texture2D _captureTex;
		private RenderTexture _rt;
		private bool _waitingForResult;
		// Map-related variables.
		private GameObject _mapParent;
		private UnityEngine.UI.RawImage _mapImage;
		private UnityEngine.UI.RawImage _playerImage; // Player arrow
		private Texture2D _mapCaptureTex;
		private RenderTexture _mapRt;
        private Camera _mapCamera; // Virtual camera for capturing the full map
        private (float, float) _prevPlayerMapPos;
        private TrailDrawer _trailDrawer; // Draws the player trajectory
        private GameObject _mapArea;
		private float _actionStartTime;
		private string _lastToken;
		private readonly HashSet<KeyCode> _heldKeys = new HashSet<KeyCode>();
		private bool _serverReady;
		private bool _probingServer;
		private float _nextProbeTime; // cooldown for probing attempts
		private Coroutine _serialLoopRoutine;
		private readonly List<Vector2> _playerPath = new List<Vector2>(); // Player trajectory during the task as UV ratios
		private readonly List<TrajectorySample> _trajectorySamples = new List<TrajectorySample>(); // Detailed trajectory during the task
		private string _sessionId;
		private string _sessionStartIsoUtc;
		private bool _trajectorySavedToDisk;
		private float _nextTrajectorySampleTime;
		// Persistent connection management.
		private TcpClient _persistentClient;
		private NetworkStream _persistentStream;
		private StreamWriter _persistentWriter;
		private StreamReader _persistentReader;
		private bool _connectionEstablished;
		// logging state
		private bool _log_wait_enable;
		private bool _log_wait_global;
		private bool _log_wait_server;
		private bool _log_wait_video;
		private bool _log_wait_reflection;
		private float _overrideHoldSeconds; // Hold duration from AI, enabled when greater than 0
		private bool _initialPoseSynced; // Whether pose updates were awaited before the first map send
			
		// Initial position request state.
		private bool _startIndexRequested = false;
		private MovieMapInitializer _movieMapInitializer;
		
		// Location logging state.
		private float _lastLocationLogTime;

		/// <summary>
		/// Returns whether the Python server is enabled.
		/// </summary>
		public bool IsEnabled()
		{
			return enable;
		}

		/// <summary>
		/// Gets the start location index.
		/// </summary>
		public int GetStartIndex()
		{
			return startIndex;
		}

		private void Awake()
		{
			var nowUtc = DateTime.UtcNow;
			_sessionId = nowUtc.ToString("yyyyMMdd_HHmmss");
			_sessionStartIsoUtc = nowUtc.ToString("o");
			_trajectorySavedToDisk = false;
			if (sourceCamera == null)
			{
				sourceCamera = Camera.main;
			}
			if (moviePlayer == null) moviePlayer = FindAnyObjectByType<MoviePlayer>();
			if (movieChanger == null) movieChanger = FindAnyObjectByType<MovieChanger>();
			if (arrowManager == null) arrowManager = FindAnyObjectByType<ArrowManager>();
			if (cameraRotator == null) cameraRotator = FindAnyObjectByType<CameraRotator>();
			// Get MovieMapInitializer.
			if (requestStartIndexFromPython)
			{
				_movieMapInitializer = FindAnyObjectByType<MovieMapInitializer>();
				if (_movieMapInitializer == null)
				{
					LogWarn("MovieMapInitializer not found, start index request will be disabled");
					requestStartIndexFromPython = false;
				}
			}
			_rt = new RenderTexture(width, height, 16);
			_captureTex = new Texture2D(width, height, TextureFormat.RGB24, false);
			// Initialize map-related resources.
			if (includeMapImage)
			{
                _mapRt = new RenderTexture(mapWidth, mapHeight, 16);
				if (useHighQualityMapCapture)
				{
					// High-quality settings.
					_mapRt.antiAliasing = 4; // Anti-aliasing
					_mapRt.filterMode = FilterMode.Bilinear; // Bilinear filtering
				}
				// Use RGBA32 for high-quality capture.
				var format = useHighQualityMapCapture ? TextureFormat.RGBA32 : TextureFormat.RGB24;
				_mapCaptureTex = new Texture2D(mapWidth, mapHeight, format, false);
				// Get the map UI object.
				_mapParent = GameObject.Find("MapParent");
				if (_mapParent != null)
				{
					_mapImage = _mapParent.GetComponentInChildren<UnityEngine.UI.RawImage>();
				}
				// Get the player arrow.
				var playerImageObj = GameObject.Find("PlayerImage");
				if (playerImageObj != null)
				{
					_playerImage = playerImageObj.GetComponent<UnityEngine.UI.RawImage>();
				}
			}
			
			_serverReady = !waitForServerReady; // Treat as ready initially when not waiting.
			DebugLog($"Awake: serverReady={_serverReady} waitServer={waitForServerReady} waitGlobal={waitForGlobalInitialized} waitVideo={waitForVideoReady} includeMap={includeMapImage}");
		}

		private void OnDestroy()
		{
			SaveTrajectoryArtifacts("destroy");
			// Clean up the persistent connection.
			ClosePersistentConnection();
			
			if (_rt != null)
			{
				_rt.Release();
				DestroyImmediate(_rt);
			}
			if (_captureTex != null)
			{
				DestroyImmediate(_captureTex);
			}
			
			// Clean up map-related resources.
			if (_mapRt != null)
			{
				_mapRt.Release();
				DestroyImmediate(_mapRt);
			}
			if (_mapCaptureTex != null)
			{
				DestroyImmediate(_mapCaptureTex);
			}
		}

		private void Start()
		{
			_nextTrajectorySampleTime = Time.time;
			if (_serialLoopRoutine == null)
			{
				Log("Start: launching AISerialLoop");
				_serialLoopRoutine = StartCoroutine(AISerialLoop());
			}
			_lastLocationLogTime = Time.time;
		}

		private void Update()
		{
			// Log location info regardless of enable.
			if (logLocationInfo && Time.time - _lastLocationLogTime >= locationLogInterval)
			{
				LogCurrentLocation();
				_lastLocationLogTime = Time.time;
			}

			// Save the trajectory at a fixed interval even when Python is not used.
			if (Time.time >= _nextTrajectorySampleTime)
			{
				if (TryCaptureTrajectoryObservation(out _, true))
				{
					_nextTrajectorySampleTime = Time.time + Mathf.Max(0.1f, trajectorySampleInterval);
				}
				else
				{
					_nextTrajectorySampleTime = Time.time + trajectorySampleInterval;
				}
			}
		}

		private void OnDisable()
		{
			if (_serialLoopRoutine != null)
			{
				DebugLog("OnDisable: stopping AISerialLoop");
				StopCoroutine(_serialLoopRoutine);
				_serialLoopRoutine = null;
			}
		}

		private IEnumerator AISerialLoop()
		{
			while (true)
			{
				// Wait until enabled.
				while (!enable)
				{
					if (!_log_wait_enable) { DebugLog("Loop: waiting enable==true"); _log_wait_enable = true; }
					yield return null;
				}
				if (_log_wait_enable) { DebugLog("Loop: enable==true"); _log_wait_enable = false; }

				// Readiness gate: MovieMap initialization.
				if (waitForGlobalInitialized)
				{
					// When initial position requests are enabled, wait until BaseDataInitialized.
					bool initializationReady = requestStartIndexFromPython ? 
						GlobalInfo.BaseDataInitialized : 
						GlobalInfo.Initialized;
						
					if (!initializationReady)
					{
						if (!_log_wait_global) { DebugLog("Loop: waiting GlobalInfo initialization"); _log_wait_global = true; }
						yield return null;
						continue;
					}
				}
				if (_log_wait_global) { DebugLog("Loop: GlobalInfo initialization OK"); _log_wait_global = false; }

				// Readiness gate: Python server.
				if (waitForServerReady && !_serverReady)
				{
					// Start a probe only when cooldown has passed
					if (!_probingServer && Time.time >= _nextProbeTime)
					{
						DebugLog("Loop: probing server...");
						StartCoroutine(ProbeServerReady());
						_nextProbeTime = Time.time + probeRetryInterval;
					}
					if (!_log_wait_server) { DebugLog("Loop: waiting server ready"); _log_wait_server = true; }
					yield return new WaitForSeconds(0.5f);
					continue;
				}
				if (_log_wait_server) { DebugLog("Loop: server ready"); _log_wait_server = false; }
				
				// Establish the persistent connection after the server is ready.
				if (!_connectionEstablished && _serverReady)
				{
					DebugLog("Loop: establishing persistent connection...");
					yield return EstablishPersistentConnection();
				}

				// Send the initial position request once.
				if (requestStartIndexFromPython && !_startIndexRequested && _connectionEstablished && GlobalInfo.BaseDataInitialized)
				{
					DebugLog("Loop: conditions met for start index request, sending...");
					_startIndexRequested = true;
					StartCoroutine(RequestStartIndexFromPython());
				}
				else if (requestStartIndexFromPython && !_startIndexRequested)
				{
					DebugLog($"Loop: start index request conditions - connected:{_connectionEstablished}, baseData:{GlobalInfo.BaseDataInitialized}");
				}

				// Readiness gate: video readiness.
				// In Python connection wait mode, wait until GlobalInfo.Initialized completes.
				if (waitForVideoReady)
				{
					bool videoConditionMet = false;
					
					if (requestStartIndexFromPython)
					{
						// In Python connection wait mode, check video after GlobalInfo.Initialized completes.
						if (GlobalInfo.Initialized)
						{
							videoConditionMet = IsVideoReady();
						}
						else
						{
							if (!_log_wait_video) { DebugLog("Loop: waiting GlobalInfo.Initialized for video loading"); _log_wait_video = true; }
							yield return null;
							continue;
						}
					}
					else
					{
						// In legacy mode, check the video directly.
						videoConditionMet = IsVideoReady();
					}
					
					if (!videoConditionMet)
					{
						if (!_log_wait_video) { DebugLog("Loop: waiting video ready"); _log_wait_video = true; }
						yield return null;
						continue;
					}
				}
				if (_log_wait_video) { DebugLog("Loop: video ready"); _log_wait_video = false; }

				// Wait for application, which is the core of serial execution.
				if (_waitingForResult)
				{
					if (!_log_wait_reflection) { DebugLog($"Loop: waiting reflection token={_lastToken}"); _log_wait_reflection = true; }
					while (true)
					{
						bool timeout = Time.time - _actionStartTime > maxActionWaitSeconds;
						if (IsActionReflected() || timeout)
						{
							_waitingForResult = false;
							// Clear held key state when the action ends.
							ClearHeldKeys();
							if (timeout) DebugLog($"Loop: reflection timeout token={_lastToken}"); else DebugLog($"Loop: reflection done token={_lastToken}");
							_log_wait_reflection = false;
							break;
						}
						yield return null;
					}
					// Send the next request immediately after application completes.
					continue;
				}

				// Send, receive, and inject; sleep briefly and retry on failure.
				if (requestInterval > 0f && Time.time < _nextTime)
				{
					yield return null;
					continue;
				}

				DebugLog("Loop: request action with retry");
				yield return RequestActionWithRetry();
				_nextTime = Time.time + requestInterval;
				if (!_waitingForResult)
				{
					// Throttle when no decision was applied, such as on missing responses.
					DebugLog("Loop: no waiting state after request (no decision?) throttle 0.2s");
					yield return new WaitForSeconds(0.2f);
				}
			}
		}

		private bool IsVideoReady()
		{
			if (moviePlayer == null) return false;
			var sphere = moviePlayer.CurrentSphere;
			if (sphere == null) return false;
			var vp = sphere.GetComponent<VideoPlayer>();
			if (vp == null) return false;
			// Treat as ready if any condition is true.
			if (vp.isPrepared) return true;
			if (vp.isPlaying) return true;
			if (vp.frameCount > 0) return true;
			if (vp.texture != null) return true;
			return false;
		}

		private IEnumerator ProbeServerReady()
		{
			_probingServer = true;
			TcpClient client = new TcpClient();
			IAsyncResult ar = client.BeginConnect(host, port, null, null);
			float start = Time.time;
			while (!ar.IsCompleted && (Time.time - start) < 3.0f) { yield return null; }
			if (!ar.IsCompleted)
			{
				try { client.Close(); } catch { }
				_probingServer = false;
				LogWarn("Probe: connect timeout");
				yield break;
			}
			try { client.EndConnect(ar); }
			catch { _probingServer = false; LogWarn("Probe: connect failed"); yield break; }

			using (client)
			using (var stream = client.GetStream())
			using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
			using (var reader = new StreamReader(stream, Encoding.UTF8))
			{
				writer.NewLine = "\n"; writer.AutoFlush = true;
				writer.WriteLine(JsonUtility.ToJson(new PingRequest()));
				string resp = null; Exception err = null;
				var th = new System.Threading.Thread(() => { try { resp = reader.ReadLine(); } catch (Exception ex) { err = ex; } });
				th.IsBackground = true; th.Start();
				float ws = Time.time;
				while ((Time.time - ws) < 2.0f)
				{
					if (err != null) break;
					if (!string.IsNullOrEmpty(resp)) break;
					yield return null;
				}
				if (!string.IsNullOrEmpty(resp))
				{
					_serverReady = true;
					Log("Probe: server responded OK");
				}
			}
			_probingServer = false;
		}
		
		private IEnumerator RequestStartIndexFromPython()
		{
			DebugLog("RequestStartIndex: sending start index request to Python");
			
			if (!_connectionEstablished)
			{
				LogWarn("RequestStartIndex: no connection available");
				yield break;
			}
			
			if (_persistentClient?.Connected != true)
			{
				LogWarn("RequestStartIndex: persistent client is not connected");
				ClosePersistentConnection();
				yield break;
			}
			
			// Get the maximum index count.
			int maxIndex = GlobalInfo.IntersectionPathText?.Length ?? 607; // Default value
			
			var request = new StartIndexRequest
			{
				max_index = maxIndex - 1 // Subtract 1 because this is 0-based.
			};
			
			string requestJson = JsonUtility.ToJson(request);
			
			try
			{
				_persistentWriter.WriteLine(requestJson);
				DebugLog($"RequestStartIndex: sent request, max_index={maxIndex - 1}");
			}
			catch (Exception ex)
			{
				LogWarn($"RequestStartIndex: send failed - {ex.Message}");
				yield break;
			}
			
			// Wait for the response.
			string response = null;
			Exception readErr = null;
			var th = new System.Threading.Thread(() =>
			{
				try { response = _persistentReader.ReadLine(); }
				catch (Exception ex) { readErr = ex; }
			});
			th.IsBackground = true; th.Start();
			
			float waitStart = Time.time;
			while ((Time.time - waitStart) < 10f) // 10-second timeout
			{
				if (readErr != null)
				{
					LogWarn($"RequestStartIndex: read error - {readErr.Message}");
					yield break;
				}
				if (!string.IsNullOrEmpty(response)) break;
				yield return null;
			}
			
			if (string.IsNullOrEmpty(response))
			{
				LogWarn("RequestStartIndex: timeout or empty response");
				yield break;
			}
			
			DebugLog($"RequestStartIndex: received response ({DescribePayload(response)})");
			
			// Parse the response.
			try
			{
				var startIndexResponse = JsonUtility.FromJson<StartIndexResponse>(response);
				if (startIndexResponse != null && startIndexResponse.success)
				{
					int startIndex = startIndexResponse.start_index;
					bool showMap = startIndexResponse.show_map;
					Log($"RequestStartIndex: received start index {startIndex} (show_map={showMap})");

					MapVisibilitySettings.SetMapAllowed(showMap);
					if (!showMap)
					{
						Log("RequestStartIndex: map visibility disabled by Python response");
					}
					
					// Notify MovieMapInitializer.
					if (_movieMapInitializer != null)
					{
						DebugLog($"RequestStartIndex: notifying MovieMapInitializer with start index {startIndex}");
						_movieMapInitializer.OnPythonConnected(startIndex);
						DebugLog("RequestStartIndex: MovieMapInitializer notification completed");
					}
					else
					{
						LogWarn("RequestStartIndex: MovieMapInitializer is null, cannot notify");
					}
				}
				else
				{
					LogWarn($"RequestStartIndex: invalid or unsuccessful response ({DescribePayload(response)})");
				}
			}
			catch (Exception ex)
			{
				LogWarn($"RequestStartIndex: JSON parse error - {ex.Message} ({DescribePayload(response)})");
			}
		}

		private void ClosePersistentConnection()
		{
			DebugLog("ClosePersistentConnection: cleaning up connection");
			_connectionEstablished = false;
			
			try { _persistentReader?.Close(); } catch { }
			try { _persistentWriter?.Close(); } catch { }
			try { _persistentStream?.Close(); } catch { }
			try { _persistentClient?.Close(); } catch { }
			
			_persistentReader = null;
			_persistentWriter = null;
			_persistentStream = null;
			_persistentClient = null;
		}

		private IEnumerator EstablishPersistentConnection()
		{
			if (_connectionEstablished && _persistentClient?.Connected == true)
			{
				DebugLog("EstablishPersistentConnection: connection already established and active");
				yield break; // Already connected
			}

			DebugLog("EstablishPersistentConnection: connecting to server");
			ClosePersistentConnection(); // Clear the existing connection.
			
			_persistentClient = new TcpClient();
			IAsyncResult ar = _persistentClient.BeginConnect(host, port, null, null);
			float start = Time.time;
			while (!ar.IsCompleted && (Time.time - start) < 3.0f) { yield return null; }
			
			if (!ar.IsCompleted)
			{
				try { _persistentClient.Close(); } catch { }
				LogWarn("EstablishPersistentConnection: connect timeout");
				yield break;
			}
			
			try 
			{ 
				_persistentClient.EndConnect(ar); 
				_persistentStream = _persistentClient.GetStream();
				_persistentWriter = new StreamWriter(_persistentStream, new UTF8Encoding(false));
				_persistentReader = new StreamReader(_persistentStream, Encoding.UTF8);
				_persistentWriter.NewLine = "\n";
				_persistentWriter.AutoFlush = true;
				_connectionEstablished = true;
				Log($"EstablishPersistentConnection: connection established, requestStartIndex={requestStartIndexFromPython}");
			}
			catch (Exception ex) 
			{ 
				LogWarn($"EstablishPersistentConnection: connect failed - {ex.Message}"); 
				ClosePersistentConnection();
			}
		}

		private IEnumerator RequestActionWithRetry()
		{
			for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
			{
				// Attempt to connect if the connection is not established.
				if (!_connectionEstablished)
				{
					DebugLog("RequestAction: establishing persistent connection...");
					yield return EstablishPersistentConnection();
					DebugLog($"RequestAction: connection establishment result = {_connectionEstablished}");
				}
				
				// Skip if the connection is not established.
				if (!_connectionEstablished)
				{
					DebugWarn($"RequestAction: failed to establish connection on attempt {attempt}");
					if (attempt < maxRetryAttempts)
					{
						yield return new WaitForSeconds(0.5f);
					}
					continue;
				}
				
				yield return RequestActionOnce();
				
				if (_waitingForResult)
				{
					// Stop retrying after success.
					DebugLog($"RequestAction: success on attempt {attempt}");
					yield break;
				}
				
				if (attempt < maxRetryAttempts)
				{
					DebugLog($"RequestAction: attempt {attempt} failed, retrying in 0.5s");
					// Reset the connection after failure.
					ClosePersistentConnection();
					yield return new WaitForSeconds(0.5f);
				}
				else
				{
					LogWarn($"RequestAction: all {maxRetryAttempts} attempts failed");
				}
			}
		}

		private IEnumerator RequestActionOnce()
		{
			if (!_connectionEstablished)
			{
				LogWarn("Request: no persistent connection available");
				yield break;
			}
			
			// Check connection health.
			if (_persistentClient?.Connected != true)
			{
				LogWarn("Request: persistent connection is not active");
				ClosePersistentConnection();
				yield break;
			}
			
			// Before the first send, wait one frame for pose updates, especially rotation.
			if (!_initialPoseSynced)
			{
				yield return EnsureInitialPoseSynced();
			}
			string b64 = CaptureCameraBase64();
			if (string.IsNullOrEmpty(b64)) yield break;
			
			string mapB64 = CaptureMapBase64();
			float posX = 0f;
			float posZ = 0f;
			string segmentPath = "";
			if (TryCaptureTrajectoryObservation(out var observation))
			{
				posX = observation.position.x;
				posZ = observation.position.z;
				segmentPath = observation.segmentPath;
			}
			
			// Build the request using a DTO because JsonUtility cannot serialize anonymous types.
			DebugLog($"Request: captured image len={b64.Length}" + (mapB64 != null ? $", map len={mapB64.Length}" : ", no map") + $", pos=({posX:F2}, {posZ:F2}), path={segmentPath}");
			var req = new ActRequest
			{
				image_b64 = b64,
				map_image_b64 = mapB64,
				position_x = posX,
				position_z = posZ,
				segment_path = segmentPath
			};
			string line = JsonUtility.ToJson(req);
			
			// Send the request.
			bool sendSuccess = false;
			try
			{
				_persistentWriter.WriteLine(line);
				DebugLog("Request: sent JSON line via persistent connection");
				sendSuccess = true;
			}
			catch (Exception ex)
			{
				LogWarn($"Request: send failed - {ex.Message}");
				ClosePersistentConnection();
				yield break;
			}
			
			if (!sendSuccess) yield break;
			
			// Wait for the response.
			string resp = null;
			Exception readErr = null;
			var th = new System.Threading.Thread(() =>
			{
				try { resp = _persistentReader.ReadLine(); }
				catch (Exception ex) { readErr = ex; }
			});
			th.IsBackground = true; th.Start();
			DebugLog("Response: waiting...");
			float ws = Time.time;
			float lastLogTime = ws;
			while ((Time.time - ws) < 180f) // 180-second timeout
			{
				if (readErr != null) 
				{
					LogWarn($"Response: read error - {readErr.Message}");
					ClosePersistentConnection();
					yield break;
				}
				if (!string.IsNullOrEmpty(resp)) break;
				
				// Log the wait status every 5 seconds.
				if (Time.time - lastLogTime > 5f)
				{
					DebugLog($"Response: still waiting... ({Time.time - ws:F1}s elapsed)");
					lastLogTime = Time.time;
				}
				
				yield return null;
			}
			if (string.IsNullOrEmpty(resp) && readErr == null)
			{
				LogWarn("Response: timeout waiting for server response");
			}
			if (string.IsNullOrEmpty(resp)) 
			{
				LogWarn("Response: empty or null response received");
				ClosePersistentConnection();
				yield break;
			}
			DebugLog($"Response: received len={resp.Length}");
			
			// Parse the response, preferring the new format where action is a string.
			try
			{
				var parsedV2 = JsonUtility.FromJson<PythonEnvelopeV2>(resp);
				if (parsedV2 != null && parsedV2.success && !string.IsNullOrEmpty(parsedV2.action))
				{
					var act = parsedV2.action.Trim();
					if (!string.IsNullOrEmpty(act) && string.Equals(act, "ANSWER", StringComparison.OrdinalIgnoreCase))
					{
						Log("Response: ANSWER action received. Stopping Unity.");
						// Send the trajectory image to Python at task end so Python saves map_images_b64.
						try { SendFinalTrajectoryToPython(); } catch (Exception ex) { LogWarn($"Send final trajectory failed: {ex.Message}"); }
						// Explicitly release input and stop after closing the connection; Python exits autonomously.
						try { ClearHeldKeys(); } catch { }
						ClosePersistentConnection();
						SaveTrajectoryArtifacts("python_answer");
						StopUnity();
						yield break;
					}
					ApplyDecision(parsedV2.action);
					if (parsedV2.hold_seconds > 0f)
					{
						_overrideHoldSeconds = parsedV2.hold_seconds;
						DebugLog($"Apply: override hold_seconds={_overrideHoldSeconds:F2}");
					}
				}
				else
				{
					// Fallback to the old format: token plus action object.
					var parsedV1 = JsonUtility.FromJson<PythonEnvelopeV1>(resp);
					if (parsedV1 != null && parsedV1.success && (parsedV1.action != null || !string.IsNullOrEmpty(parsedV1.token)))
					{
						string token = !string.IsNullOrEmpty(parsedV1.token) ? parsedV1.token : InferTokenFromActionDTO(parsedV1.action);
						if (!string.IsNullOrEmpty(token))
						{
							ApplyDecision(token);
						}
						else
						{
							LogWarn($"Response: could not infer token from legacy action DTO ({DescribePayload(resp)})");
						}
					}
					else
					{
						LogWarn($"Response: parse failed or action missing ({DescribePayload(resp)})");
					}
				}
			}
			catch (Exception ex)
			{
				LogError($"Response: JSON parse exception - {ex.Message} ({DescribePayload(resp)})");
			}
		}

		// Before the first send, wait until these conditions are met so the arrow
		// in the first map capture is not fixed pointing upward:
		// 1) the video is loaded,
		// 2) playback has advanced to at least frame 1,
		// 3) UserLocationService rotation has moved away from its initial value (0,0,0,0).
		private IEnumerator EnsureInitialPoseSynced()
		{
			// Synchronization is unnecessary when map images are not sent.
			if (!includeMapImage)
			{
				_initialPoseSynced = true;
				yield break;
			}

			float deadline = Time.time + 2.0f; // Safety timeout: wait up to 2 seconds.
			// First wait for video loading to complete.
			while (movieChanger != null && !movieChanger.IsFirstMovieLoaded)
			{
				if (Time.time > deadline) break;
				yield return null;
			}

			// Wait until playback has advanced by at least one frame.
			while (movieChanger != null && movieChanger.CurrentFrame() <= 0)
			{
				if (Time.time > deadline) break;
				yield return null;
			}

			// Wait until UserLocationService rotation differs from the initial value (0,0,0,0).
			for (int i = 0; i < 30; i++) // Wait up to roughly 30 frames.
			{
				try
				{
					var uls = GetUserLocationService();
					if (uls != null)
					{
						var (_, rot) = uls.CurrentPositionState();
						if (rot.x != 0f || rot.y != 0f || rot.z != 0f || rot.w != 0f)
						{
							break;
						}
					}
				}
				catch { }
				if (Time.time > deadline) break;
				yield return null; // Advance one frame.
			}

			// Wait until the frame end so UI rotation and similar updates are applied.
			yield return new WaitForEndOfFrame();
			// Wait for one more frame end to stabilize display and state.
			yield return new WaitForEndOfFrame();
			_initialPoseSynced = true;
		}

		[Serializable]
		private class ActRequest
		{
			public string type = "act";
			public string image_b64;
			public string map_image_b64; // Base64 map image data
			public List<string> map_images_b64; // Multiple map images, such as the final trajectory
			public float position_x;
			public float position_z;
			public string segment_path;
		}
		
		[Serializable]
		private class StartIndexRequest
		{
			public string type = "get_start_index";
			public int max_index; // Maximum available index
		}
		
		[Serializable]
		private class StartIndexResponse
		{
			public bool success;
			public int start_index;
			public string error;
			public bool show_map = true;
		}
		
		
		private void InjectToInput()
		{
			var hold = new Dictionary<KeyCode, bool>();
			// hold-type: W/A/S/D/Q/E as needed; arrows as hold for selection UI
			hold[KeyCode.W] = _heldKeys.Contains(KeyCode.W);
			hold[KeyCode.S] = _heldKeys.Contains(KeyCode.S);
			hold[KeyCode.A] = _heldKeys.Contains(KeyCode.A);
			hold[KeyCode.D] = _heldKeys.Contains(KeyCode.D);
			hold[KeyCode.Q] = _heldKeys.Contains(KeyCode.Q);
			hold[KeyCode.E] = _heldKeys.Contains(KeyCode.E);
			hold[KeyCode.LeftArrow] = _heldKeys.Contains(KeyCode.LeftArrow);
			hold[KeyCode.RightArrow] = _heldKeys.Contains(KeyCode.RightArrow);
			hold[KeyCode.UpArrow] = _heldKeys.Contains(KeyCode.UpArrow);
			hold[KeyCode.Return] = _heldKeys.Contains(KeyCode.Return);
			RandomInputManager.InjectHoldStates(hold);
			
			// Inject KeyDown explicitly for arrow keys and Enter so press detection is independent of frame order.
			var downs = new HashSet<KeyCode>();
			if (hold[KeyCode.UpArrow]) downs.Add(KeyCode.UpArrow);
			if (hold[KeyCode.LeftArrow]) downs.Add(KeyCode.LeftArrow);
			if (hold[KeyCode.RightArrow]) downs.Add(KeyCode.RightArrow);
			if (hold[KeyCode.Return]) downs.Add(KeyCode.Return);
			RandomInputManager.InjectKeyDown(downs);
			var downsStr = downs.Count > 0
				? "[" + string.Join(",", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(downs, d => d.ToString()))) + "]"
				: "-";
			DebugLog($"Inject: holds=[W:{hold[KeyCode.W]} S:{hold[KeyCode.S]} A:{hold[KeyCode.A]} D:{hold[KeyCode.D]} Q:{hold[KeyCode.Q]} E:{hold[KeyCode.E]} L:{hold[KeyCode.LeftArrow]} R:{hold[KeyCode.RightArrow]} U:{hold[KeyCode.UpArrow]} Enter:{hold[KeyCode.Return]}] downs={downsStr}");
		}
		
		private void ApplyDecision(string token)
		{
			// Clear AI overrides for each new decision.
			_overrideHoldSeconds = 0f;
			_lastToken = (token ?? "").Trim().ToUpperInvariant();
			DebugLog($"Decision: token={_lastToken}");
			// Update held keys.
			_heldKeys.Clear();
			switch (_lastToken)
			{
				case "W":
					_heldKeys.Add(KeyCode.W);
					break;
				case "LEFT":
					_heldKeys.Add(KeyCode.LeftArrow);
					break;
				case "RIGHT":
					_heldKeys.Add(KeyCode.RightArrow);
					break;
				case "UP":
					_heldKeys.Add(KeyCode.UpArrow);
					break;
				case "S":
					_heldKeys.Add(KeyCode.S);
					break;
				case "A":
					_heldKeys.Add(KeyCode.A);
					break;
				case "D":
					_heldKeys.Add(KeyCode.D);
					break;
				case "Q":
					_heldKeys.Add(KeyCode.Q);
					break;
				case "E":
					_heldKeys.Add(KeyCode.E);
					break;
				case "ENTER":
					_heldKeys.Add(KeyCode.Return);
					break;
			}
			// Inject input.
			InjectToInput();
			// Start waiting for application.
			_actionStartTime = Time.time;
			_waitingForResult = true;
			DebugLog("Decision: waiting for reflection start");
		}
		
		private void ClearHeldKeys()
		{
			DebugLog($"ClearHeldKeys: clearing {_heldKeys.Count} held keys");
			_heldKeys.Clear();
			// Reflect all keys as released in the input system.
			var hold = new Dictionary<KeyCode, bool>();
			hold[KeyCode.W] = false;
			hold[KeyCode.S] = false;
			hold[KeyCode.A] = false;
			hold[KeyCode.D] = false;
			hold[KeyCode.Q] = false;
			hold[KeyCode.E] = false;
			hold[KeyCode.LeftArrow] = false;
			hold[KeyCode.RightArrow] = false;
			hold[KeyCode.UpArrow] = false;
			hold[KeyCode.Return] = false;
			RandomInputManager.InjectHoldStates(hold);
			DebugLog("ClearHeldKeys: all keys released");
		}
		
		private bool IsActionReflected()
		{
			// Simple criteria for deciding that the action has been applied.
			switch (_lastToken)
			{
				case "W":
					// Forward, preferring the AI override when present.
					float wHold = _overrideHoldSeconds > 0f ? _overrideHoldSeconds : (usePerActionHoldSeconds ? holdWSeconds : minMoveHoldSeconds);
					return (Time.time - _actionStartTime) > wHold;
				case "LEFT":
				case "RIGHT":
				case "UP":
					// Selection highlight.
					if (!usePerActionHoldSeconds)
						return (Time.time - _actionStartTime) > minSelectHoldSeconds;
					float holdForArrows = _lastToken == "LEFT" ? holdLeftSeconds : (_lastToken == "RIGHT" ? holdRightSeconds : holdUpSeconds);
					return (Time.time - _actionStartTime) > holdForArrows;
				case "S":
					{
						float sHold = _overrideHoldSeconds > 0f ? _overrideHoldSeconds : (usePerActionHoldSeconds ? holdSSeconds : 0.3f);
						return (Time.time - _actionStartTime) > sHold;
					}
				case "A":
					{
						// Rotate left, preferring AI hold_seconds when present.
						float aHold = _overrideHoldSeconds > 0f ? _overrideHoldSeconds : (usePerActionHoldSeconds ? holdASeconds : 0.3f);
						return (Time.time - _actionStartTime) > aHold;
					}
				case "D":
					{
						// Rotate right, preferring AI hold_seconds when present.
						float dHold = _overrideHoldSeconds > 0f ? _overrideHoldSeconds : (usePerActionHoldSeconds ? holdDSeconds : 0.3f);
						return (Time.time - _actionStartTime) > dHold;
					}
				case "Q":
				case "E":
					{
						// Camera pitch rotation, preferring AI hold_seconds when present.
						float qeHold = _overrideHoldSeconds > 0f ? _overrideHoldSeconds : (usePerActionHoldSeconds ? (_lastToken == "Q" ? holdQSeconds : holdESeconds) : 0.5f);
						return (Time.time - _actionStartTime) > qeHold;
					}
				case "ENTER":
					return (Time.time - _actionStartTime) > (usePerActionHoldSeconds ? holdEnterSeconds : 0.3f);
				default:
					return (Time.time - _actionStartTime) > 0.3f;
			}
		}

		private string CaptureCameraBase64()
		{
			if (sourceCamera == null) return null;
			var prevRT = sourceCamera.targetTexture;
			var prevActive = RenderTexture.active;
			try
			{
				sourceCamera.targetTexture = _rt;
				sourceCamera.Render();
				RenderTexture.active = _rt;
				_captureTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				_captureTex.Apply();
				byte[] png = _captureTex.EncodeToPNG();
				return Convert.ToBase64String(png);
			}
			catch { return null; }
			finally
			{
				RenderTexture.active = prevActive;
				sourceCamera.targetTexture = prevRT;
			}
		}

		private string CaptureMapBase64()
		{
			DebugLog($"CaptureMapBase64: includeMapImage={includeMapImage}, _mapImage={_mapImage != null}, _mapParent={_mapParent != null}");
			
			if (!includeMapImage)
			{
				DebugLog("CaptureMapBase64: includeMapImage is false");
				return null;
			}
			
			if (_mapImage == null)
			{
				DebugLog("CaptureMapBase64: _mapImage is null");
				return null;
			}
			
			if (_mapParent == null)
			{
				DebugLog("CaptureMapBase64: _mapParent is null");
				return null;
			}
			
			if (!_mapParent.activeSelf)
			{
				DebugLog("CaptureMapBase64: _mapParent is not active (map is hidden)");
				return null;
			}
			
			// Log detailed map state.
			DebugLog($"CaptureMapBase64: _mapParent.name={_mapParent.name}, active={_mapParent.activeSelf}");
			if (_mapImage != null)
			{
				DebugLog($"CaptureMapBase64: _mapImage.name={_mapImage.name}, enabled={_mapImage.enabled}, texture={_mapImage.texture != null}");
			}

			DebugLog("CaptureMapBase64: all checks passed, attempting capture");
			try
			{
				var result = CaptureMapWithPlayerArrow();
				DebugLog($"CaptureMapBase64: result={result != null}, length={result?.Length ?? 0}");
				return result;
			}
			catch (System.Exception ex)
			{
				DebugLog($"CaptureMapBase64: error - {ex.Message}");
				return null;
			}
		}

		private string CaptureMapWithPlayerArrow()
		{
			DebugLog("CaptureMapWithPlayerArrow: starting UI region capture");
			// Capture the map region that is actually displayed in the UI.
			try
			{
				var result = CaptureUIRegion();
				if (result != null)
				{
					DebugLog($"CaptureMapWithPlayerArrow: UI region capture successful, length={result.Length}");
					return result;
				}
				else
				{
					DebugLog("CaptureMapWithPlayerArrow: UI region capture returned null, trying fallback");
					return CaptureHighQualityMapWithPlayer();
				}
			}
			catch (System.Exception ex)
			{
				DebugLog($"CaptureMapWithPlayerArrow: UI region capture error - {ex.Message}, trying fallback");
				// Fallback: try the legacy method.
				return CaptureMapFallback();
			}
		}

		private string CaptureUIRegion()
		{
			DebugLog("CaptureUIRegion: starting");
			// Get MapParent's actual screen coordinates.
			var mapRect = _mapParent.GetComponent<RectTransform>();
			if (mapRect == null)
			{
				DebugLog("CaptureUIRegion: mapRect is null");
				return null;
			}

			// Calculate the rectangle in screen coordinates.
			Vector3[] corners = new Vector3[4];
			mapRect.GetWorldCorners(corners);
			
			// Convert to screen coordinates.
			var canvas = _mapParent.GetComponentInParent<Canvas>();
			var camera = canvas.worldCamera ?? Camera.main;
			
			var screenBottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
			var screenTopRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);

			// Normalize coordinates by flipping the Y axis.
			var x = Mathf.RoundToInt(screenBottomLeft.x);
			var y = Mathf.RoundToInt(Screen.height - screenTopRight.y);
			var width = Mathf.RoundToInt(screenTopRight.x - screenBottomLeft.x);
			var height = Mathf.RoundToInt(screenTopRight.y - screenBottomLeft.y);

			// Clamp to the screen bounds.
			x = Mathf.Max(0, x);
			y = Mathf.Max(0, y);
			width = Mathf.Min(Screen.width - x, width);
			height = Mathf.Min(Screen.height - y, height);

			if (width <= 0 || height <= 0)
			{
				DebugLog("CaptureUIRegion: Invalid capture region");
				return null;
			}

			DebugLog($"Capturing UI region: ({x}, {y}) size=({width}, {height})");

			// Wait until the end of the frame.
			return CaptureScreenRegion(x, y, width, height);
		}

		private string CaptureScreenRegion(int x, int y, int width, int height)
		{
			// Render the UI canvas directly.
			return CaptureCanvasRegion();
		}

		private string CaptureCanvasRegion()
		{
			var canvas = _mapParent.GetComponentInParent<Canvas>();
			if (canvas == null) return null;

			// Draw the whole UI into a temporary screen-sized RenderTexture, then crop the MapParent region.
			var tempCameraObj = new GameObject("TempUICamera");
			var tempCamera = tempCameraObj.AddComponent<Camera>();
			var prevActive = RenderTexture.active;
			RenderTexture tempRt = null;
			Texture2D regionTexture = null;
			try
			{
				int screenW = Screen.width;
				int screenH = Screen.height;
				tempRt = new RenderTexture(screenW, screenH, 16, RenderTextureFormat.ARGB32);
				tempRt.antiAliasing = useHighQualityMapCapture ? 4 : 1;
				
				// Camera settings for UI only.
				tempCamera.clearFlags = CameraClearFlags.SolidColor;
				tempCamera.backgroundColor = Color.clear;
				tempCamera.orthographic = true;
				tempCamera.targetTexture = tempRt;
				tempCamera.cullingMask = 1 << 5; // UI layer
				tempCamera.depth = 100;
				
				// Temporarily switch Canvas settings so ScreenSpace-Camera renders the UI to the camera.
				var originalRenderMode = canvas.renderMode;
				var originalCamera = canvas.worldCamera;
				canvas.renderMode = RenderMode.ScreenSpaceCamera;
				canvas.worldCamera = tempCamera;
				
				// Render.
				RenderTexture.active = tempRt;
				GL.Clear(true, true, Color.clear);
				tempCamera.Render();
				
            // Get MapParent screen coordinates in pixels.
            var mapRect = _mapParent.GetComponent<RectTransform>();
            if (mapRect == null)
            {
                // Restore and exit.
                canvas.renderMode = originalRenderMode;
                canvas.worldCamera = originalCamera;
                return null;
            }
            Vector3[] corners = new Vector3[4];
            mapRect.GetWorldCorners(corners);
            var camForScreenPoint = canvas.worldCamera ?? Camera.main;
            var screenBL = RectTransformUtility.WorldToScreenPoint(camForScreenPoint, corners[0]);
            var screenTR = RectTransformUtility.WorldToScreenPoint(camForScreenPoint, corners[2]);
            int x = Mathf.RoundToInt(screenBL.x);
            int y = Mathf.RoundToInt(screenH - screenTR.y);
            int w = Mathf.RoundToInt(screenTR.x - screenBL.x);
            int h = Mathf.RoundToInt(screenTR.y - screenBL.y);
				
				// Clamp the range.
				x = Mathf.Max(0, x);
				y = Mathf.Max(0, y);
				w = Mathf.Min(screenW - x, w);
				h = Mathf.Min(screenH - y, h);
				if (w <= 0 || h <= 0)
				{
					canvas.renderMode = originalRenderMode;
					canvas.worldCamera = originalCamera;
					return null;
				}
				
            // Crop only the MapParent region from tempRt, which contains the full rendered UI.
            regionTexture = new Texture2D(w, h, useHighQualityMapCapture ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            regionTexture.ReadPixels(new Rect(x, y, w, h), 0, 0);
            regionTexture.Apply();
				
				// Resize to the target size when needed.
				Texture2D outputTex;
				if (w != mapWidth || h != mapHeight)
				{
					outputTex = ResizeTexture(regionTexture, mapWidth, mapHeight);
				}
				else
				{
					outputTex = regionTexture;
				}
				
				byte[] imageData = useHighQualityMapCapture ? outputTex.EncodeToPNG() : outputTex.EncodeToJPG(75);
				
				// Restore.
				canvas.renderMode = originalRenderMode;
				canvas.worldCamera = originalCamera;
				
				return Convert.ToBase64String(imageData);
			}
			finally
			{
				RenderTexture.active = prevActive;
				if (tempRt != null)
				{
					tempRt.Release();
					DestroyImmediate(tempRt);
				}
				if (regionTexture != null && (regionTexture.width != mapWidth || regionTexture.height != mapHeight))
				{
					DestroyImmediate(regionTexture);
				}
				if (tempCameraObj != null)
				{
					DestroyImmediate(tempCameraObj);
				}
			}
		}
		
		private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
		{
			var rt = new RenderTexture(targetWidth, targetHeight, 16);
			var prevActive = RenderTexture.active;
			
			RenderTexture.active = rt;
			Graphics.Blit(source, rt);
			
			var result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
			result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
			result.Apply();
			
			RenderTexture.active = prevActive;
			rt.Release();
			DestroyImmediate(rt);
			
			return result;
		}
		
		private string CaptureHighQualityMapWithPlayer()
		{
			// Fallback: use the legacy compositing method.
			var prevActive = RenderTexture.active;
			RenderTexture.active = _mapRt;
			
			try
			{
				GL.Clear(true, true, Color.clear);
				
				var mapTexture = _mapImage.texture;
				if (mapTexture != null)
				{
					Graphics.Blit(mapTexture, _mapRt);
				}
				
				OverLayPlayerTexture();
				
				_mapCaptureTex.ReadPixels(new Rect(0, 0, mapWidth, mapHeight), 0, 0);
				_mapCaptureTex.Apply();
				
				byte[] imageData = useHighQualityMapCapture ? 
					_mapCaptureTex.EncodeToPNG() : 
					_mapCaptureTex.EncodeToJPG(75);
				
				return Convert.ToBase64String(imageData);
			}
			catch (System.Exception ex)
			{
				DebugLog($"CaptureMapFallback: error - {ex.Message}");
				return null;
			}
			finally
			{
				RenderTexture.active = prevActive;
			}
		}
		
		private string CaptureMapFallback()
        {
			// Fallback: use the legacy compositing method.
			var prevActive = RenderTexture.active;
			RenderTexture.active = _mapRt;
			
			try
			{
				GL.Clear(true, true, Color.clear);
				
				var mapTexture = _mapImage.texture;
				if (mapTexture != null)
				{
					Graphics.Blit(mapTexture, _mapRt);
				}
				
				_mapCaptureTex.ReadPixels(new Rect(0, 0, mapWidth, mapHeight), 0, 0);
				_mapCaptureTex.Apply();
				
				byte[] imageData = useHighQualityMapCapture ?
					_mapCaptureTex.EncodeToPNG() :
					_mapCaptureTex.EncodeToJPG(75);
				
				return Convert.ToBase64String(imageData);
			}
			catch (System.Exception ex)
			{
				DebugLog($"CaptureMapFallback: error - {ex.Message}");
				return null;
			}
			finally
			{
				RenderTexture.active = prevActive;
			}
		}
		
        private void OverLayPlayerTexture()
        {
            var uls = GetUserLocationService();
            var playerTexture = _playerImage != null ? _playerImage.texture : null;
            // Player icon size in pixels.
            var (playerHeight, playerWidth) = (playerIconSize, playerIconSize);
            if (playerTexture != null && uls != null)
            {
                // Coordinates as UV ratios.
                var (rx, ry) = uls.PositionRatio();
                // Use the avatar yaw angle around the Y axis.
                var (_, rot) = uls.CurrentPositionState();
                float yaw = rot.eulerAngles.y;

                GL.PushMatrix();
                // Pixel coordinate system with the origin at the top left.
                GL.LoadPixelMatrix(0, mapWidth, mapHeight, 0);

                // Center the icon.
                float posX = rx * mapWidth  - playerWidth  * 0.5f;
                float posY = (1 - ry) * mapHeight - playerHeight * 0.5f;

                // Rotate and draw the texture.
                Material mat = new Material(Shader.Find("Hidden/RotateTexture"));
                mat.SetFloat("_Rotation", yaw * Mathf.Deg2Rad);
                var dst = new Rect(posX, posY, playerWidth, playerHeight);
                Graphics.DrawTexture(dst, playerTexture, mat);
                GL.PopMatrix();
            }
        }
		
        private string DepictPlayerTrajectory()
        {
            var userLocationService = GetUserLocationService();
            if (_trailDrawer == null)
            {
                _trailDrawer = new TrailDrawer(mapWidth, mapHeight, _mapRt);
                _prevPlayerMapPos = userLocationService.PositionRatio();
                return null;
            }
            var currentPos = userLocationService.PositionRatio();
            _trailDrawer.DrawLine(new Vector2(_prevPlayerMapPos.Item1, _prevPlayerMapPos.Item2),
                                  new Vector2(currentPos.Item1, currentPos.Item2), Color.red, trajectoryLineWidth);
            _prevPlayerMapPos = currentPos;

            return CaptureMap(_trailDrawer.trailRT);
        }
		
		private string CaptureMap(RenderTexture rt)
		{
			// Fallback: use the legacy compositing method.
			var prevActive = RenderTexture.active;
			RenderTexture.active = _mapRt;
			
			try
			{
				GL.Clear(true, true, Color.clear);
				
				var mapTexture = _mapImage.texture;
                if (mapTexture != null)
                {
                    Graphics.Blit(mapTexture, _mapRt);
                }
                Rect rect = new Rect(0, 0, mapWidth, mapHeight);
                if(rt != null)
                {
                    Material mat = new Material(Shader.Find("Hidden/RotateTexture"));
                    Graphics.Blit(rt, _mapRt, mat);
                }
				
				_mapCaptureTex.ReadPixels(new Rect(0, 0, mapWidth, mapHeight), 0, 0);
				_mapCaptureTex.Apply();
				
				byte[] imageData = useHighQualityMapCapture ?
					_mapCaptureTex.EncodeToPNG() :
					_mapCaptureTex.EncodeToJPG(75);
				
				return Convert.ToBase64String(imageData);
			}
			catch (System.Exception ex)
			{
				DebugLog($"CaptureMapFallback: error - {ex.Message}");
				return null;
			}
			finally
			{
				RenderTexture.active = prevActive;
			}
		}

		private void RecordTrajectorySample(Vector2 ratio, Vector3 position, Quaternion rotation, string segmentPath)
		{
			try
			{
				if (_trajectorySamples.Count > 0)
				{
					var last = _trajectorySamples[_trajectorySamples.Count - 1];
					if (Mathf.Approximately(last.elapsedSeconds, Time.time) &&
					    Mathf.Approximately(last.ratioX, ratio.x) &&
					    Mathf.Approximately(last.ratioY, ratio.y))
					{
						return;
					}
				}

				var sample = new TrajectorySample
				{
					elapsedSeconds = Time.time,
					ratioX = ratio.x,
					ratioY = ratio.y,
					position = position,
					yawDegrees = rotation.eulerAngles.y,
					segmentPath = segmentPath
				};
				_trajectorySamples.Add(sample);
			}
			catch (Exception ex)
			{
				DebugWarn($"RecordTrajectorySample: failed - {ex.Message}");
			}
		}

		private bool TryCaptureTrajectoryObservation(out TrajectoryObservation observation, bool recordSample = true)
		{
			observation = default;
			try
			{
				var userLocationService = GetUserLocationService();
				if (userLocationService == null)
				{
					return false;
				}

				var segment = userLocationService.CurrentSegment();
				var (position, rotation) = userLocationService.CurrentPositionState();
				var ratioTuple = userLocationService.PositionRatio();
				var ratio = new Vector2(ratioTuple.Item1, ratioTuple.Item2);

				observation = new TrajectoryObservation
				{
					ratio = ratio,
					position = position,
					rotation = rotation,
					segmentPath = segment?.Path ?? ""
				};

				if (recordSample)
				{
					if (_playerPath.Count == 0 || _playerPath[_playerPath.Count - 1] != ratio)
					{
						_playerPath.Add(ratio);
					}
					RecordTrajectorySample(ratio, position, rotation, observation.segmentPath);
				}

				return true;
			}
			catch (Exception ex)
			{
				DebugWarn($"TryCaptureTrajectoryObservation: failed - {ex.Message}");
				return false;
			}
		}

		private void SaveTrajectoryArtifacts(string stopReason)
		{
			if (_trajectorySavedToDisk)
			{
				return;
			}

			if (_trajectorySamples == null || _trajectorySamples.Count == 0)
			{
				DebugLog("SaveTrajectoryArtifacts: no trajectory samples captured; exporting empty log");
			}

			try
			{
				var session = string.IsNullOrEmpty(_sessionId)
					? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")
					: _sessionId;
				var baseDir = ResolveTrajectoryOutputDirectory();

				var payload = new TrajectoryLog
				{
					sessionId = session,
					stopReason = stopReason,
					startedAtUtc = _sessionStartIsoUtc,
					endedAtUtc = DateTime.UtcNow.ToString("o"),
					durationSeconds = _trajectorySamples.Count > 0 ? _trajectorySamples[_trajectorySamples.Count - 1].elapsedSeconds : 0f,
					samples = _trajectorySamples.ToArray()
				};

				var jsonPath = Path.Combine(baseDir, $"{session}_trajectory.json");
				File.WriteAllText(jsonPath, JsonUtility.ToJson(payload, true));
				Log($"SaveTrajectoryArtifacts: trajectory JSON saved to {jsonPath}");

				if (_playerPath != null && _playerPath.Count >= 2)
				{
					TrailDrawer drawer = null;
					try
					{
						drawer = new TrailDrawer(mapWidth, mapHeight, _mapRt);
						for (int i = 1; i < _playerPath.Count; i++)
						{
							drawer.DrawLine(_playerPath[i - 1], _playerPath[i], Color.red, trajectoryLineWidth);
						}

						var mapB64 = CaptureMap(drawer.trailRT);
						if (!string.IsNullOrEmpty(mapB64))
						{
							var bytes = Convert.FromBase64String(mapB64);
							var ext = useHighQualityMapCapture ? "png" : "jpg";
							var mapPath = Path.Combine(baseDir, $"{session}_trajectory.{ext}");
							File.WriteAllBytes(mapPath, bytes);
							Log($"SaveTrajectoryArtifacts: trajectory map saved to {mapPath}");
						}
					}
					catch (Exception ex)
					{
						LogWarn($"SaveTrajectoryArtifacts: map export failed - {ex.Message}");
					}
					finally
					{
						if (drawer?.trailRT != null)
						{
							drawer.trailRT.Release();
							Destroy(drawer.trailRT);
						}
					}
				}

				_trajectorySavedToDisk = true;
			}
			catch (Exception ex)
			{
				LogWarn($"SaveTrajectoryArtifacts: failed - {ex.Message}");
			}
		}

		private string ResolveTrajectoryOutputDirectory()
		{
			string resolved = null;
			if (preferProjectLogsDirectory)
			{
				try
				{
					var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
					var projectLogs = Path.Combine(projectRoot, "Logs", "trajectory_logs");
					Directory.CreateDirectory(projectLogs);
					resolved = projectLogs;
				}
				catch (Exception ex)
				{
					LogWarn($"ResolveTrajectoryOutputDirectory: project Logs directory unavailable - {ex.Message}");
				}
			}

			if (string.IsNullOrEmpty(resolved))
			{
				try
				{
					var fallback = Path.Combine(Application.persistentDataPath, "trajectory_logs");
					Directory.CreateDirectory(fallback);
					resolved = fallback;
				}
				catch (Exception ex)
				{
					LogWarn($"ResolveTrajectoryOutputDirectory: persistentDataPath fallback failed - {ex.Message}");
					resolved = Application.persistentDataPath;
				}
			}

			return resolved;
		}

		private void ExportTrajectoryImage()
		{
			try
			{
				// Backward compatibility hook for local saves if needed; currently unused.
				if (_playerPath == null || _playerPath.Count < 2)
				{
					return;
				}
				var drawer = new TrailDrawer(mapWidth, mapHeight, _mapRt);
				for (int i = 1; i < _playerPath.Count; i++)
				{
					drawer.DrawLine(_playerPath[i - 1], _playerPath[i], Color.red, trajectoryLineWidth);
				}
				CaptureMap(drawer.trailRT);
			}
			catch (Exception) { }
		}

		private void SendFinalTrajectoryToPython()
		{
			if (!_connectionEstablished || _persistentWriter == null) return;
			if (_playerPath == null || _playerPath.Count < 2) return;

			// 1) Generate the final trajectory image.
			var drawer = new TrailDrawer(mapWidth, mapHeight, _mapRt);
			for (int i = 1; i < _playerPath.Count; i++)
			{
				drawer.DrawLine(_playerPath[i - 1], _playerPath[i], Color.red, trajectoryLineWidth);
			}
			string finalMapB64 = CaptureMap(drawer.trailRT);
			if (string.IsNullOrEmpty(finalMapB64)) return;

			// 2) Capture the camera image, which is a required field.
			string cameraB64 = CaptureCameraBase64();
			if (string.IsNullOrEmpty(cameraB64)) return;

			// 3) Location information.
			float posX = 0f, posZ = 0f; string segmentPath = "";
			try
			{
				var uls = GetUserLocationService();
				if (uls != null)
				{
					var seg = uls.CurrentSegment();
					var pr = uls.CurrentPositionState();
					posX = pr.Item1.x; posZ = pr.Item1.z;
					segmentPath = seg?.Path ?? "";
				}
			}
			catch (Exception) { }

			// 4) Send the final trajectory in map_images_b64.
			var finalReq = new ActRequest
			{
				image_b64 = cameraB64,
				map_image_b64 = null,
				map_images_b64 = new List<string> { finalMapB64 },
				position_x = posX,
				position_z = posZ,
				segment_path = segmentPath
			};
			string line = JsonUtility.ToJson(finalReq);
			try { _persistentWriter.WriteLine(line); Log("Final trajectory sent to Python."); }
			catch (Exception ex) { LogWarn($"SendFinalTrajectoryToPython: send failed - {ex.Message}"); }
		}
		
		private struct TrajectoryObservation
		{
			public Vector2 ratio;
			public Vector3 position;
			public Quaternion rotation;
			public string segmentPath;
		}

		[Serializable]
		private class TrajectorySample
		{
			public float elapsedSeconds;
			public float ratioX;
			public float ratioY;
			public Vector3 position;
			public float yawDegrees;
			public string segmentPath;
		}

		[Serializable]
		private class TrajectoryLog
		{
			public string sessionId;
			public string stopReason;
			public string startedAtUtc;
			public string endedAtUtc;
			public float durationSeconds;
			public TrajectorySample[] samples;
		}
		
        [Serializable]
		private class PythonEnvelopeV1
		{
			public bool success;
			public KeyActionDTO action; // legacy action DTO
			public string token; // legacy token string
			public string raw_response;
		}
		
		[Serializable]
		private class PythonEnvelopeV2
		{
			public bool success;
			public string action; // new: action token as string (e.g., "W", "LEFT")
			public string raw_response;
			public string thought;
			public string reflection;
			public string error;
			public float hold_seconds; // optional: AI suggested hold duration (seconds)
		}
		
		private string InferTokenFromActionDTO(KeyActionDTO dto)
		{
			if (dto == null) return null;
			if (dto.wKey) return "W";
			if (dto.leftArrow) return "LEFT";
			if (dto.rightArrow) return "RIGHT";
			if (dto.upArrow) return "UP";
			if (dto.sKey) return "S";
			if (dto.aKey) return "A";
			if (dto.dKey) return "D";
			if (dto.qKey) return "Q";
			if (dto.eKey) return "E";
			if (dto.enter) return "ENTER";
			return null;
		}
		
		private void Log(string message)
		{
			Debug.Log("[AIInput] " + message);
		}

		private void DebugLog(string message)
		{
			if (verboseLog)
			{
				Debug.Log("[AIInput] " + message);
			}
		}

		private string DescribePayload(string payload)
		{
			if (string.IsNullOrEmpty(payload))
			{
				return "len=0 hash=00000000";
			}

			unchecked
			{
				uint hash = 2166136261;
				for (int i = 0; i < payload.Length; i++)
				{
					hash ^= payload[i];
					hash *= 16777619;
				}
				return $"len={payload.Length} hash={hash:X8}";
			}
		}
		
		private UserLocationService GetUserLocationService()
		{
			if (movieChanger != null)
			{
				return movieChanger.UserLocationService;
			}
			return null;
		}

		private void LogCurrentLocation()
		{
			if (movieChanger == null)
			{
				return;
			}

			try
			{
				var userLocationService = GetUserLocationService();
				if (userLocationService != null)
				{
					var segment = userLocationService.CurrentSegment();
					var (position, rotation) = userLocationService.CurrentPositionState();
					string segmentPath = segment?.Path ?? "unknown";

					Log($"Location: pos=({position.x:F2}, {position.z:F2}), path={segmentPath}");
				}
			}
			catch (Exception ex)
			{
				DebugWarn($"Failed to log location: {ex.Message}");
			}
		}
		
		private void LogWarn(string message)
		{
			Debug.LogWarning("[AIInput] " + message);
		}

		private void DebugWarn(string message)
		{
			if (verboseLog)
			{
				Debug.LogWarning("[AIInput] " + message);
			}
		}
		
		private void LogError(string message)
		{
			Debug.LogError("[AIInput] " + message);
		}
		
static Mesh _quad;
static Mesh GetQuad(){
	if (_quad) return _quad;
	_quad = new Mesh{
		vertices = new []{ new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(0,1,0) },
		uv	   = new []{ new Vector2(0,0),   new Vector2(1,0),   new Vector2(1,1),   new Vector2(0,1) }
	};
	_quad.triangles = new[]{ 0,1,2, 0,2,3 };
	return _quad;
}
		
		private void StopUnity()
		{
			SaveTrajectoryArtifacts("stop_unity");
			Log("StopUnity: stopping play mode / application");
			// Stop play mode in the editor; quit the app in builds.
#if UNITY_EDITOR
			try
			{
				UnityEditor.EditorApplication.isPlaying = false;
			}
			catch { }
#else
			try
			{
				Application.Quit();
			}
			catch { }
#endif
		}
		
		[Serializable]
		private class PingRequest
		{
			public string type = "ping";
		}
		
		[Serializable]
		private class KeyActionDTO
		{
			public bool wKey;
			public bool sKey;
			public bool aKey;
			public bool dKey;
			public bool qKey;
			public bool eKey;
			public bool leftArrow;
			public bool rightArrow;
			public bool upArrow;
			public bool enter;
		}
	}
	
}

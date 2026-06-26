using System;
using System.Collections.Generic;
using UnityEngine;

namespace MovieMap.Core
{
	public class RandomInputManager : MonoBehaviour
	{
		[Header("Enable random input injection")]
		[SerializeField]
		private bool enableRandomInput = false;

		[Header("External (AI) input injection")]
		[SerializeField]
		private bool enableInjectedInput = false;

		[Header("Randomization settings")]
		[SerializeField] private float changeIntervalMinSeconds = 0.4f;
		[SerializeField] private float changeIntervalMaxSeconds = 1.2f;
		[SerializeField] private float holdKeyProbability = 0.25f; // probability a hold-type key remains/turns on at each tick
		[SerializeField] private float oneShotKeyProbability = 0.05f; // probability to emit a one-shot key down at each tick

		[Header("Mouse simulation settings")]
		[SerializeField] private bool simulateMouse = true;
		[SerializeField] private float holdMouseProbability = 0.3f;
		[SerializeField] private float oneShotMouseDownProbability = 0.05f;
		[SerializeField] private Vector2 mouseDeltaRangeWhenHolding = new Vector2(30f, 20f);
		[SerializeField] private Vector2 mouseDeltaRangeWhenIdle = new Vector2(5f, 3f);

		private static RandomInputManager instance;
		private float nextChangeTime;

		private readonly KeyCode[] holdKeys = new[]
		{
			KeyCode.W, KeyCode.LeftShift, KeyCode.A, KeyCode.D, KeyCode.Q, KeyCode.E,
			KeyCode.UpArrow, KeyCode.LeftArrow, KeyCode.RightArrow
		};

		private readonly KeyCode[] oneShotKeys = new[]
		{
			KeyCode.S, KeyCode.Return, KeyCode.M
		};

		private readonly Dictionary<KeyCode, bool> currentKeyState = new Dictionary<KeyCode, bool>();
		private readonly Dictionary<KeyCode, bool> previousKeyState = new Dictionary<KeyCode, bool>();

		// External injected input state (AI)
		private readonly Dictionary<KeyCode, bool> injectedHoldCurrent = new Dictionary<KeyCode, bool>();
		private readonly Dictionary<KeyCode, bool> injectedHoldPrevious = new Dictionary<KeyCode, bool>();
		private readonly HashSet<KeyCode> injectedDownThisFrame = new HashSet<KeyCode>();
		private readonly HashSet<KeyCode> injectedOneShotHolds = new HashSet<KeyCode>();

		// Mouse simulation state
		private Vector3 syntheticMousePosition;
		private bool currentMouse0;
		private bool previousMouse0;

		public static bool Enabled
		{
			get => instance != null && instance.enableRandomInput;
			set
			{
				EnsureExists();
				instance.enableRandomInput = value;
			}
		}

		public static bool InjectedEnabled
		{
			get => instance != null && instance.enableInjectedInput;
			set
			{
				EnsureExists();
				instance.enableInjectedInput = value;
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void EnsureExists()
		{
			if (instance != null) return;
			var go = new GameObject("RandomInputManager");
			DontDestroyOnLoad(go);
			instance = go.AddComponent<RandomInputManager>();
		}

		private void Awake()
		{
			if (instance != null && instance != this)
			{
				Destroy(gameObject);
				return;
			}
			instance = this;
			InitializeStates();
			InitializeMouse();
			ScheduleNextChange();
		}

		private void InitializeStates()
		{
			foreach (var key in holdKeys)
			{
				currentKeyState[key] = false;
				previousKeyState[key] = false;
			}
			foreach (var key in oneShotKeys)
			{
				currentKeyState[key] = false;
				previousKeyState[key] = false;
			}
		}

		private void ScheduleNextChange()
		{
			nextChangeTime = Time.time + UnityEngine.Random.Range(changeIntervalMinSeconds, changeIntervalMaxSeconds);
		}

		private void Update()
		{
			// allow toggling regardless of current mode
			if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
			{
				enableRandomInput = !enableRandomInput;
				Debug.Log($"RandomInputManager: {(enableRandomInput ? "ENABLED" : "DISABLED")} (F8 to toggle)");
			}

			// Always advance injected states so GetKeyDown works even when random is disabled
			foreach (var kv in injectedHoldCurrent)
			{
				injectedHoldPrevious[kv.Key] = kv.Value;
			}
			injectedDownThisFrame.Clear();

			if (!enableRandomInput) return;

			// advance per-frame state and clear one-shot keys unless triggered this frame
			foreach (var key in currentKeyState.Keys)
			{
				previousKeyState[key] = currentKeyState[key];
			}
			previousMouse0 = currentMouse0;

			if (Time.time >= nextChangeTime)
			{
				// randomize hold keys (set on with probability, otherwise off)
				foreach (var key in holdKeys)
				{
					currentKeyState[key] = UnityEngine.Random.value < holdKeyProbability;
				}

				// one-shot keys: emit a down event for exactly one frame
				foreach (var key in oneShotKeys)
				{
					currentKeyState[key] = UnityEngine.Random.value < oneShotKeyProbability;
				}

				// mouse button 0
				if (simulateMouse)
				{
					bool hold = UnityEngine.Random.value < holdMouseProbability;
					bool oneShotDown = UnityEngine.Random.value < oneShotMouseDownProbability;
					currentMouse0 = hold || oneShotDown;

					// update mouse position
					Vector2 range = currentMouse0 ? mouseDeltaRangeWhenHolding : mouseDeltaRangeWhenIdle;
					float dx = UnityEngine.Random.Range(-range.x, range.x);
					float dy = UnityEngine.Random.Range(-range.y, range.y);
					syntheticMousePosition.x = Mathf.Clamp(syntheticMousePosition.x + dx, 0, Screen.width);
					syntheticMousePosition.y = Mathf.Clamp(syntheticMousePosition.y + dy, 0, Screen.height);
				}

				ScheduleNextChange();
			}
			else
			{
				// keep one-shot keys false in frames without a scheduled change
				foreach (var key in oneShotKeys)
				{
					currentKeyState[key] = false;
				}
				if (simulateMouse)
				{
					// one-shot mouse down false
					currentMouse0 = currentMouse0 && holdMouseProbability > 0f; // maintain hold across frames; no new one-shot
				}
			}

			// clear one-shot holds at the end of our update
			if (injectedOneShotHolds.Count > 0)
			{
				foreach (var k in injectedOneShotHolds)
				{
					injectedHoldCurrent[k] = false;
				}
				injectedOneShotHolds.Clear();
			}
		}

		private void InitializeMouse()
		{
			syntheticMousePosition = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
			currentMouse0 = false;
			previousMouse0 = false;
		}

		public static bool GetKey(KeyCode key)
		{
			if (InjectedEnabled)
			{
				EnsureExists();
				if (instance.injectedHoldCurrent.TryGetValue(key, out var hold) && hold) return true;
				return false;
			}
			if (!Enabled) return Input.GetKey(key);
			EnsureExists();
			return instance.currentKeyState.TryGetValue(key, out var v) ? v : false;
		}

		public static bool GetKeyDown(KeyCode key)
		{
			if (InjectedEnabled)
			{
				EnsureExists();
				bool down = instance.injectedDownThisFrame.Contains(key);
				bool curHold = instance.injectedHoldCurrent.TryGetValue(key, out var ch) && ch;
				bool prevHold = instance.injectedHoldPrevious.TryGetValue(key, out var ph) && ph;
				return down || (curHold && !prevHold);
			}
			if (!Enabled) return Input.GetKeyDown(key);
			EnsureExists();
			bool cur = instance.currentKeyState.TryGetValue(key, out var cv) && cv;
			bool prev = instance.previousKeyState.TryGetValue(key, out var pv) && pv;
			return cur && !prev;
		}

		public static bool GetButtonDown(string buttonName)
		{
			if (InjectedEnabled)
			{
				if (string.Equals(buttonName, "Enter", StringComparison.OrdinalIgnoreCase))
				{
					return GetKeyDown(KeyCode.Return);
				}
				if (string.Equals(buttonName, "Map", StringComparison.OrdinalIgnoreCase))
				{
					return GetKeyDown(KeyCode.M);
				}
				return false;
			}
			if (!Enabled) return Input.GetButtonDown(buttonName);
			// Map legacy input names to keys we randomize
			if (string.Equals(buttonName, "Enter", StringComparison.OrdinalIgnoreCase))
			{
				return GetKeyDown(KeyCode.Return);
			}
			if (string.Equals(buttonName, "Map", StringComparison.OrdinalIgnoreCase))
			{
				return GetKeyDown(KeyCode.M);
			}
			return false;
		}

		public static bool GetMouseButton(int button)
		{
			if (!Enabled) return Input.GetMouseButton(button);
			EnsureExists();
			if (button == 0) return instance.simulateMouse && instance.currentMouse0;
			return false;
		}

		public static bool GetMouseButtonDown(int button)
		{
			if (!Enabled) return Input.GetMouseButtonDown(button);
			EnsureExists();
			if (button == 0) return instance.simulateMouse && instance.currentMouse0 && !instance.previousMouse0;
			return false;
		}

		public static Vector3 GetMousePosition()
		{
			if (!Enabled) return Input.mousePosition;
			EnsureExists();
			return instance.simulateMouse ? instance.syntheticMousePosition : Input.mousePosition;
		}

		// External API
		public static void InjectHoldStates(Dictionary<KeyCode, bool> holdStates)
		{
			EnsureExists();
			instance.enableInjectedInput = true;
			if (holdStates == null) return;
			foreach (var kv in holdStates)
			{
				instance.injectedHoldCurrent[kv.Key] = kv.Value;
			}
		}

		public static void InjectKeyDown(HashSet<KeyCode> downKeys)
		{
			EnsureExists();
			instance.enableInjectedInput = true;
			if (downKeys == null) return;
			foreach (var k in downKeys)
			{
				instance.injectedDownThisFrame.Add(k);
				// Also emulate a transient hold so GetKeyDown can be derived from hold edge if needed
				instance.injectedHoldPrevious[k] = false;
				instance.injectedHoldCurrent[k] = true;
				instance.injectedOneShotHolds.Add(k);
			}
		}
	}
}



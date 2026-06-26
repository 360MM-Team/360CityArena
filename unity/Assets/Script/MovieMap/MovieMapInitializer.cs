using UnityEngine;

namespace MovieMap.Core
{
    public class MovieMapInitializer : MonoBehaviour
    {
        readonly string _areaName = "akihabara_completed";
        
        [SerializeField]
        [Tooltip("PythonAIInputClient（nullの場合は自動検索）")]
        PythonAIInputClient _pythonClient;
        
        async void Awake()
        {
            // Get a reference to PythonAIInputClient.
            if (_pythonClient == null)
            {
                _pythonClient = FindAnyObjectByType<PythonAIInputClient>();
            }
            
            // Check whether the Python server is disabled.
            bool pythonEnabled = _pythonClient != null && _pythonClient.IsEnabled();
            
            if (!GlobalInfo.BaseDataInitialized)
            {
                Debug.Log("Initializing GlobalInfo base data with area: " + _areaName);
                
                // Initialize base data only.
                await GlobalInfo.InitializeBaseData(_areaName);
                Debug.Log("GlobalInfo base data initialized successfully.");
                
                // If the Python server is disabled, set the initial position directly.
                if (!pythonEnabled)
                {
                    // Get the start position from PythonAIInputClient.
                    int startIndex = _pythonClient != null ? _pythonClient.GetStartIndex() : -1;
                    Debug.Log($"Python Server is disabled. Using direct initialization with start index: {startIndex}");
                    GlobalInfo.SetStartIndex(startIndex);
                    GlobalInfo.SetStartLocationAndFinalize();
                    Debug.Log("Direct initialization completed - GlobalInfo fully initialized.");
                }
                else
                {
                    Debug.Log("Python Server is enabled. Waiting for Python connection to set start location...");
                }
            }
        }
        
        // Called when the Python connection is established.
        public void OnPythonConnected(int startIndex = -1)
        {
            Debug.Log($"OnPythonConnected called with startIndex: {startIndex}");
            
            if (!GlobalInfo.BaseDataInitialized)
            {
                Debug.LogError("Base data must be initialized before setting start location from Python!");
                return;
            }
            
            if (GlobalInfo.Initialized)
            {
                Debug.LogWarning("GlobalInfo already initialized, ignoring Python start index.");
                return;
            }
            
            Debug.Log($"Setting start location from Python: {startIndex}");
            GlobalInfo.SetStartIndex(startIndex);
            
            Debug.Log("Calling GlobalInfo.SetStartLocationAndFinalize()...");
            GlobalInfo.SetStartLocationAndFinalize();
            Debug.Log($"GlobalInfo initialization completed. Initialized = {GlobalInfo.Initialized}");
        }
    }
}

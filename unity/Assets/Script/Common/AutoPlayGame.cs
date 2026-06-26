using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

public static class AutoPlayGame
{
    private const string AutoStartScenePath = "Assets/Scene/Akihabara.unity";

    public static void PlayGame()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            FocusGameView();
            return;
        }

        if (!OpenAutoStartScene())
        {
            return;
        }

        EditorApplication.EnterPlaymode();
        FocusGameView();
    }

    private static bool OpenAutoStartScene()
    {
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (activeScene.path == AutoStartScenePath)
        {
            return true;
        }

        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(AutoStartScenePath);
        if (sceneAsset == null)
        {
            Debug.LogError($"[AutoPlayGame] Auto-start scene not found: {AutoStartScenePath}");
            return false;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[AutoPlayGame] Play mode canceled because current scene changes were not saved.");
            return false;
        }

        EditorSceneManager.OpenScene(AutoStartScenePath, OpenSceneMode.Single);
        Debug.Log($"[AutoPlayGame] Opened auto-start scene: {AutoStartScenePath}");
        return true;
    }

    private static void FocusGameView()
    {
        var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        var gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
        if (gameViews != null && gameViews.Length > 0)
        {
            var gameView = (EditorWindow)gameViews[0];
            gameView.Focus();
        }
    }
}

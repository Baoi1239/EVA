using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EVAMirroringDemoBuilder
{
    private const string MainPath = "Assets/_Project/Scenes/Main.unity";
    private const string DemoPath = "Assets/_Project/Scenes/MirroringDemo.unity";

    [MenuItem("EVA/Mirroring/Create Demo Scene")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoPath) != null)
        {
            Debug.Log("[EVA Mirroring] Demo scene already exists: " + DemoPath);
            return;
        }

        var scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        // Save to a new path before editing, so Main.unity never receives demo changes.
        if (!EditorSceneManager.SaveScene(scene, DemoPath))
            throw new System.InvalidOperationException("Could not create MirroringDemo.unity");

        RemoveAll<EVASystemLauncher>();
        RemoveAll<BackendConnection>();
        RemoveAll<EVASelectionMenu>();
        RemoveAll<EVALoadingOverlay>();
        RemoveAll<EVAConversationDisplay>();
        RemoveAll<MicrophoneCapture>();
        RemoveAll<StreamingAudioPlayer>();
        foreach (var visemes in Object.FindObjectsByType<EVAVisemeController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
            visemes.enabled = false;

        var appearance = Object.FindFirstObjectByType<EVAAppearanceController>(FindObjectsInactive.Include);
        var animations = Object.FindFirstObjectByType<EVAAnimationController>(FindObjectsInactive.Include);
        if (appearance == null || animations == null)
            throw new System.InvalidOperationException("EVA avatar/animation controller missing from source scene");
        appearance.SetAppearance(0);
        appearance.enabled = false; // Keep the selected demo avatar fixed.
        if (Camera.main != null) Camera.main.stereoTargetEye = StereoTargetEyeMask.None;

        var avatar = animations.gameObject;
        avatar.AddComponent<EVAPoseReceiver>();
        var gaze = avatar.GetComponent<EVAGazeController>();
        if (gaze) gaze.enabled = false;
        var body = avatar.AddComponent<EVAUpperBodyMirroring>();
        body.SetSmoothingSeconds(.1f);
        new GameObject("Mirroring Demo UI").AddComponent<EVAMirroringDemoUI>();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new System.InvalidOperationException("Could not save MirroringDemo.unity");
        AssetDatabase.Refresh();
        Debug.Log("[EVA Mirroring] Created " + DemoPath);
    }

    public static void BuildForBatch() => Build();

    public static void ValidateForBatch()
    {
        EditorSceneManager.OpenScene(DemoPath, OpenSceneMode.Single);
        if (Object.FindFirstObjectByType<EVASystemLauncher>(FindObjectsInactive.Include) != null
            || Object.FindFirstObjectByType<BackendConnection>(FindObjectsInactive.Include) != null
            || Object.FindFirstObjectByType<EVASelectionMenu>(FindObjectsInactive.Include) != null)
            throw new System.InvalidOperationException("Speech backend/menu remains in demo scene");
        var receiver = Object.FindFirstObjectByType<EVAPoseReceiver>(FindObjectsInactive.Include);
        var body = Object.FindFirstObjectByType<EVAUpperBodyMirroring>(FindObjectsInactive.Include);
        var ui = Object.FindFirstObjectByType<EVAMirroringDemoUI>(FindObjectsInactive.Include);
        if (receiver == null || body == null || ui == null)
            throw new System.InvalidOperationException("Mirroring receiver, upper body or UI missing");
        var gaze = body.GetComponent<EVAGazeController>();
        if (gaze && gaze.enabled) throw new System.InvalidOperationException("Demo gaze controller enabled");
        foreach (float value in new[] { 0f, 0.5f, 1f })
        {
            body.SetStrength(value);
            if (!Mathf.Approximately(body.Strength, value))
                throw new System.InvalidOperationException("Strength setter failed at " + value);
        }
        Debug.Log("[EVA Mirroring] Demo validation passed: 0%, 50%, 100%; no speech backend");
    }

    private static void RemoveAll<T>() where T : Component
    {
        foreach (var component in Object.FindObjectsByType<T>(FindObjectsInactive.Include,
            FindObjectsSortMode.None))
            Object.DestroyImmediate(component, true);
    }
}

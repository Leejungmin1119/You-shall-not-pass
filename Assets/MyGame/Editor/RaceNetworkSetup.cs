using System;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>기존 씬/프리팹 GUID를 유지하면서 레이스 네트워크 컴포넌트만 연결한다.</summary>
public static class RaceNetworkSetup
{
    public const string GameScene = "Assets/Scenes/SampleScene.unity";
    public const string PlayerPrefab = "Assets/MyGame/Prefabs/NetworkPlatformPlayer.prefab";
    public const string RulesPath = "Assets/MyGame/Settings/RaceNetworkRules.asset";

    [MenuItem("Tools/Network Practice/Configure Race Networking")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save open scene changes before configuring race networking.");
        if (!AssetDatabase.IsValidFolder("Assets/MyGame/Settings"))
            AssetDatabase.CreateFolder("Assets/MyGame", "Settings");
        var rules = AssetDatabase.LoadAssetAtPath<RaceNetworkRules>(RulesPath);
        if (rules == null)
        {
            rules = ScriptableObject.CreateInstance<RaceNetworkRules>();
            AssetDatabase.CreateAsset(rules, RulesPath);
        }
        var prefab = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            var inventory = prefab.GetComponent<NetworkRaceInventory>() ?? prefab.AddComponent<NetworkRaceInventory>();
            inventory.syncMode = SyncMode.Owner;
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        string previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.OpenScene(GameScene);
        var legacy = UnityEngine.Object.FindFirstObjectByType<rolechangeManager>();
        if (legacy == null) throw new InvalidOperationException("The gameplay scene needs the existing role manager object.");
        var race = legacy.GetComponent<RaceSession>() ?? legacy.gameObject.AddComponent<RaceSession>();
        var serialized = new SerializedObject(race);
        serialized.FindProperty("rules").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RaceNetworkRules>(RulesPath);
        serialized.FindProperty("startPoint").objectReferenceValue =
            new SerializedObject(legacy).FindProperty("firstSpawnPoint").objectReferenceValue;
        var map = UnityEngine.Object.FindFirstObjectByType<MapViewController>();
        serialized.FindProperty("fieldBounds").objectReferenceValue =
            new SerializedObject(map).FindProperty("mapBounds").objectReferenceValue;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (race.Rules == null || serialized.FindProperty("fieldBounds").objectReferenceValue == null)
            throw new InvalidOperationException("Race rules or field bounds failed to resolve.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(previous) && previous != GameScene) EditorSceneManager.OpenScene(previous);
        Debug.Log("RACE_NETWORK_CONFIGURED");
    }
}

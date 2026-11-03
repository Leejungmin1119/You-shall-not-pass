using System;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>main의 씬 연결을 검사한다. 이전 레이스 관리자를 중복으로 추가하지 않는다.</summary>
public static class RaceNetworkSetup
{
    public const string GameScene = "Assets/Scenes/SampleScene.unity";
    public const string PlayerPrefab = "Assets/MyGame/Prefabs/NetworkPlatformPlayer.prefab";

    [MenuItem("Tools/Network Practice/Configure Race Networking")]
    public static void Configure() => Validate();

    [MenuItem("Tools/Network Practice/Validate Merged Race Networking")]
    public static void ValidateMenu() => Debug.Log(Validate());

    public static string Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        if (prefab == null || prefab.GetComponent<PlayerInventory>()?.syncMode != SyncMode.Owner ||
            prefab.GetComponent<PlayerHeldItem>()?.syncMode != SyncMode.Owner ||
            prefab.GetComponent<PlayerNetworkTransform>()?.syncDirection != SyncDirection.ClientToServer)
            throw new InvalidOperationException("Player inventory ownership or movement configuration is invalid.");
        var scene = SceneManager.GetSceneByPath(GameScene);
        bool closeAfter = !scene.isLoaded;
        if (closeAfter) scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Additive);
        try
        {
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach (var transform in transforms)
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                    throw new InvalidOperationException("Missing script: " + transform.name);
            if (transforms.Count(t => t.GetComponent<RaceManager>() != null) != 1 ||
                transforms.Count(t => t.GetComponent<SpearNetwork>() != null) != 1 ||
                transforms.Count(t => t.GetComponent<ConveyorController>() != null) != 1)
                throw new InvalidOperationException("The game needs one race manager, trap network and conveyor.");
            return "MERGED_RACE_SETUP_VALIDATED";
        }
        finally { if (closeAfter) EditorSceneManager.CloseScene(scene, true); }
    }
}

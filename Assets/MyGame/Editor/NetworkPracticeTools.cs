using System;
using System.IO;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class NetworkPracticeTools
{
    public const string ScenePath = "Assets/MyGame/Scenes/NetworkPlatform.unity";
    [MenuItem("Tools/Network Practice/Open Platform Scene")]
    public static void OpenScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Tools/Network Practice/Validate Setup")]
    public static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyGame/Prefabs/NetworkPlatformPlayer.prefab");
        if (prefab == null || prefab.GetComponent<NetworkIdentity>() == null ||
            prefab.GetComponent<NetworkPlayerController>() == null ||
            prefab.GetComponent<NetworkPlayerScore>() == null ||
            prefab.GetComponent<NetworkPlayerEffects>() == null ||
            prefab.GetComponent<NetworkPlayerBombSpawner>() == null ||
            prefab.GetComponent<PlayerMovement>() == null)
            throw new Exception("Network platform player components missing.");
        if (prefab.GetComponent<InputManager>() != null)
            throw new Exception("Network player must use the scene input manager.");
        if (prefab.GetComponent<PlayerMovement>().moveStats == null)
            throw new Exception("Movement stats missing.");
        var sync = prefab.GetComponent<NetworkTransformReliable>();
        if (sync == null || sync.syncDirection != SyncDirection.ClientToServer)
            throw new Exception("Owner movement synchronization missing.");
        var opened = SceneManager.GetSceneByPath(ScenePath);
        bool closeAfter = !opened.IsValid();
        if (closeAfter) opened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var components = opened.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true));
            foreach (var t in components)
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    throw new Exception("Missing script: " + t.name);
            var manager = opened.GetRootGameObjects().Select(g => g.GetComponent<PracticeNetworkManager>()).FirstOrDefault(m => m != null);
            if (manager == null || manager.playerPrefab != prefab || manager.transport == null || manager.spawnPrefabs.Count != 1)
                throw new Exception("Network manager references invalid.");
            Debug.Log("NETWORK_SETUP_VALIDATED: player, movement, transport, bomb registration and scene scripts.");
        }
        finally { if (closeAfter) EditorSceneManager.CloseScene(opened, true); }
    }
}

#if UNITY_EDITOR
using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 게임 플레이어 프리팹의 NetworkTransformReliable을 PlayerNetworkTransform으로 바꾼다.
/// 컴포넌트를 지우고 새로 붙이지 않고 스크립트 참조만 바꿔서, 인스펙터 설정값과 컴포넌트 순서가 그대로 남는다.
/// 이미 바뀌어 있으면 아무것도 하지 않는다(다시 눌러도 같은 결과).
/// </summary>
public static class PlayerNetworkTransformSetup
{
    private const string PrefabPath = "Assets/MyGame/Prefabs/NetworkPlatformPlayer.prefab";

    [MenuItem("Tools/네트워크/플레이어 위치 동기화를 PlayerNetworkTransform으로 교체")]
    public static void Replace()
    {
        MonoScript newScript = FindScript(typeof(PlayerNetworkTransform));
        if (newScript == null)
        {
            Debug.LogError("PlayerNetworkTransform 스크립트를 찾지 못했습니다.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            int replaced = 0;
            foreach (NetworkTransformReliable component in root.GetComponentsInChildren<NetworkTransformReliable>(true))
            {
                // 하위 클래스(이미 교체된 것)는 건드리지 않는다.
                if (component.GetType() != typeof(NetworkTransformReliable))
                    continue;

                SerializedObject serialized = new SerializedObject(component);
                serialized.FindProperty("m_Script").objectReferenceValue = newScript;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                replaced++;
            }

            if (replaced > 0)
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

            Debug.Log($"[위치 동기화] {PrefabPath}: {replaced}개 교체" + (replaced == 0 ? " (이미 교체됨)" : ""));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static MonoScript FindScript(System.Type type)
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:MonoScript {type.Name}"))
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == type)
                return script;
        }

        return null;
    }
}
#endif

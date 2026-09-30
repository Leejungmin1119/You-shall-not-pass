#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 서버 담당의 NetworkPlatform 씬을 건드리지 않고, 복사본에 창 컴포넌트를 얹은 시험 씬을 만든다.
/// 이미 시험 씬이 있으면 지우지 않고 그 씬을 열어 빠진 것만 채운다(다시 눌러도 같은 결과).
/// </summary>
public static class SpearNetworkSetup
{
    private const string SourceScenePath = "Assets/MyGame/Scenes/NetworkPlatform.unity";
    private const string TestScenePath = "Assets/Scenes/SpearNetworkTest.unity";

    [MenuItem("Tools/프로토타입/창 네트워크 시험 씬 만들기")]
    public static void CreateTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
        {
            if (!AssetDatabase.CopyAsset(SourceScenePath, TestScenePath))
            {
                Debug.LogError($"{SourceScenePath}를 복사하지 못했습니다.");
                return;
            }
        }

        EditorSceneManager.OpenScene(TestScenePath);

        SpearDropper dropper = Object.FindFirstObjectByType<SpearDropper>(FindObjectsInactive.Include);
        if (dropper == null)
        {
            GameObject dropperObject = new GameObject("SpearDropper");
            Undo.RegisterCreatedObjectUndo(dropperObject, "창 투하기 배치");
            dropper = dropperObject.AddComponent<SpearDropper>();
        }

        if (Object.FindFirstObjectByType<SpearNetwork>(FindObjectsInactive.Include) == null)
        {
            GameObject networkObject = new GameObject("SpearNetwork");
            Undo.RegisterCreatedObjectUndo(networkObject, "창 네트워크 배치");
            networkObject.AddComponent<SpearNetwork>();
        }

        // 지면 레이어를 따로 적으면 이동 쪽 설정과 어긋날 수 있어, 이동 스탯의 값을 그대로 가져온다.
        string[] guids = AssetDatabase.FindAssets("t:PlayerMovementStats");
        if (guids.Length == 0)
        {
            Debug.LogWarning("PlayerMovementStats 에셋이 없어 SpearDropper의 GroundLayer를 채우지 못했습니다.");
        }
        else
        {
            PlayerMovementStats stats = AssetDatabase.LoadAssetAtPath<PlayerMovementStats>(
                AssetDatabase.GUIDToAssetPath(guids[0]));

            Undo.RecordObject(dropper, "창 투하기 배치");
            dropper.GroundLayer = stats.GroundLayer;
            EditorUtility.SetDirty(dropper);
        }

        EditorSceneManager.SaveScene(dropper.gameObject.scene);
    }
}
#endif

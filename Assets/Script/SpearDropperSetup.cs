#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 열려 있는 씬에 SpearDropper를 배치하는 메뉴. 여러 번 눌러도 하나만 남는다.
/// </summary>
public static class SpearDropperSetup
{
    private const string ObjectName = "SpearDropper";

    [MenuItem("Tools/프로토타입/창 투하기 배치")]
    public static void Place()
    {
        SpearDropper dropper = Object.FindFirstObjectByType<SpearDropper>(FindObjectsInactive.Include);
        if (dropper == null)
        {
            GameObject dropperObject = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(dropperObject, "창 투하기 배치");
            dropper = dropperObject.AddComponent<SpearDropper>();
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

        EditorSceneManager.MarkSceneDirty(dropper.gameObject.scene);
        Selection.activeGameObject = dropper.gameObject;
    }
}
#endif

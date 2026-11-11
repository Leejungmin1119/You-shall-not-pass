#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class JackOLanternDeathSetup
{
    private const string StagePath = "Assets/Scenes/Stage1.unity";
    private const int FrameCount = 12;

    [MenuItem("Tools/캐릭터/잭오랜턴 사망 스프라이트 연결")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play를 종료한 뒤 적용하세요.");

        Sprite[] forward = LoadFrames("Forward");
        Sprite[] backward = LoadFrames("Backward");
        Scene stage = SceneManager.GetSceneByPath(StagePath);
        bool openedForSetup = !stage.isLoaded;
        if (openedForSetup)
            stage = EditorSceneManager.OpenScene(StagePath, OpenSceneMode.Additive);

        try
        {
            if (stage.isDirty)
                throw new InvalidOperationException("Stage1의 작업 중인 변경을 저장한 뒤 적용하세요.");

            DeathSequence sequence = stage.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<DeathSequence>(true)).Single();
            var serialized = new SerializedObject(sequence);
            SetFrames(serialized.FindProperty("jackForward"), forward);
            SetFrames(serialized.FindProperty("jackBackward"), backward);
            if (serialized.ApplyModifiedProperties() && !EditorSceneManager.SaveScene(stage))
                throw new InvalidOperationException("Stage1의 사망 스프라이트 연결을 저장하지 못했습니다.");
        }
        finally
        {
            if (openedForSetup && !stage.isDirty)
                EditorSceneManager.CloseScene(stage, true);
        }
    }

    private static Sprite[] LoadFrames(string direction)
    {
        string prefix = $"JackOLantern_Death_{direction}";
        var sprites = AssetDatabase.LoadAllAssetsAtPath($"Assets/Image/{prefix}.png")
            .OfType<Sprite>().ToDictionary(sprite => sprite.name);
        var frames = new Sprite[FrameCount];
        for (int index = 0; index < frames.Length; index++)
        {
            string name = $"{prefix}_{index + 1:00}";
            if (!sprites.TryGetValue(name, out frames[index]))
                throw new InvalidOperationException($"사망 시트의 {name} 스프라이트가 없습니다.");
        }

        return frames;
    }

    private static void SetFrames(SerializedProperty property, Sprite[] frames)
    {
        property.arraySize = frames.Length;
        for (int index = 0; index < frames.Length; index++)
            property.GetArrayElementAtIndex(index).objectReferenceValue = frames[index];
    }
}
#endif

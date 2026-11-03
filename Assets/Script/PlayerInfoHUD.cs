using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>좌측 상단에 이름·플레이어 이미지·점수를 순서대로 표시한다.</summary>
public sealed class PlayerInfoHUD : MonoBehaviour
{
    [Serializable]
    private sealed class PlayerSlot
    {
        [SerializeField] private UnityEngine.UI.Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text scoreText;

        private PlayerScore score;
        public NetworkPlayerProfile Profile { get; private set; }

        /// <summary>칸을 플레이어에 연결하고 현재 값 표시와 변경 알림 구독을 수행한다.</summary>
        public void Bind(NetworkPlayerProfile profile)
        {
            if (Profile == profile && profile != null)
                return;

            Clear();
            if (profile == null)
                return;

            Profile = profile;
            score = profile.GetComponent<PlayerScore>();
            score.ScoreChanged += RefreshScore;

            var visual = profile.GetComponent<SpriteRenderer>();
            if (visual != null)
                icon.sprite = visual.sprite;

            icon.enabled = icon.sprite != null;
            RefreshName();
            RefreshScore(score.Score);
        }

        /// <summary>값 변경 구독을 해제하고 빈 플레이어 칸으로 되돌린다.</summary>
        public void Clear()
        {
            if (score != null)
                score.ScoreChanged -= RefreshScore;

            Profile = null;
            score = null;
            if (icon != null)
                icon.enabled = false;

            if (nameText != null)
            {
                nameText.text = "—";
                nameText.color = Color.white;
            }

            if (scoreText != null)
                scoreText.text = "Score: —";

        }

        /// <summary>현재 이름을 표시하며 사용자 이름은 리치 텍스트로 해석하지 않는다.</summary>
        public void RefreshName()
        {
            nameText.richText = false;
            nameText.text = Profile.PlayerName;
            nameText.color = Profile.PlayerColor;
        }

        /// <summary>점수 변경이 있을 때만 해당 칸의 숫자를 갱신한다.</summary>
        private void RefreshScore(int value)
        {
            scoreText.SetText("Score: {0}", value);
        }

    }

    [SerializeField] private PlayerSlot[] slots = Array.Empty<PlayerSlot>();
    [SerializeField] private GridLayoutGroup layout;

    private readonly List<NetworkPlayerProfile> players = new List<NetworkPlayerProfile>(4);

    /// <summary>씬에 연결된 UI 너비에 맞춰 처음 표시할 열 수를 설정한다.</summary>
    private void Awake()
    {
        OnRectTransformDimensionsChange();
    }

    /// <summary>게임 플레이어를 등록하고 모든 화면에서 같은 네트워크 ID 순서로 배치한다.</summary>
    public void RegisterPlayer(NetworkPlayerProfile player)
    {
        if (players.Contains(player) || !player.TryGetComponent<PlayerScore>(out _))
            return;

        players.Add(player);
        players.Sort((left, right) => left.netId.CompareTo(right.netId));
        RefreshSlots();
    }

    /// <summary>퇴장한 플레이어를 제거하고 남은 플레이어의 칸을 정리한다.</summary>
    public void UnregisterPlayer(NetworkPlayerProfile player)
    {
        if (players.Remove(player))
            RefreshSlots();
    }

    /// <summary>참가자가 바뀔 때만 칸을 다시 연결하고 빈 칸을 비운다.</summary>
    private void RefreshSlots()
    {
        for (int index = 0; index < slots.Length; index++)
            slots[index].Bind(index < players.Count ? players[index] : null);
    }

    /// <summary>이름이 변경된 플레이어의 칸만 갱신한다.</summary>
    public void RefreshName(NetworkPlayerProfile player)
    {
        foreach (PlayerSlot slot in slots)
        {
            if (slot.Profile == player)
            {
                slot.RefreshName();
                return;
            }
        }
    }

    /// <summary>일반 화면은 가로 4칸, 좁은 화면은 2열로 배치해 글자 겹침을 줄인다.</summary>
    private void OnRectTransformDimensionsChange()
    {
        if (layout == null)
            return;

        float width = ((RectTransform)transform).rect.width;
        int columns = width >= 360f ? 4 : 2;
        layout.constraintCount = columns;
        layout.cellSize = new Vector2((width - layout.spacing.x * (columns - 1)) / columns, 152f);
    }

    /// <summary>씬 종료 시 점수 이벤트 연결을 모두 해제한다.</summary>
    private void OnDestroy()
    {
        foreach (PlayerSlot slot in slots)
            slot.Clear();

        players.Clear();
    }
}

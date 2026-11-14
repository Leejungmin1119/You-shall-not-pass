using System;
using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;

/// <summary>기록 깃발과 현재 공격자의 점을 표시한다. 플레이어 입력은 받지 않는다.</summary>
public sealed class RaceProgressUI : MonoBehaviour
{
    [Serializable]
    private sealed class RecordMarker
    {
        public RectTransform Root;
        public UnityEngine.UI.Image Fill;
        public TMP_Text Name;
    }

    [SerializeField] private RaceProgress progress;
    [SerializeField] private RectTransform track;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform currentMarker;
    [SerializeField] private UnityEngine.UI.Image currentDot;
    [SerializeField] private TMP_Text currentLabel;
    [SerializeField] private RecordMarker[] flags = new RecordMarker[0];

    private readonly List<NetworkPlayerProfile> profiles = new List<NetworkPlayerProfile>(4);
    private float nextRosterRefresh;
    private uint currentPlayerId;
    private NetworkPlayerProfile currentProfile;

    /// <summary>참가자 생성 순서와 관계없이 표시 직후 목록을 확인한다.</summary>
    private void OnEnable()
    {
        nextRosterRefresh = 0f;
    }

    /// <summary>기존 위치 동기화로 점을 움직이고 확정된 기록만 깃발에 반영한다.</summary>
    private void LateUpdate()
    {
        if (progress == null || !progress.enabled || !NetworkClient.active)
        {
            group.alpha = 0f;
            return;
        }

        group.alpha = 1f;

        if (Time.unscaledTime >= nextRosterRefresh)
        {
            RefreshRoster();
            nextRosterRefresh = Time.unscaledTime + 0.25f;
        }

        RefreshFlags();
        RefreshCurrentMarker();
    }

    /// <summary>접속·퇴장을 반영할 때만 참가자 목록을 갱신하고 네트워크 번호순으로 정렬한다.</summary>
    private void RefreshRoster()
    {
        profiles.Clear();

        foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
        {
            if (identity.TryGetComponent(out NetworkPlayerProfile profile) &&
                identity.TryGetComponent<PlayerState>(out _))
                profiles.Add(profile);
        }

        profiles.Sort(CompareProfiles);
    }

    /// <summary>모든 화면에서 동일한 순서로 깃발을 연결한다.</summary>
    private static int CompareProfiles(NetworkPlayerProfile left, NetworkPlayerProfile right)
    {
        return left.netId.CompareTo(right.netId);
    }

    /// <summary>본인을 포함한 모든 플레이어의 확정 기록에 이름과 선택 색상의 깃발을 표시한다.</summary>
    private void RefreshFlags()
    {
        for (int index = 0; index < flags.Length; index++)
        {
            RecordMarker flag = flags[index];
            NetworkPlayerProfile profile = index < profiles.Count ? profiles[index] : null;
            float record = 0f;
            bool visible = profile != null &&
                progress.BestRecords.TryGetValue(profile.netId, out record);

            if (flag.Root.gameObject.activeSelf != visible)
                flag.Root.gameObject.SetActive(visible);

            if (!visible)
                continue;

            PlaceMarker(flag.Root, record);
            flag.Fill.color = profile.PlayerColor;
            flag.Name.text = profile.PlayerName;
            flag.Name.color = Color.black;
        }
    }

    /// <summary>현재 공격자만 점으로 표시하며, 자기 차례에는 YOU, 관전자에게는 공격자 이름을 표시한다.</summary>
    private void RefreshCurrentMarker()
    {
        bool visible = progress.TryGetAttacker(out PlayerState attacker);

        if (currentMarker.gameObject.activeSelf != visible)
            currentMarker.gameObject.SetActive(visible);

        if (!visible)
            return;

        if (currentPlayerId != attacker.netId || currentProfile == null)
        {
            currentPlayerId = attacker.netId;
            currentProfile = attacker.GetComponent<NetworkPlayerProfile>();
        }

        PlaceMarker(currentMarker, progress.GetCurrentProgress(attacker));
        currentDot.color = currentProfile != null ? currentProfile.PlayerColor : Color.white;
        currentLabel.text = attacker.isLocalPlayer ? "YOU" :
            currentProfile != null ? currentProfile.PlayerName : "";
    }

    /// <summary>화면 너비에 맞춰진 진행 선의 양 끝 사이에서 위치를 변경한다.</summary>
    private void PlaceMarker(RectTransform marker, float value)
    {
        marker.anchoredPosition = new Vector2(Mathf.Lerp(track.rect.xMin, track.rect.xMax, value), 0f);
    }
}

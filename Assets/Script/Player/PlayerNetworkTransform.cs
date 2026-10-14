using Mirror;
using UnityEngine;

/// <summary>
/// 플레이어 위치 동기화. NetworkTransformReliable과 같고 순간이동 처리만 다르다.
///
/// Mirror 96.0.1의 순간이동(OnTeleport)은 ResetState()를 부르는데, NetworkTransformReliable이 이를
/// 덮어써서 델타 압축 기준값(lastSerialized/lastDeserializedPosition)까지 0으로 지운다.
/// 호스트가 조종하는 캐릭터는 ServerTeleport 즉시 한 번, 호스트로 돌아온 RpcTeleport에서 한 번 더 지워지고,
/// 그 사이 목적지가 원격으로 전송되면 원격 화면 위치가 "실제 위치 + 목적지"로 고정된다
/// (교대 순간이동 8회 재현 8/8, 원격 소유 캐릭터는 0/8).
///
/// Mirror 주석이 의도한 대로 보간 버퍼만 비우고 기준값은 유지한다. 기준값은 양쪽이 같은 값을 계속
/// 주고받으며 갱신되므로, 순간이동도 "큰 차이"로 정상 전송된다.
/// </summary>
public class PlayerNetworkTransform : NetworkTransformReliable
{
    protected override void OnTeleport(Vector3 destination)
    {
        target.position = destination;
        ClearSnapshots();
    }

    protected override void OnTeleport(Vector3 destination, Quaternion rotation)
    {
        target.position = destination;
        target.rotation = rotation;
        ClearSnapshots();
    }

    private void ClearSnapshots()
    {
        // 이전 위치의 스냅샷이 남아 있으면 목적지로 가지 않고 그쪽으로 보간해 버린다.
        serverSnapshots.Clear();
        clientSnapshots.Clear();
    }
}

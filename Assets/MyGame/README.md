# 서버 실습 적용

`Scenes/NetworkPlatform.unity`를 열고 Play → Host (Server + Client)를 누릅니다.
- 이동/점프: 기존 Check.inputactions 바인딩 사용
- P: 서버에서 점수 증가 → SyncVar로 이름표 갱신
- R: ClientRpc로 해당 플레이어의 빨간 효과를 모든 관찰 클라이언트에 표시
- T: TargetRpc로 요청자에게만 파란 효과 표시
- B: 서버에 정상 위치로 폭탄 생성 요청
- Y: 거리 검증 확인용 비정상 위치 요청

두 번째 실행 창에서 localhost와 Client를 선택하면 로컬 접속을 시험할 수 있습니다. 다른 컴퓨터는 호스트의 LAN 주소로 접속합니다(KCP UDP 7777).

기존 SampleScene과 이동 코드는 유지했습니다. NetworkPlatform 씬은 기존 지형/입력/스탯을 사용하고, 플레이어는 접속 시 네트워크 프리팹으로 생성합니다. 본인 플레이어만 이동/물리를 계산하고 위치는 ClientToServer NetworkTransform으로 전송합니다. 이 구조는 기존 실습과 같은 클라이언트 권한 이동이며 서버 이동 검증은 포함하지 않습니다.

플레이어 네트워크 기능은 역할별로 분리되어 있습니다.

- `Scripts/Player/NetworkPlayerController.cs`: 이동과 로컬 물리 활성화
- `Scripts/Player/NetworkPlayerScore.cs`: 점수 Command와 SyncVar
- `Scripts/Player/NetworkPlayerEffects.cs`: ClientRpc와 TargetRpc 효과
- `Scripts/Player/NetworkPlayerBombSpawner.cs`: 폭탄 요청, 서버 검증, Spawn
- `Scripts/Network/PracticeNetworkManager.cs`: 접속과 서버 생명주기
- `Scripts/World/NetworkBomb.cs`: 서버 기준 폭탄 제거

폭탄 생성은 서버 권한 구조를 사용합니다. 클라이언트는 원하는 생성 위치만 Command로 보내며, 서버가 프리팹 연결 여부, 남은 개수, 쿨타임, 플레이어와의 거리를 검사합니다. 승인된 요청만 서버에서 생성해 모든 클라이언트에 공유하고, 거절 이유는 요청자에게만 TargetRpc로 전달합니다.

`Scenes/NetworkPractice.unity`는 원래 실습 씬입니다. 이 씬은 WASD/방향키 이동, Space 점수 증가를 사용합니다. 두 씬 모두 새 Input System으로 동작합니다.

Tools → Network Practice → Validate Setup에서 연결 설정을 검사할 수 있습니다. 빌드 씬 목록의 첫 번째 씬으로 NetworkPlatform을 등록했습니다. 기존 SampleScene도 목록에 유지했습니다.

검증: 게임 코드 Unity 컴파일, 편집기 도구 별도 C# 컴파일, 씬/프리팹 GUID 연결 검사 통과. 실제 Host/Client 실행 검증은 자동 검증 환경의 IL 후처리 프로세스 연결 문제로 완료하지 못했습니다.

## 9~10일차 기능

- H: 서버에 자기 캐릭터 피해 20 요청 (연습용)
- 체력은 SyncVar로 공유하고 HP 텍스트에 표시
- 체력 0: 플랫폼 이동의 MovementLocked 활성화 및 속도 0
- F: 준비 상태 전환, Ready/Not Ready 텍스트 동기화
- Host에서 Enter: 정확히 4명이 접속하고 모두 준비했는지 검사
- 최대 접속 수는 4명, Enter는 시작 가능 로그만 출력
- 씬 전환과 부활은 아직 구현하지 않음

이동은 기존 ClientToServer 방식이며 사망 이동 제한도 정상 클라이언트의 동작 제한이다. 서버의 비정상 이동 검증은 별도 구현이 필요하다.

## 11일차: 4인 로비와 씬 전환

빌드는 LobbyScene에서 시작합니다. 로비 화면은 CapstoneLobbyUI가 Play 시 생성하는 임시 UI이며 영어로 표시합니다.
- Create room: Host 시작. Join room: 입력한 LAN IP로 Client 접속 (로컬 테스트는 localhost).
- 4개 슬롯은 실제 접속 플레이어와 동기화된 준비 상태를 표시합니다.
- Ready up 또는 F: 준비 상태 변경.
- 정확히 4명이 모두 준비하면 Host의 Start game 버튼 / Enter로 GameScene에 함께 이동.
- LobbyScene의 NetworkManager는 유지되며 GameScene에는 중복 매니저가 없습니다.
- 로비에서는 플레이어 이동과 물리 계산을 멈춥니다.
- Leave / Cancel 또는 게임의 Leave game으로 접속 종료. Offline Scene 설정으로 LobbyScene에 복귀합니다.
- NetworkPlatform / NetworkPractice는 기존 실습용으로 남겨둡니다.

검증 시 최신 빌드 4개(Host 포함)로 접속해 준비 전 시작 차단, 준비 표시, 전원 씬 이동, 접속 종료를 확인하세요.

이번 변경 검증: Unity의 기존 참조 목록을 사용한 C# 컴파일(경고 없음), 씬 내부 참조 검사 통과. Unity 자동 실행은 소켓 권한 제한으로 중단되어 화면 렌더링과 실제 4인 접속은 미검증입니다.

## 화면 중앙 알림

준비 조건을 충족하지 않고 Host가 Enter를 누르면 READY UP!을 표시합니다.
게임 씬 로딩 완료 시 각 클라이언트에 GAME START!를 표시합니다.
알림은 약 2.4초 동안 등장 확대, 부드러운 밝기 변화, 페이드아웃을 적용합니다.
CapstoneLobbyUI의 Notice Duration으로 표시 시간을 조절할 수 있습니다.
현재 사용자가 수정한 2인 시작 조건은 유지합니다.

## 우주선 대기실

LobbyScene의 Play 화면을 금속 바닥, 화물 상자, 환기구와 별이 보이는 우주선 대기실로 변경했습니다.
접속한 플레이어는 작은 정비 로봇으로 표시되며 WASD/방향키로 평면 이동합니다.
기존 ClientToServer NetworkTransform 위치를 UI 캐릭터 표시에도 사용합니다.
로비 물리는 꺼져 있고 게임 씬에서는 기존 플랫폼 이동을 사용합니다.
색은 netId 기반이며 현재 2인 시작 조건은 유지했습니다. 장식 상자는 이동을 막지 않습니다.

## 12~13일차: 룸 플레이어 분리와 이름 입력

LobbyScene은 CapstoneRoomManager(NetworkRoomManager 상속)를 사용합니다.
Room Player Prefab은 CapstoneRoomPlayer, 게임 Player Prefab은 NetworkPlatformPlayer입니다.
로비 오른쪽 PLAYER NAME에 입력하고 Save name을 누르면 서버가 이름을 검증·저장합니다.
공백 이름은 거절하고 제어 문자와 TMP 태그 문자를 제거하며 최대 16자로 제한합니다.
승인된 이름은 로비에서 공유되고 게임 시작 시 OnRoomServerSceneLoadedForPlayer에서 NetworkPlayerProfile로 복사됩니다.
게임의 이름·점수 표시는 NetworkPlayerScore가 함께 갱신합니다.
이름 변경 시 준비를 해제합니다. 텍스트 입력 중 WASD/F/Enter 게임 입력은 처리하지 않습니다. Esc로 입력 포커스를 해제할 수 있습니다.
Min Players=2(현재 테스트 설정), Max Connections=4. 전원 준비 후 Host가 Launch game/Enter로 시작합니다.
기존 PracticeNetworkManager와 연습 씬은 유지합니다.

검증: 이번 이름 기능의 C# 컴파일과 씬·프리팹 내부 참조 검사 통과. Unity 창 접근 시간 초과로 실제 Host/Client 이름 동기화·씬 전환은 미검증입니다. 최신 빌드로 이름 저장, 양쪽 로비 표시, 게임 이름표 유지, 입력 중 단축키 차단을 확인하세요.

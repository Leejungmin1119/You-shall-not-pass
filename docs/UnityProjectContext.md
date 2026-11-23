# 기획 대비 개발 진행 상태

분석일: 2026-11-20 (한국 시간)  
기준 커밋: `4c2e078487c7fdc40b3b8e4a05dde0dc0b7621ef`  
프로젝트: `C:/Users/lee/Documents/GitHub/You-shall-not-pass`

## 참조 기준과 확인 범위

- 지침: `.claude/CLAUDE.md`. Git 변경 작업과 임의 폴더 재구성은 하지 않는다.
- 이번 기획 기준: `C:/Users/lee/Downloads/c266a51a-ba26-4d57-8802-e0297ac7de4a_ExportBlock-6f8b7f9d-d39a-4e2c-a2ae-dd2cc8f6ce4a`의 Markdown 본문과 관련 하위 명세.
- 이번 사용자 지정이 `.claude/CLAUDE.md`에 적힌 11월 8일 내보내기 경로보다 우선한다. 지침 파일 자체는 수정하지 않았다.
- 코드, 씬·프리팹의 직렬화 설정, 아이템 데이터, 커밋 기록, 저장된 검증 기록을 대조했다. Unity 실행·컴파일·빌드·다중 기기 플레이를 이번에 수행하지 않았다.
- 문서의 ‘구현 확인’은 코드 또는 연결 설정의 존재를 뜻한다. 현재 버전의 실행 성공이나 기획상 완성을 뜻하지 않는다.
- 파란색 의견·[제안]·[미정], 과거 기록, ‘쓰지 않게 된 아이디어’를 확정 요구사항과 구분한다. Markdown에서 의견의 색상이 보존되지 않은 부분은 팀 확정 여부를 단정하지 않는다.

## 현재 단계

기본 공수 교대 경기와 네트워크 기반을 갖춘 프로토타입이다. 최근에는 캐릭터 그림, 사망 연출, 슬로우 스킬, 발소리·BGM·배경까지 추가됐다. 남은 큰 작업은 기획과 규칙 합의, 결과 전달과 조작 피드백, 현재 버전의 실제 멀티플레이 검증이다. 작업별 규모가 달라 전체 진행률을 백분율로 계산하지 않았다.

| 영역 | 확인된 구현 | 남은 차이·확인 사항 | 근거 |
| --- | --- | --- | --- |
| 로비 | 최대 4명, 전원 준비 후 5초 카운트다운, 자리 배정, 이름·캐릭터 선택, 호스트 강퇴 | 4종·중복 금지 기획과 달리 3종·중복 허용. 공개 방·입장 승인·채팅·방장 승계는 없음 | `CapstoneRoomManager`, `CapstoneRoomPlayer`, `CapstoneLobbyUI` |
| 경기 규칙 | 최초 공격자 무작위, 이후 참가 순서 교대. 공격자만 이동, 수비자만 함정 사용. 사망·골인 시 교대와 아이템·코스트 초기화 | 기획 골인 +2 / 코드 +1. 카드 뽑기 방식은 현재 순서 결정과 다르며 범위 확정 필요 | `RaceManager`, `rolechangeManager`, `PlayerState` |
| 종료 | 판 전체 900초, 20점 선취, 최고점·공동 승자, 참가자 이탈 시 무승부 종료, 0.5초 뒤 로비 | 결과 화면과 골인·적중 점수의 별도 누적이 없음 | `RaceManager.EndMatch`, `PlayerScore` |
| 경제·보유 | 수비자 시작 100, 5초마다 +10, 상한 300. 공유 컨베이어, 인벤토리 3칸, 우클릭 즉시 들기, 숫자키 선택·취소 | 실패 이유가 화면에 나오지 않음. 부족 코스트 회색 표시·사용 불가 표시·드래그 정렬은 남음 | `PlayerCost`, `ConveyorController`, `PlayerInventory`, `PlayerHeldItem` |
| 아이템 | 폭탄·창 1~3개·커서 조종 미사일·농구공·바위. 서버 구매·사용·적중 및 소유자 득점 경로 | 농구공·바위의 이번 학기 포함 여부 미정. 야구 배트·빙의 장치는 다음 학기 보류이므로 이번 필수 미완성으로 세지 않음 | `SpearNetwork`, `Bomb`, `Spear`, `Missile`, `RoundTrap`, `Assets/SO` |
| 이동·피격 | 이동·가변 점프·벽 점프, 코요테·버퍼, 역할별 표시·충돌. 적중 후 6초 사망 연출, 히트스톱·슬로모션, 시간 정지 보정 | 골인 0.4초·낙사 0.3초 대기, 무적 깜빡임, 골인·득점 시각 연출은 남음 | `PlayerMovement`, `PlayerJumpMotor`, `PlayerState`, `DeathSequence`, `RaceManager` |
| 슬로우 스킬 | E, 공격자 전용. 5초 0.1배 + 1초 복귀, 공격자 이동·판 시간은 유지. 무대 흑백과 BGM 전환 | 사용 가능 표시 없음. 사망 때만 횟수 초기화되므로 스킬 사용 후 골인하면 다음 공격 차례에도 소모 상태 — 의도 확인 필요 | `PlayerTimeSkill`, `RaceManager`, `TimeSkillGrayscale`, `BgmPlayer` |
| 맵 | 실제 게임 씬 Stage1. 고정 대포·회전 불·쿵쿵이·사라지는 바닥·이동 발판 | 난이도·2~3인 밸런스는 사람의 플레이 확인 필요. 과거 문서의 라운드별 동적 맵을 현재 필수로 단정하지 않음 | `LobbyScene.unity` GameplayScene, `Stage1.unity`, 각 맵 함정 스크립트 |
| 화면·소리 | 점수·타이머·카운트다운·진행도 HUD, 공격자 추적·Tab 전체 보기, 할로윈 패럴랙스, BGM·발소리·일부 함정음 | YOUR TURN, 종료·이탈 알림, 스킬 상태, 일부 한글화·폰트, 설정·튜토리얼은 남음 | `RaceTimerUI`, `PlayerInfoHUD`, `RaceProgressUI`, `MapViewController`, `SoundPlayer`, `BgmPlayer` |
| 접속 | Mirror Host/KCP, LAN 검색·직접 IP, 중계 서버 코드와 배포 설정 | LobbyScene의 Relay URL이 비어 있음. 공개 인터넷 서비스 완료로 볼 수 없음 | `CapstoneLanDiscovery`, `LanRoomConnector`, `PasswordRelayTransport`, `Server/PasswordRelay` |

## 기획서 자체에서 먼저 정리할 부분

1. **골인 점수**: 1·2·3·9장과 6-7은 +2를 요구하고, 코드 및 6장 일부 우선순위 표는 +1이다. 10장도 팀 협의가 필요하다고 적는다.
2. **공격 순서**: 11월 6일 확정 기록은 최초 무작위 + 참가 순서다. 3장·7-6에는 카드 뽑기가 추가돼 있다. 단순 연출 추가가 아니라 서버 순서 규칙 변경이다.
3. **캐릭터**: 4종·중복 금지 팀 결정과 현재 3종·중복 허용의 차이가 있다. 4인 중복 금지를 하려면 네 번째 캐릭터가 먼저 필요하다.
4. **시작 보호**: 4장은 반경 3유닛·2초를 제시하지만 3장·5-0·실제 코드는 무적 0.5초 동안 반경 1.5유닛이다. 보호 규칙을 하나로 맞춰야 한다.
5. **복귀·역할 기록**: 6장 일부와 10장에는 ‘역할 구분 없음’, 피격·낙사 후 10유닛 뒤 복귀 같은 이전 모드 설명이 남아 있다. 현재 공수 모드는 차례가 끝나면 출발점으로 초기화한다.
6. **결과 화면과 로비 정책**: 3장은 0.5초 뒤 전원 로비, 7-8은 개별 [로비로]/[나가기]와 방장 승계다. Mirror의 현재 호스트 구조에서 개별 씬 이동·호스트 승계까지 하려면 UI 외 네트워크 설계가 필요하다.
7. **최신 소리 상태**: 7-9와 10장 일부는 배경음악·소리가 없다고 적지만, 9장과 현재 코드는 11월 17일 추가된 BGM·발소리·착지음을 반영한다. ‘없음’ 목록을 그대로 작업 목록으로 쓰면 중복 작업이 된다.

## 다음 작업 권장 순서

### 1. 이번 목표 범위와 규칙 확정

골인 +1/+2, 공격 순서, 캐릭터 4종·중복, 보호 범위, 슬로우 재충전 기준을 정한다. 결과 화면은 우선 승자·점수·종료 사유를 보여주고 방장이 전체 로비로 넘기는 범위부터 할지, 7-8의 개별 복귀·방장 승계까지 할지 나눈다. 다음 학기로 보류한 아이템과 미정 로비 확장 기능을 이번 필수 범위에 자동 포함하지 않는다.

완료 기준: 최신 요구사항 표에서 확정·보류·미정이 나뉘고, 각 규칙의 값과 적용 모드가 하나씩 정해져 있다.

### 2. 최신 버전으로 실제 2~4인 경기 확인

현재 코드를 새로 빌드해 로비 → 준비 → Stage1 → 수비자 구매·사용 → 적중·낙사·골인 → 교대 → 종료 → 재입장을 확인한다. 특히 11월 16일 이후 추가된 슬로우 스킬의 생성 중 함정·사망·골인·흑백 해제·BGM 복귀를 원격 화면에서도 확인한다. 가능하면 실제 서로 다른 PC에서 실행한다.

완료 기준: 각 화면에서 공격자·점수·함정·위치·스킬 해제가 일치하고, 한 판과 재경기까지 진행된다. 시험용 종료 조건 검사와 실제 사람이 플레이한 경기를 구분해 기록한다.

### 3. 경기 결과와 필수 안내 완성

결과 화면에 필요한 골인·적중 점수를 서버에서 별도로 보관하고 승자·공동 우승·이탈 종료를 표시한다. YOUR TURN, 구매·사용 실패 사유, 스킬 사용 가능 여부를 먼저 넣는다. 이후 시간 경고·사용 불가 미리보기·코스트 부족 표시를 연결한다.

완료 기준: 처음 하는 사람이 누구 차례인지, 왜 구매·사용이 안 됐는지, 누가 왜 이겼는지를 로그 없이 알 수 있다.

### 4. 짧은 전환과 캐릭터 요구사항 적용

합의된 골인·낙사 대기와 입력 잠금, 카메라 전환·득점 연출을 연결한다. 4종·중복 금지를 유지한다면 새 캐릭터 리소스와 서버의 선택·준비 중복 검사를 함께 만든다. 큰 연출보다 판정과 전환의 일관성을 먼저 확인한다.

### 5. 실전 밸런스와 접속 범위 마감

2인과 4인에서 공격 성공률, 함정 명중률, 골인·적중 점수 비율, 평균 경기 길이를 기록한다. 인터넷 플레이가 이번 목표라면 공개 중계 서버 배포와 WSS 설정·서로 다른 인터넷 접속 검증을 별도 필수 작업으로 올린다. 설정·튜토리얼·추가 효과음·세부 이펙트는 이 결과에 맞춰 진행한다.

## 환경과 구조

- Unity `6000.3.24f1`, URP `17.3.0`. `QualitySettings.asset`이 `Assets/Settings/UniversalRP.asset`을 참조한다. GraphicsSettings의 전역 파이프라인 값만 보고 Built-in으로 판단하면 안 된다.
- 새 Input System `1.20.0`, Cinemachine `3.1.7`, Unity Test Framework `1.6.0`. `activeInputHandler: 1`.
- 네트워크는 Assets에 포함된 Mirror를 실제 게임 코드가 사용한다. 플랫폼 목표는 PC이며, 저장된 기록에는 Windows x64·macOS 개발 빌드가 있다. Steam 연동 완료는 확인하지 않았다.
- 시작: 빌드 첫 씬 `LobbyScene` → `CapstoneRoomManager.GameplayScene`인 `Stage1` → 기존 방 로비. GameScene·NetworkPlatform·SampleScene도 빌드 목록에 있으나 실제 기본 게임 진입 씬과 구분한다.
- `Assets/Script`: 현재 공수 경기·아이템·이동·화면·소리. `Assets/MyGame/Scripts`: 로비·접속·플레이어 네트워크 및 개발 probe. 두 경로 모두 현재 런타임에 관여한다.
- `Assets/SO`: 아이템·이동 설정. `Assets/MyGame/Prefabs`: 네트워크 플레이어·룸 플레이어. `Assets/Scenes/Stage1.unity`: 경기·함정·HUD·배경 연결.
- 별도 first-party asmdef는 해당 코드 루트에서 발견하지 못했다. 런타임은 기본 Assembly-CSharp, Editor 도구는 Assembly-CSharp-Editor에 속한다. Mirror는 별도 어셈블리들로 구성된다.
- MonoBehaviour/NetworkBehaviour 중심이다. 경기·점수·구매·적중은 서버, 이동은 소유자 클라이언트가 계산한다. SyncVar/SyncList, Command/TargetRpc/ClientRpc 및 아이템 전용 메시지를 사용한다. 아이템·이동 수치는 ScriptableObject, 나머지 일부 규칙은 Inspector·상수에 나뉜다.
- 기존 코드는 중괄호 다음 줄, private 직렬화 필드, 한국어 설명 주석과 역할별 클래스를 주로 사용한다. 명명은 기존 기능별 차이가 있으므로 일괄 변경을 권하지 않는다.
- 이번 세션에 Unity 전용 MCP 도구는 제공되지 않았다. manifest에서 Unity MCP 패키지를 발견하지 못했고, Editor 연결·Console·Play 상태는 확인하지 않았다.

## 검증 기록과 한계

`docs/NETWORK_AUDIT_2026-11-14.md`와 `docs/validation/network-audit-2026-11-14.json`에는 다음 결과가 저장돼 있다.

- 실제 KCP 별도 프로세스 통합 76개, 강퇴 17개, 경계 사례 20개, Node 중계 서버 8개: 합계 121개 통과.
- macOS 개발 빌드 성공(경고 347건), Windows x64 개발 빌드 성공(경고 512건), 수집된 Unity 런타임 오류 0건.
- 네트워크 실행은 Mac 한 대의 Host + 최대 3개 Client다. Windows 실행·Mac/Windows 다중 기기 통신·공개 인터넷·지연/손실 환경 검증은 이 결과에 포함되지 않는다.
- 시간·점수 종료 등 일부 조건은 개발용 서버 fixture로 검사했다. 실제 15분 플레이 통과와 동일하지 않다.
- 기록 이후 11월 16~18일의 스킬·음향·배경·그림 변경이 있다. 과거 성공을 현재 HEAD 전체의 회귀 통과로 표시하지 않는다.
- `Tools/Network/verify_lan.py`, `verify_lobby_kick.py`, `verify_network_edges.py`, `Assets/MyGame/Editor/RaceNetworkChecks.cs`, `Server/PasswordRelay/relay.test.mjs`가 검증 수단이다. Unity Test Framework 설치와 first-party EditMode/PlayMode 테스트 보유를 혼동하지 않는다. 해당 코드 루트에서 별도 테스트 어셈블리는 확인되지 않았다.
- `Assets/MyGame/README.md`, `RACE_NETWORK.md`, 10월 상태 문서의 일부 미구현·미검증 설명은 오래됐다. 현재 코드와 11월 14일 기록이 더 최신인 부분을 우선한다.

분석 시작 시부터 ProjectSettings 네 파일이 변경 상태였다. 해당 설정·코드·씬·프리팹은 수정하지 않았고, 이 문서만 추가했다.

## 주요 근거 파일

기획서 1~10장 및 5·6·7장 관련 하위 명세, `.claude/CLAUDE.md`, `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, `ProjectSettings/EditorBuildSettings.asset`, `QualitySettings.asset`, `Assets/Scenes/LobbyScene.unity`, `Stage1.unity`, `Assets/MyGame/Prefabs/NetworkPlatformPlayer.prefab`, `Assets/SO/*.asset`, `Assets/Script/RaceManager.cs`, `rolechangeManager.cs`, `Player/PlayerState.cs`, `Player/PlayerScore.cs`, `Player/PlayerCost.cs`, `Player/PlayerInventory.cs`, `Player/PlayerHeldItem.cs`, `Player/PlayerTimeSkill.cs`, `Player/PlayerIdleBoil.cs`, `SpearNetwork.cs`, `Bomb.cs`, `Missile.cs`, `RoundTrap.cs`, `DeathSequence.cs`, `RaceTimerUI.cs`, `PlayerInfoHUD.cs`, `BgmPlayer.cs`, `Assets/MyGame/Scripts/Network/CapstoneRoomManager.cs`, `PasswordRelayTransport.cs`, `Assets/MyGame/Scripts/Player/CapstoneRoomPlayer.cs`, `NetworkPlayerProfile.cs`, `Assets/MyGame/README.md`, `RACE_NETWORK.md`, `docs/MERGE_MAIN_2026-11-04.md`, `NETWORK_IMPLEMENTATION_STATUS.md`, `NETWORK_AUDIT_2026-11-14.md`, 해당 검증 JSON, `Server/PasswordRelay/README.md`.

# 서버 실습 적용

`Scenes/NetworkPlatform.unity`를 열고 Play → Host (Server + Client)를 누릅니다.
- 이동/점프: 기존 Check.inputactions 바인딩 사용
- P: 서버에서 점수 증가 → SyncVar로 이름표 갱신
- R: ClientRpc로 해당 플레이어의 빨간 효과를 모든 관찰 클라이언트에 표시
- T: TargetRpc로 요청자에게만 파란 효과 표시
- B: 서버에서 폭탄 생성 → 모든 클라이언트에 생성 → 3초 후 서버에서 제거

두 번째 실행 창에서 localhost와 Client를 선택하면 로컬 접속을 시험할 수 있습니다. 다른 컴퓨터는 호스트의 LAN 주소로 접속합니다(KCP UDP 7777).

기존 SampleScene과 이동 코드는 유지했습니다. NetworkPlatform 씬은 기존 지형/입력/스탯을 사용하고, 플레이어는 접속 시 네트워크 프리팹으로 생성합니다. 본인 플레이어만 이동/물리를 계산하고 위치는 ClientToServer NetworkTransform으로 전송합니다. 이 구조는 기존 실습과 같은 클라이언트 권한 이동이며 서버 이동 검증은 포함하지 않습니다.

`Scenes/NetworkPractice.unity`는 원래 실습 씬입니다. 이 씬은 WASD/방향키 이동, Space 점수 증가를 사용합니다. 두 씬 모두 새 Input System으로 동작합니다.

Tools → Network Practice → Validate Setup에서 연결 설정을 검사할 수 있습니다. 빌드 씬 목록의 첫 번째 씬으로 NetworkPlatform을 등록했습니다. 기존 SampleScene도 목록에 유지했습니다.

검증: 게임 코드 Unity 컴파일, 편집기 도구 별도 C# 컴파일, 씬/프리팹 GUID 연결 검사 통과. 실제 Host/Client 실행 검증은 자동 검증 환경의 IL 후처리 프로세스 연결 문제로 완료하지 못했습니다.

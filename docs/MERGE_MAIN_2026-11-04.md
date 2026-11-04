# 2026-11-04 최신 main 통합·점검

## 병합 기준

- 작업 브랜치: `codex/you-shall-not-pass-work`
- 기존 HEAD: `eea17bf`
- 가져온 main: `aa518dc`
- 작업 브랜치 원격에는 새 커밋이 없었고, main의 새 커밋 10개를 통합했다.
- 병합 전 작업 트리는 깨끗했다. 씬·프리팹은 main의 자동 병합 결과를 유지했으며 직접 YAML을 작성하지 않았다.

## 주요 변경

- 대포·회전 불·사라지는 바닥·쿵쿵이 함정과 최신 배치·이미지.
- 공격자/수비자 역할 분리와 공수 교대, 공격자 골인 1점 규칙.
- 함정 사용이 가능한 개발자 1인 시험 모드.

## 충돌 해결

`RaceManager`, `PlayerState`, `PlayerHeldItem`, `FallZone` 네 파일이 충돌했다.
최신 main의 공수 역할 규칙·교대 흐름을 채택했다. 기존 서버 검증은 이 규칙에 맞게 연결했다.

- 아이템 요청의 턴 번호 검사, 구매 쿨타임, Owner 전용 내용·공개 보유 개수 동기화를 유지한다.
- `CanUseItems` 대신 main의 `CanUseTraps`를 사용하여 일반 경기 공격자의 함정 요청을 서버에서도 차단한다.
- 낙사 요청은 턴 번호와 서버 콜라이더 위치를 검사한 후 공수 교대를 요청한다. 수비자의 낙사 요청은 거절한다.
- 최신 main의 공격자 퇴장 시 다음 공격자 선택을 유지한다. 예전의 모든 퇴장 즉시 경기 종료 정책을 덮어씌우지 않는다.
- 경기 전체 종료 시각·종료 사유·공동 승자 복제와 결과 전달 후 0.5초 로비 복귀를 유지한다. 과거의 골인 후 20초 마감은 새 공수 경기에서 사용하지 않는다.
- 기존 `RaceNetworkChecks`를 공수 경기 검사로 갱신했다. 개발 빌드 행동 probe는 구매 가능한 역할이 될 때까지 기다린다.

## 확인 결과

- Unity 번들 Roslyn으로 런타임 64개, Editor 3개, Player 설정 런타임 64개 소스 컴파일 성공.
- 활성 빌드 씬 4개와 연결된 씬·프리팹 12개, 스크립트 참조 172개를 정적으로 검사했다. 누락된 스크립트 GUID와 중복 GUID 없음.
- 충돌 마커·공백 오류 검사.
- 위 컴파일은 실제 Unity 가져오기, Mirror Weaver 후처리나 Play 실행을 대신하지 않는다.

## Hub 실행 재확인 및 라이선스 안내 정정

최초 CLI batch 검사에서는 `No valid Unity Editor license found`와 Editor exit 198이 발생했다. 이 결과로 사용자의 라이선스가 미활성화됐다고 판단한 것은 잘못이었다.

후속 확인 결과:

- Unity Hub의 Personal 라이선스가 활성 상태이며, Hub를 통해 연 Editor 로그도 `Product: Unity Personal`, `Type: Assigned`로 라이선스를 인식했다.
- 해당 Editor에서 최신 소스 컴파일과 Mirror Weaver 후처리가 수행됐다.
- Tools → Network Practice → Validate Merged Race Networking이 `MERGED_RACE_SETUP_VALIDATED`를 반환했다.
- 로비 Play와 SOLO TEST (DEV) 버튼을 통해 로컬 Host의 SampleScene 진입·플레이어 생성을 확인했다.
- 준비 연출 이후 타이머 진행, 코스트 증가, 컨베이어 상품 표시와 D 입력에 따른 이동을 확인했다.
- Leave game으로 로비 복귀를 확인하고 Play를 종료했다.

CLI 검사 로그에는 인증 토큰을 받지 못했다는 오류가 있었으며 Hub 실행과 인증 경로가 달랐다. CLI 실패를 Hub 라이선스 미활성화와 동일하게 해석하지 않는다. 자동 제어 도구의 연결 실패 원인은 별도 확인 대상이다.

## 남은 검증

별도 프로세스 Host/Client 접속, 수비자 구매·사용, 새 함정 적중, 사망·골인·퇴장 교대, 재입장을 포함한 전체 멀티플레이 회귀는 아직 수행하지 않았다. `RaceNetworkChecks.Run()`의 2인 이상 검사도 아직 실행하지 않았다. 1인 시험은 원격 네트워크 검증을 대체하지 않는다.

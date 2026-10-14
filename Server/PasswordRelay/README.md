# 비밀번호 방 중계 서버

현재 구현은 배포 전입니다. 공개 서버 URL이 없으므로 다른 Wi-Fi에서 접속 가능한 상태는 아닙니다.
Unity LobbyScene의 PasswordRelayTransport → Relay Url에 공개 WSS URL을 넣어야 합니다.

## 동작

Host가 비밀번호(8~64자)로 방을 만들고, 참가자는 같은 비밀번호만 입력합니다.
비밀번호가 방을 찾는 값이므로 동시에 같은 비밀번호의 방 두 개는 만들 수 없습니다.
공개 방 목록은 제공하지 않습니다. Host 포함 최대 4명입니다.
모든 게임 데이터는 중계를 거칩니다. 게임 규칙은 기존 Unity Host가 계속 판단합니다.
공유기 포트 개방은 필요하지 않습니다. Host가 나가거나 중계 서버가 재시작되면 방은 종료됩니다.

## 로컬 개발 테스트

Node.js 22 이상과 pnpm 설치 후 이 폴더에서:

```bash
pnpm install --frozen-lockfile
pnpm test
pnpm start
```

Unity Inspector에서 Relay Url을 `ws://127.0.0.1:8080/relay`로 설정합니다.
같은 컴퓨터에서 에디터와 최신 빌드를 실행하고 동일한 비밀번호로 Create room / Join room을 선택합니다.
이 주소는 로컬 개발 전용입니다. 다른 컴퓨터·외부 인터넷용으로 배포하면 반드시 WSS를 사용합니다.
테스트를 끝낸 뒤 배포 주소로 바꾸어 새로 빌드합니다.

## 공개 서버 배포

Docker Compose를 실행할 서버와 해당 서버를 가리키는 도메인이 필요합니다.

1. 서버에 이 폴더를 전달합니다.
2. `.env.example`을 `.env`로 복사하고 실제 도메인을 설정합니다.
3. 도메인 DNS를 서버 공인 IP에 연결하고 서버 인바운드 TCP 80·443을 허용합니다.
4. `docker compose up -d --build`를 실행합니다.
5. `https://도메인/health`가 `ok`를 반환하는지 확인합니다.
6. Unity Relay Url을 `wss://도메인/relay`로 설정합니다.
7. 새로 빌드한 Host·Client를 서로 다른 Wi-Fi 또는 모바일 핫스팟에서 시험합니다.

또는 WebSocket을 지원하는 컨테이너 호스팅에 Dockerfile로 배포하고 HTTPS URL을 사용할 수 있습니다.
단일 인스턴스로 운영합니다. 방 정보는 메모리에 있어서 여러 인스턴스·재시작 복구는 지원하지 않습니다.

## 제한 및 인증

- 외부 연결은 WSS만 허용하며 인증서를 무시하는 옵션은 없습니다.
- 비밀번호는 URL·로그에 기록하지 않고, 서버는 메모리의 방 인덱스로 HMAC 결과를 사용합니다.
- 비밀번호는 참가 권한이며 계정 로그인·사용자 신원 인증이 아닙니다. 다른 사람에게 공유하면 그 사람도 입장할 수 있습니다.
- 한 IP에서 분당 30회 인증 시도로 제한합니다. 역방향 프록시 뒤에서는 소켓 IP가 프록시 IP여서 이 제한이 공유될 수 있습니다.
- 최대 연결·방 개수, 메시지 크기·전송량·송신 버퍼 제한과 heartbeat를 사용합니다.
- 방은 게임 시작 후 잠깁니다. 자동 재접속·Host 승계는 구현하지 않았습니다.
- WebSocket/TCP의 신뢰성 전달만 사용합니다. 지연·패킷 손실이 있는 실제 게임 테스트가 필요합니다.

## 검증

Node 통합 테스트 6개: 데이터 전달/접속 ID 위조 방지, 비밀번호 거절, 정원/시작 후 입장, Host 종료/방 정리, 잘못된 메시지/heartbeat, 방 분리/kick.
Unity C# 컴파일 및 Play 진입, 비밀번호 입력 UI 표시 확인. 공개 서버 배포와 서로 다른 인터넷의 실제 플레이 테스트는 아직 수행하지 않았습니다.

## 문서

- [Mirror SimpleWeb](https://mirror-networking.gitbook.io/docs/manual/transports/websockets-transport)
- [ws](https://github.com/websockets/ws)
- [Caddy reverse proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy)

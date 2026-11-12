# 진행도 UI 개별 이미지

기존 전체 시안과 별도로 사용할 PNG 부품 4개. 모두 투명 배경이며 비픽셀 손그림 형태다.

- ProgressRoute_Line_v1.png: 검은 진행 경로 선.
- ProgressRoute_FlagOutline_v1.png: 검은 깃대와 깃발 테두리. 내부는 투명.
- ProgressRoute_FlagFill_v1.png: 깃발 내부 흰색 마스크. Image.color로 플레이어 색상 적용.
- ProgressRoute_DotTint_v1.png: 현재 위치와 목적지에 재사용할 흰색 점. 현재 위치는 플레이어 선택 색상, 목적지는 검은색 적용.

## 배치 시 주의

깃발 내부와 테두리는 동일한 Sprite Rect로 함께 잘라 정렬했다. 같은 부모 아래 동일한 RectTransform 크기와 위치로 겹치며 내부를 먼저, 테두리를 위에 그린다. 깃발 부모의 pivot은 깃대 아래쪽 (0.024, 0.007)이다. 두 파일을 서로 다른 내용 영역으로 다시 잘라내지 않는다.

YOU, Goal, 플레이어 이름은 이미지에 굽지 않고 별도 텍스트로 표시한다. 현재 위치 점과 YOU는 함께 움직인다. 목적지는 동일한 점 이미지를 검은색으로 작게 표시한다.

2026-11-11 실제 적용: Stage1의 RaceProgressUI에 연결했다. Sprite (2D and UI), Multiple의 단일 Sprite Rect, Full Rect, 알파 사용, 밉맵 끄기, Bilinear, 비압축으로 설정했다. 점과 선의 투명 여백은 Sprite Rect에서 잘라냈으며 원본 PNG는 변경하지 않았다. 모든 진행도 UI Graphic의 Raycast Target을 껐다. 인벤토리는 골드 아래로 이동했다.

## 생성 도구 / 프롬프트

내장 imagegen 도구 사용. 다음 조건으로 부품을 개별 생성했고 깃발 내부는 테두리를 참조하여 생성했다.

1. Line: One continuous black hand-drawn horizontal doodle line on transparent background; rounded ends, slight organic wobble; no flags, dots, labels, shadows or backdrop.
2. Flag outline: One right-pointing triangular pennant outline with vertical pole, black rounded doodle strokes; transparent cloth interior and exterior; no fill, ground, text, shadows.
3. Flag fill: Extract only the cloth interior from the outline reference as an opaque white mask; preserve canvas and placement; remove outline and pole; transparent exterior.
4. Dot: One flat filled disk on transparency, recolored to white using the generated disk as the reference; no hole, outlines, gradients or shadows; tintable at runtime.

확인: 호스트 + 가상 클라이언트 1개로 진행도 앞뒤 이동, 선택 색상, 사망 시 최고기록 확정/유지, 골인 및 제한시간 종료 시 초기화를 확인했다. 구매·인벤토리 선택/취소, 빈 슬롯 거절, 사망 연출 UI 숨김/복구도 확인했다. 새 UI는 입력을 받지 않는다. 경로는 Stage1의 ProgressRoute 자식 19개를 순서대로 연결하며 RaceProgress 인스펙터와 Gizmo로 확인할 수 있다.



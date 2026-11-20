# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

## Users

팀 안에서 데모나 보고에 쓸 비교 영상을 빠르게 만드는 사람. 같은 장면을 찍은 여러 영상(최대 4개)을
한 화면에 나란히 맞추고, 필요한 구간만 잘라 MP4 하나로 내보내 발표·보고에 붙인다.
영상 편집 전문가가 아니어도 바로 쓸 수 있어야 한다.

## Product Purpose

Windows 11에서 MP4 영상과 MP3·WAV 음원을 하나의 통합 타임라인에서 시간·ROI·배치를 맞춰
H.264/AAC MP4로 합성한다. 성공은 "설치나 학습 없이, 영상들을 정확히 맞춘 비교 영상을 몇 분 안에 내보내는 것"이다.

## Positioning

범용 편집기(Premiere, DaVinci, CapCut 등)와 달리:

- **설치 없는 portable:** zip을 풀고 `DiffVideo.exe`를 실행한다. 설치·관리자 권한·.NET 런타임이 필요 없다.
- **단순함:** 비교 영상에 필요한 기능만 있다. 배울 것이 적다.
- **정밀한 시간 정렬:** 트랙별 시작 시각, 출력 프레임 격자 정렬, 프레임 단위 이동, 교차/전체 프리셋으로 영상을 정확히 맞춘다.

## Operating Context

- Windows 11 x64 업무용 PC. 실행 파일은 코드 서명되지 않아 처음 실행 때 SmartScreen 창이 뜬다.
- 흐름: 미디어 끌어 놓기 → 활성 영상의 캔버스 배치·ROI 조정 → 타임라인에서 시작 시각과 내보내기 구간 설정 → MP4 내보내기.
- 내보내기는 편집과 독립적으로 백그라운드에서 진행되고 취소할 수 있다.
- 설정은 `%LOCALAPPDATA%\DiffVideo\settings.json`에 저장된다.

## Capabilities and Constraints

- WPF(.NET 10) 데스크톱 앱. 확정 사양과 수용 기준은 `docs/IMPLEMENTATION-PLAN.md`에 있다.
- 입력: MP4 영상 최대 4개, MP3·WAV 음원 최대 4개. 캔버스 최대 3840×2160, 24/25/30/50/60fps.
- 프리뷰는 시간·ROI·배치 확인용이며 최종 출력과 색감·화질이 다를 수 있다.
- 지원하지 않는 것(범위 밖): 프로젝트 저장, ROI 키프레임, 자동 피사체 추적, HDR 출력, MP3 반복.
- 용어는 `CONTEXT.md`를 따른다(전체 바, 구간 바, 전체 헤드, 구간 헤드, 내보내기 구간, 활성 영상, 캔버스 배치, ROI 등).
- 미정: 다크 모드는 요구 사항이지만 아직 구현되지 않았다.

## Brand Commitments

- 이름은 DiffVideo. UI 문구는 한국어.
- 아이콘: `src/DiffVideo.App/Assets/DiffVideo.ico`.
- 라이선스: 소스 MIT, FFmpeg는 LGPL(`THIRD-PARTY-NOTICES.md`).

## Evidence on Hand

- 사용 설명과 단축키: `README.md`. 버전별 UI 반영 기록: `docs/UI-DESIGN.md`.
- 사용자 후기, 사례, 벤치마크 자료는 없다. 만들어 넣지 않는다.

## Product Principles

1. **풀면 바로 실행된다.** 설치·권한·런타임·네트워크를 요구하는 기능은 넣지 않는다.
2. **비교에 필요한 것만.** 범용 편집 기능보다 맞추고 자르고 내보내는 흐름을 짧게 유지한다.
3. **시간과 좌표는 정확하게.** 시각은 프레임 격자에, ROI는 원본 픽셀 좌표에 맞춘다. 근사치를 보여 주지 않는다.
4. **키보드로 끝낼 수 있게.** 재생·프레임 이동·구간 설정·내보내기를 단축키로 할 수 있어야 한다.

## Accessibility & Inclusion

- 색 대비는 WCAG AA: 일반 글자 4.5:1, 큰 글자와 UI 요소(테두리, 아이콘, 포커스 표시) 3:1 이상.
- 다크 모드를 제공한다(영상을 어두운 환경에서 확인하는 경우).
- 키보드 중심 조작. 입력칸에 커서가 있을 때는 단축키가 동작하지 않는다. 포커스 위치가 늘 보여야 한다.

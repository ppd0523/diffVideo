# DiffVideo

Windows 11에서 MP4 영상(최대 4개)과 MP3 음원(최대 4개)을 하나의 타임라인에서
편집해 H.264/AAC MP4로 합성하는 portable WPF 애플리케이션입니다.
확정 사양과 수용 기준은 [구현 계획](docs/IMPLEMENTATION-PLAN.md)에 있습니다.

## 주요 기능

- 드래그앤드롭으로 영상·음원 추가, 격자 자동 배치, 타임라인 행 순서 = 레이어 순서
- 사용자 지정 캔버스(최대 3840×2160), 24/25/30/50/60fps
- 영상별 ROI 자르기(프리뷰 우클릭 드래그 또는 ROI 팝업), 위치·크기·Fit/Fill 조절
- 트랙별 시작 시각, 출력 구간 지정, 재생·프레임 단위 탐색
- 트랙별 볼륨(0~200%)과 오디오 포함 여부. 믹스는 볼륨 비율로 정규화되고 리미터가 항상 적용됩니다
- HDR 입력의 SDR BT.709 변환, AMD AMF 인코딩 후 Media Foundation 자동 대체
- 편집과 독립적으로 진행되는 취소 가능한 내보내기, 영상별 파일명 표시 옵션

프로젝트 저장, ROI 키프레임, 자동 피사체 추적, HDR 출력, MP3 반복은 지원하지 않습니다.

## 사용

1. portable ZIP을 풀고 `DiffVideo.exe`를 실행합니다. 설치·관리자 권한·.NET 런타임은 필요 없습니다.
2. MP4/MP3를 프리뷰나 타임라인에 끌어 놓거나 미디어 추가 아이콘으로 엽니다.
3. 트랙을 클릭해 활성화한 뒤 프리뷰에서 이동·크기 조절하고, 우클릭 드래그로 ROI를 지정합니다.
   `Esc`는 진행 중인 드래그를 취소합니다.
4. 타임라인에서 재생 시작점, 트랙 시작 시각, 내보내기 구간(종료 시각은 제외 경계)을 정합니다.
   `Space`는 재생/일시정지입니다.
5. **MP4 내보내기**로 설정을 확인하고 저장 위치를 고릅니다.

설정은 `%LOCALAPPDATA%\DiffVideo\settings.json`에 저장되며, 없거나 손상되면 기본값으로 시작합니다.
프리뷰의 색감·화질은 최종 출력과 다를 수 있습니다.

자세한 내용: [프리뷰 설계](docs/PREVIEW-ARCHITECTURE.md),
[리소스 비교](docs/RESOURCE-COMPARISON.md), [UI 디자인](docs/UI-DESIGN.md)

## 빌드

요구 사항: Windows 11 x64, .NET SDK 10.0.400 이상, PowerShell.

```powershell
.\build.bat                      # FFmpeg 준비 + 테스트 + portable 패키지
.\build.bat -SkipTests           # 테스트 생략
```

결과: `artifacts/DiffVideo-win-x64-portable.zip`.
버전별 배포는 `-PackageName DiffVideo-0.4.0-win-x64`로 만듭니다.

## 라이선스

소스는 [MIT](LICENSE)입니다. FFmpeg 조건은 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.

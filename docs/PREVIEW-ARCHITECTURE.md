# 0.3.0 개별 플레이어 프리뷰

## 정상 실행 경로

`MainViewModel → PlayerPreviewSession → SourceVideoPlayer 두 개 + BufferedTimelineAudio`

- Windows `MediaPlayer`가 원본을 재생하고, 각 플레이어의 `VideoDrawing`을 WPF
  화면 캔버스에서 잘라서 배치한다. 플레이어 자체 오디오는 항상 음소거한다.
- `PreviewLayout`이 ROI·Fit/Fill/Stretch의 좌표를 계산한다. 캔버스는 영상 파일이나
  합성 비트맵을 만들지 않고 표시 변환·클리핑·레이어 순서만 적용한다.
- Windows에서 열리지 않는 코덱과 회전 영상은 `SourceVideoDecoder`가 원본 하나를
  디코딩한다. 출력은 최대 960×540이며, 대기열은 4프레임으로 제한한다. 영상 합성,
  ROI 필터, 프록시 재인코딩은 없다. FFmpeg 실행 파일은 기존 portable 파일을 재사용한다.
- 정지·탐색 시에는 원본별 프레임을 직접 읽는다. 첫/마지막 프레임과 최근 요청을
  캐시하고 취소된 요청이 화면에 반영되지 않도록 한다. 타임라인 드래그는 임시
  오프셋으로만 탐색하며 원본 설정은 드롭할 때 한 번만 변경한다.
- 오디오는 기존 FFmpeg 오디오 전용 믹스 필터(볼륨, 오프셋, 페이드, 리미터)를 사용한다.
  PCM은 제한된 버퍼로 전달하고, 오디오 출력 콜백은 FFmpeg 파이프에서 직접 읽지 않는다.
  WinMM 장치가 재생한 샘플 수가 영상 두 개의 기준 시계다.
- 영상 시각 차이가 50ms를 넘거나 오디오 버퍼가 부족하면 함께 정지한 뒤 재동기화한다.
  마지막 화면 위에 로딩 표시를 띄우며 취소 토큰이 자동 재개보다 우선한다.
- 내보내기는 기존 불변 합성 스냅샷과 별도 FFmpeg 프로세스를 사용한다. 프리뷰 교체나
  이후 편집은 이미 시작한 내보내기에 영향을 주지 않는다.

## 유지한 동작 및 허용한 차이

시작 마커, 일시정지·정지·재개, 출력 프레임 격자, MP3 1ms 격자, 첫/마지막 프레임
유지, 고정 ROI, 내보내기 중 편집·재생·취소와 portable 배포를 유지한다.
프리뷰와 최종 출력 사이의 색감·HDR 밝기·세부 화질 차이는 허용한다.
Windows 플레이어가 보고하는 시각은 실제 모니터·스피커의 물리적 출력 지연 측정값은
아니다. 보고서의 `MaximumObservedSkewMs`도 플레이어 시각과 오디오 장치 시계의 차이다.

## 검증 및 리소스 관찰

`build/Test-PlayerPreview.ps1`은 PowerShell 7에서 실행한다.

1. 전 구간 움직임이 있는 80초 H.264 1080p30 영상 두 개와 MP3를 생성한다.
2. 실제 WPF 앱에서 재생 제어·탐색·드롭·로딩 취소·내보내기 병행을 검증한다.
3. 코덱 대체 경로를 강제로 선택해 원본 디코더의 진행을 확인한다.
4. 동일 입력·1920×1080 캔버스·FitMode.Fill·오디오 설정으로 0.2.0 방식과 새 방식을
   각각 한 번 실행한다. 3초 진행 후 약 60초간 CPU·메모리·GPU 카운터를 기록한다.

CPU는 논리 프로세서 전체 대비 비율이며 앱과 직접 하위 FFmpeg 프로세스를 합산한다.
메모리는 프로세스 Working Set/Private Bytes 합계다(공유 페이지가 중복 계산될 수 있다).
GPU는 해당 프로세스들에 귀속된 엔진 중 가장 바쁜 엔진의 사용률이며, 시스템 전체나
GPU 메모리 사용량을 뜻하지 않는다. 사용할 수 없는 카운터는 null로 남긴다.
성능 수치에는 통과 기준을 두지 않으며 수치를 맞추기 위한 개발 반복은 하지 않는다.

`--preview-diagnostics`는 명시적으로 실행하는 로컬 진단 모드다. 기존 합성 프리뷰
코드와 `TimelineAudioPreview`는 이 비교용으로만 유지하며 정상 앱에서는 호출하지 않는다.
측정 원시 결과와 기능 체크 결과는 `artifacts/preview-validation`에 저장한다.

## 구현 근거

- [Microsoft: VideoDrawing으로 MediaPlayer 표시](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-play-media-using-a-videodrawing)
- [Microsoft: Windows 코덱 지원과 선택적 HEVC 코덱](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/supported-codecs)
- [NAudio: WaveOut 장치 위치와 버퍼](https://naudio.github.io/NAudio/api/NAudio.Wave.WaveOut.html)
- [FFmpeg: 입력 탐색과 accurate seek](https://www.ffmpeg.org/ffmpeg.html)

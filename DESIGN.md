---
name: DiffVideo
description: 여러 영상을 한 시간축에 나란히 놓고 맞추는 라이트 테이블
colors:
  darkroom-stone: "#716A65"
  darkroom-stone-hover: "#57534E"
  darkroom-stone-pressed: "#44403C"
  segment-amber: "#A16207"
  export-blue: "#2563EB"
  export-blue-hover: "#1D4ED8"
  export-blue-pressed: "#1E40AF"
  ink: "#1C1917"
  secondary-text: "#57534E"
  tertiary-text: "#A8A29E"
  canvas: "#FAFAF9"
  surface: "#F5F5F4"
  surface-raised: "#EFEDEB"
  hairline: "#E7E5E4"
  border-medium: "#8A847F"
  border-strong: "#78716C"
  error: "#DC2626"
  letterbox-black: "#000000"
  scrim: "rgba(28, 25, 23, 0.8)"
  scrim-strong: "rgba(28, 25, 23, 0.9)"
  on-scrim: "#FAFAF9"
typography:
  display:
    fontFamily: "D2Coding"
    fontSize: "30px"
    fontWeight: 700
    lineHeight: "39px"
  title:
    fontFamily: "D2Coding"
    fontSize: "22px"
    fontWeight: 600
  body:
    fontFamily: "D2Coding"
    fontSize: "15px"
    fontWeight: 400
  readout:
    fontFamily: "D2Coding"
    fontSize: "15px"
    fontWeight: 600
  label:
    fontFamily: "D2Coding"
    fontSize: "13px"
    fontWeight: 600
  tooltip:
    fontFamily: "D2Coding"
    fontSize: "12px"
    fontWeight: 500
  caption:
    fontFamily: "D2Coding"
    fontSize: "11px"
    fontWeight: 400
rounded:
  none: "0px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
components:
  button-secondary:
    textColor: "{colors.darkroom-stone}"
    typography: "{typography.body}"
    rounded: "{rounded.none}"
    padding: "12px 24px"
    height: "48px"
  button-secondary-hover:
    backgroundColor: "{colors.surface}"
  button-secondary-pressed:
    backgroundColor: "{colors.surface-raised}"
  button-primary:
    backgroundColor: "{colors.darkroom-stone}"
    textColor: "{colors.canvas}"
    rounded: "{rounded.none}"
    padding: "12px 24px"
    height: "48px"
  button-primary-hover:
    backgroundColor: "{colors.darkroom-stone-hover}"
  button-primary-pressed:
    backgroundColor: "{colors.darkroom-stone-pressed}"
  button-compact:
    textColor: "{colors.darkroom-stone}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "2.4px 3.6px"
    height: "30px"
  button-transport-icon:
    textColor: "{colors.darkroom-stone}"
    rounded: "{rounded.none}"
    width: "42px"
    height: "30px"
  button-transport-icon-segment:
    textColor: "{colors.segment-amber}"
    rounded: "{rounded.none}"
    width: "42px"
    height: "30px"
  button-export:
    backgroundColor: "{colors.export-blue}"
    textColor: "{colors.canvas}"
    rounded: "{rounded.none}"
    width: "84px"
    height: "30px"
  button-export-hover:
    backgroundColor: "{colors.export-blue-hover}"
  button-export-pressed:
    backgroundColor: "{colors.export-blue-pressed}"
  input-text:
    backgroundColor: "{colors.canvas}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "2.4px 3.6px"
    height: "30px"
  tab-item:
    textColor: "{colors.secondary-text}"
    typography: "{typography.label}"
    padding: "7px 8px"
  tab-item-selected:
    textColor: "{colors.ink}"
  tooltip:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.canvas}"
    typography: "{typography.tooltip}"
    rounded: "{rounded.none}"
    padding: "4px 8px"
  timeline-clip:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    height: "30px"
  timeline-clip-selected:
    backgroundColor: "{colors.darkroom-stone}"
    textColor: "{colors.canvas}"
  export-boundary-handle:
    backgroundColor: "{colors.segment-amber}"
    textColor: "{colors.canvas}"
    rounded: "{rounded.none}"
    width: "14px"
    height: "22px"
---

# Design System: DiffVideo

## Overview

**Creative North Star: "The Light Table"**

필름을 나란히 올려놓고 들여다보는 라이트 테이블처럼, 화면의 주인공은 영상과 시간축이다.
작업대(UI)는 따뜻한 돌색 무채색으로 물러나 있고(Windows 앱 모드에 따라 밝은 작업대 또는 어두운 작업대), 색은 두 가지 표시에만 쓴다.
암실의 돌색(Darkroom Stone)은 전체 재생 쪽과 선택 상태를, 구간 호박색(Segment Amber)은 구간 재생과
내보내기 구간을 표시한다. 그 밖의 색은 영상 자체에서 나온다.

컴포넌트는 절제되고 정확하다. 그림자도 둥근 모서리도 없는 평면이고, 영역은 1px 선과
한 단계씩의 배경 톤(canvas → surface → surface-raised)으로만 나뉜다. 상태는 테두리 색과 배경 톤으로
알린다. 글꼴은 D2Coding 하나라서 시각 표시(`00:00.000`)의 숫자 폭이 늘 같다.

데스크톱 편집기의 밀도를 따른다. 타임라인과 바의 조작은 30px 높이로 촘촘하고, 큰 48px 버튼은
대화상자와 같은 넓은 영역에만 쓴다.

**Key Characteristics:**
- 무채색 돌색 작업대 위에 영상이 유일한 색면이다.
- 색 두 가지가 두 재생 범위를 나타낸다: 전체 = Darkroom Stone, 구간 = Segment Amber.
- 모든 모서리가 각지다(0px). 그림자가 없다.
- 고정폭 글꼴 하나(D2Coding)로 한국어, 라틴 문자, 숫자를 모두 쓴다.
- 조작부는 30px, 넓은 영역만 48px.

## Colors

Tailwind의 stone 계열 무채색 위에 의미가 정해진 색 세 개(돌색, yellow-700, blue-600)를 얹은 팔레트다. 아래 값은 라이트 테마 기준이고, 다크 테마 값은 [Dark Theme](#dark-theme)에 있다. 원본은 `src/DiffVideo.App/Themes/Colors.Light.xaml`과 `Colors.Dark.xaml`이다.

### Primary
- **Darkroom Stone** (`darkroom-stone`): 선택된 트랙과 타임라인 클립, 전체 재생 헤드, 포커스 링, 입력칸의 hover·포커스 테두리, 체크된 체크박스, 슬라이더 손잡이, 진행 막대, 기본 버튼 글자, 주 버튼 배경에 쓴다. hover와 pressed는 한 단계씩 어두워진다(`darkroom-stone-hover`, `darkroom-stone-pressed`).

### Secondary
- **Segment Amber** (`segment-amber`): 구간 재생 헤드, 내보내기 시작·끝 경계 손잡이와 선, 눈금자 위 내보내기 구간 띠(19% 불투명도, `#30CA8A04`), 구간 바의 아이콘 버튼과 구간 바 바탕(9% 불투명도)에 쓴다. 코드에서는 `WarningBrush`라는 이름이지만 경고가 아니라 구간을 뜻한다.

### Tertiary
- **Export Blue** (`export-blue`): 내보내기 버튼 하나에만 쓴다. 화면에서 유일하게 채도가 높은 차가운 색이며, 결과 파일을 만드는 동작을 표시한다. hover와 pressed는 한 단계씩 어두워진다.

### Neutral
- **Ink** (`ink`): 본문 글자, 툴팁 배경. 타임라인의 내보내기 구간 밖 음영(12.5%)에도 쓴다(다크 테마에서는 검정 40%).
- **Scrim** (`scrim`, `scrim-strong`, `on-scrim`): 영상 위에 뜨는 판(버퍼링 배지·동기화 중 덮개 80%, 트랙 이름 배지 90%)과 그 위 글자, 선택되지 않은 영상의 테두리, ROI 드래그 점선. 영상이 배경이라 테마와 상관없이 늘 같은 색이다.
- **Secondary Text** (`secondary-text`): 필드 라벨, 선택되지 않은 탭, 콤보박스 화살표.
- **Tertiary Text** (`tertiary-text`): 음원 파형을 채우는 색에만 쓴다. 글자에는 쓰지 않는다.
- **Canvas** (`canvas`): 창 배경, 입력칸, 타임라인 영역, 진한 배경 위 글자색.
- **Surface** (`surface`): 프리뷰 바깥 영역, 전체 바·구간 바 배경, 타임라인 클립, 버튼 hover.
- **Surface Raised** (`surface-raised`): 버튼 pressed.
- **Hairline** (`hairline`): 영역 경계선, 탭 아래 선, 진행 막대 바탕, 분할 손잡이.
- **Border Medium** (`border-medium`): 입력칸·콤보박스·체크박스·버튼 테두리, 슬라이더 홈.
- **Border Strong** (`border-strong`): 눈금자 눈금선. 원본 픽셀 그리드는 `tertiary-text` 47%로 그린다.
- **Error** (`error`): 상태 줄의 오류 문구.
- **Letterbox Black** (`letterbox-black`): 프리뷰 캔버스에서 영상이 덮지 않는 부분. 최종 출력의 검은 바탕과 같다.

### Named Rules
**The Two Heads Rule.** 전체 재생에 속한 것(전체 헤드, 전체 바 조작)은 Darkroom Stone, 구간 재생에 속한 것(구간 헤드, 구간 바 아이콘, 내보내기 경계)은 Segment Amber다. 두 색을 바꿔 쓰거나 같은 요소에 섞지 않는다.

**The One Blue Rule.** Export Blue는 내보내기 버튼에만 쓴다. 다른 강조가 필요하면 Darkroom Stone을 쓴다.

**The Footage Is the Color Rule.** UI는 stone 무채색으로 유지한다. 장식용 색면, 그라데이션, 색으로 칠한 배경을 넣지 않는다.

### Dark Theme

상태 줄 오른쪽 끝의 테마 버튼(16px 아이콘, 24px 버튼)으로 Windows 설정 따름 → 밝게 → 어둡게를 돌아가며 고른다. 아이콘은 지금 상태(자동·해·달)를, 툴팁은 상태와 다음 동작을 보여 주고, 고른 값은 `settings.json`에 저장된다. Windows 설정을 따를 때는 실행 중에 바꿔도 바로 반영된다. 제목 표시줄도 함께 어두워진다. stone 계열을 뒤집어서 `canvas`는 진한 배경, `darkroom-stone`은 밝은 돌색이 된다. 그래서 진한 색 위 글자로 쓰던 `canvas`는 다크 테마에서 밝은 돌색 위의 진한 글자가 된다. 역할은 두 테마가 같다.

| 토큰 | 라이트 | 다크 |
|---|---|---|
| `canvas` / `surface` / `surface-raised` | #FAFAF9 / #F5F5F4 / #EFEDEB | #1C1917 / #292524 / #38332F |
| `ink` / `secondary-text` / `tertiary-text` | #1C1917 / #57534E / #A8A29E | #F5F5F4 / #A8A29E / #78716C |
| `darkroom-stone` / hover / pressed | #716A65 / #57534E / #44403C | #A8A29E / #D6D3D1 / #E7E5E4 |
| `hairline` / `border-medium` / `border-strong` | #E7E5E4 / #8A847F / #78716C | #44403C / #8A847F / #A8A29E |
| `segment-amber` | #A16207 | #CA8A04 |
| `export-blue` / hover / pressed | #2563EB / #1D4ED8 / #1E40AF | #60A5FA / #93C5FD / #BFDBFE |
| `error` | #DC2626 | #F87171 |

### WCAG AA 대비 (PRODUCT.md 기준)

두 테마 모두 아래 기준을 채운다. `tests/DiffVideo.Core.Tests/ThemePaletteTests.cs`가 색 사전을 읽어 검사하므로, 색을 바꾸고 기준에 못 미치면 테스트가 실패한다.

| 조합 | 기준 | 라이트 | 다크 |
|---|---|---|---|
| `ink` 글자, `canvas`·`surface`·`surface-raised` 위 | 4.5:1 | 16.7 / 16.0 / 15.0 | 16.0 / 13.9 / 11.4 |
| `secondary-text` 글자, `canvas`·`surface` 위 | 4.5:1 | 7.3 / 7.0 | 6.9 / 6.0 |
| `darkroom-stone` 글자(기본 버튼 평소·hover·pressed 배경 위) | 4.5:1 | 5.1 / 4.9 / 4.6 | 6.9 / 6.0 / 4.9 |
| `darkroom-stone`(hover·pressed 포함) 위 `canvas` 글자 | 4.5:1 | 5.1 이상 | 6.9 이상 |
| `segment-amber` 위 `canvas` 글자(`[` `]` 손잡이) | 4.5:1 | 4.7 | 6.0 |
| `error` 글자 | 4.5:1 | 4.6 | 6.3 |
| `border-medium` 입력칸·체크박스 테두리 | 3:1 | 3.5 | 4.7 |
| `border-strong` 눈금선 | 3:1 | 4.6 | 6.9 |
| `segment-amber` 선·아이콘, `canvas`·`surface` 위 | 3:1 | 4.7 / 4.5 | 6.0 / 5.2 |
| `export-blue`(hover·pressed 포함) 위 아이콘 | 3:1 | 4.9 이상 | 6.9 이상 |

`tertiary-text`(라이트 2.4:1)는 파형과 픽셀 그리드에만 쓰고 글자에는 쓰지 않는다.

## Typography

**Display Font:** D2Coding (앱에 내장, `Assets/Fonts/D2Coding.ttf`, `D2CodingBold.ttf`)
**Body Font:** D2Coding
**Label/Mono Font:** D2Coding

**Character:** 한 가지 고정폭 서체가 제목부터 시각 표시까지 모두 맡는다. 측정 도구 같은 고른 숫자 폭과 한글·라틴 문자의 일관된 모양이 장점이다.

### Hierarchy
- **Display** (700, 30px, 행간 39px): 대화상자 제목(「내보내기 확인」, ROI 창).
- **Title** (600, 22px): 프리뷰 위 트랙 이름 배지. 드롭 안내(20px, 600)도 이 단계에 속한다.
- **Body** (400, 15px): 창의 기본 글자.
- **Readout** (600, 15px): 전체 헤드 시각, 전체 재생 길이 같은 시각 표시.
- **Label** (600, 13px): 필드 라벨, 탭, 작은 버튼, 바 제목(「전체」, 「구간」).
- **Tooltip** (500, 12px): 툴팁. 최대 폭 320px에서 줄바꿈한다.
- **Caption** (400, 11px): 상태 줄, 오류 문구, 눈금자 숫자.

### Named Rules
**The One Typeface Rule.** D2Coding 하나만 쓴다. 단계는 크기와 굵기로만 나눈다. 시각 표시 숫자가 재생 중 흔들리지 않는 것은 이 규칙 덕분이다.

## Layout

- **창:** 기본 1440×900, 최소 1120×720. 바깥 여백 8px.
- **위아래 구성:** 위는 작업 영역(프리뷰 + 8px 간격 + 설정 패널 216px), 가운데는 8px 높이 분할 손잡이, 아래는 통합 타임라인(최소 264px, 끌어서 높이 조절), 맨 아래는 상태 줄(안쪽 여백 12×5px, 오른쪽 200px 진행 막대 칸)이다.
- **통합 타임라인:** 안쪽 여백 8px. 40px 높이의 전체 바와 구간 바가 위에 있고, 그 아래 22px 눈금자와 미디어 트랙이 이어진다. 바는 4칸으로 나뉜다: 정보(왼쪽 2칸), 재생 컨트롤(가운데), 배율·내보내기(오른쪽).
- **간격 단위:** 4 / 8 / 12 / 16 / 24px. 버튼 묶음 안은 4px, 묶음 사이는 12px.
- **프리뷰:** 캔버스 해상도를 Viewbox로 균일하게 맞추고, 남는 곳은 레터박스로 둔다.

## Elevation & Depth

그림자를 쓰지 않는다. 깊이는 1px 선(`hairline`, `border-medium`)과 배경 톤 세 단계(`canvas` → `surface` → `surface-raised`)로 나타낸다. 영상 위에 떠야 하는 표시(트랙 이름 배지, 버퍼링 배지)는 그림자 대신 반투명 판(`scrim-strong` 90%, `scrim` 80%) 위에 `on-scrim` 글자를 얹는다.

### Named Rules
**The Flat Table Rule.** 모든 표면은 평평하다. 위계가 필요하면 그림자 대신 배경 톤을 한 단계 올리거나 선을 긋는다.

## Shapes

모서리는 모두 0px이다. 버튼, 입력칸, 콤보박스 목록, 체크박스, 슬라이더 손잡이, 타임라인 클립, 배지, 툴팁 모두 해당한다. 테두리는 1px이 기본이고, 포커스·선택·hover 상태에서만 2px이 된다. 재생 헤드는 2px 세로선에 6×16px 깃발이 달린 모양이다. 전체 헤드는 깃발이 위에, 구간 헤드는 아래에 있다. 내보내기 경계 손잡이는 14×22px 사각형 안에 `[`와 `]`를 넣는다.

### Named Rules
**The Square Corner Rule.** `CornerRadius`는 항상 0이다. 둥근 모서리는 이 작업대의 물건이 아니다.

## Components

### Buttons
절제된 평면 사각형. 테두리와 배경 톤으로만 상태가 바뀐다.
- **Shape:** 각진 모서리(0px), 1px 테두리(`border-medium`).
- **Secondary(기본):** 투명 배경, `darkroom-stone` 글자, 12×24px 여백, 높이 48px, 15px SemiBold.
- **Primary:** `darkroom-stone` 배경, `canvas` 글자.
- **Hover / Pressed:** 기본 버튼은 배경이 `surface` → `surface-raised`, 주 버튼은 `darkroom-stone-hover` → `darkroom-stone-pressed`로 바뀐다.
- **Focus:** 바깥 4px에 2px `darkroom-stone` 링을 그린다.
- **Disabled:** 불투명도 0.4.
- **Compact:** 높이 30px, 13px. 타임라인과 설정 패널에 쓴다.
- **Icon:** Material 아이콘(24px 뷰포트)을 쓴다. 테두리가 없고, 바의 재생 컨트롤은 42×30px이다. 툴팁 문구가 접근성 이름이 되고, 툴팁에는 단축키를 `(D) 전체 재생`처럼 앞에 붙인다. 툴팁은 300ms 뒤에 뜨고, 비활성일 때도 뜬다.

### Inputs / Fields
- **Style:** `canvas` 배경, 1px `border-medium` 테두리, 각진 모서리, 높이 30px, 여백 2.4×3.6px.
- **Focus:** 테두리가 2px `darkroom-stone`이 된다. hover 때는 색만 바뀐다.
- **Selection:** 선택 영역은 `darkroom-stone` 60%.
- **Disabled:** `surface` 배경, 불투명도 0.5.
- **ComboBox:** 같은 틀에 1.5px 화살표(`secondary-text`)를 단다. 목록에서 선택한 항목은 `darkroom-stone` 바탕에 `canvas` 글자다.

### Navigation (설정 패널 탭)
- 13px SemiBold `secondary-text` 글자, 여백 7×8px. hover 때 `surface` 배경이 깔리고, 선택되면 `ink` 글자와 아래쪽 2px `darkroom-stone` 표시줄이 붙는다. 탭 아래에 1px `hairline`을 긋는다.

### Checkboxes
- 18px 사각형, 1.5px `border-medium` 테두리. 체크되면 `darkroom-stone`으로 채우고 `canvas` 체크 표시를 넣는다. 키보드 포커스 때는 테두리가 2px이 된다.
- 아이콘 토글(오디오 포함, 비율 잠금)은 30×30px다. 상태에 따라 아이콘과 툴팁이 바뀌고, 켜진 비율 잠금은 `darkroom-stone`으로 채운다.

### Timeline Clip (Signature)
- 30px 높이 `surface` 막대에 1px `hairline` 테두리를 두르고 `ink` 글자를 쓴다. 활성 영상의 클립은 `darkroom-stone` 바탕에 `canvas` 글자로 바뀐다. 음원 클립 안의 파형은 `tertiary-text`로 칠한다.

### Playheads & Export Range (Signature)
- 전체 헤드는 `darkroom-stone`, 구간 헤드는 `segment-amber`이고, 둘 다 2px 선에 6×16px 깃발을 단다. 손잡이 잡기 폭은 12px이다.
- 내보내기 구간: 경계 손잡이(`segment-amber`, `[` `]`), 2px 경계선, 눈금자 위 호박색 띠를 쓴다. 구간 밖은 `ink` 12.5% 음영으로 덮는다.

### Preview Overlays (Signature)
- 선택된 영상은 2px `darkroom-stone` 테두리와 16px 배치 손잡이 8개(`canvas` 바탕, 2px `darkroom-stone` 테두리)로 표시한다. 선택되지 않은 영상은 1px `on-scrim` 테두리다.
- 버퍼링 배지는 `scrim` 판에 96px 진행 막대와 12px 「버퍼링」 글자를 넣는다. 진행 막대는 0.7초 주기로 깜빡인다.

### Tooltips
- `ink` 배경, `canvas` 글자, 12px Medium, 여백 4×8px, 최대 폭 320px. 넘치면 줄바꿈한다. 각진 모서리이고 그림자는 없다. 다크 테마에서는 밝은 판에 진한 글자가 된다.

### Scrollbars
- 화살표 없는 평면 막대. `surface` 홈 위에 3px 안쪽으로 `border-medium` 손잡이를 둔다. hover 때 `darkroom-stone`, 끄는 중에는 `darkroom-stone-hover`가 된다. 스크롤할 것이 없으면 손잡이를 숨긴다. 입력칸의 오른쪽 클릭 메뉴는 `surface` 바탕에 `ink` 글자다.

## Do's and Don'ts

### Do:
- **Do** 색은 토큰으로만 지정한다. XAML에서는 `{DynamicResource XBrush}`, C#에서는 `SetResourceReference`를 쓴다. 직접 그리는 컨트롤(`AxisRuler`, `CoordinateTip`, `SourcePixelGrid`)은 `OnRender`에서 `FindResource`로 읽는다(테마가 바뀌면 App이 다시 그리게 한다).
- **Do** 새 색 토큰은 `Colors.Light.xaml`과 `Colors.Dark.xaml`에 같은 키로 함께 넣는다. 영상 위에 뜨는 것만 테마와 무관한 `Scrim*` 토큰(`DesignSystem.xaml`)을 쓴다.
- **Do** 전체 재생 쪽 요소는 Darkroom Stone, 구간 재생 쪽 요소는 Segment Amber로 칠한다(The Two Heads Rule).
- **Do** 아이콘만 있는 버튼에는 단축키를 앞에 붙인 툴팁을 단다. 툴팁 문구가 접근성 이름이 된다.
- **Do** 글자색은 `canvas`와 `surface` 위 모두에서 4.5:1 이상, 선·아이콘·포커스 표시는 3:1 이상인지 위 대비 표로 확인한다.
- **Do** 조작부는 30px, 넓은 영역의 버튼만 48px로 맞춘다.

### Don't:
- **Don't** XAML이나 C#에 hex 색을 직접 쓰지 않는다. 예외는 영상 자체와 같은 검정(프리뷰 레터박스)과 내보내기 출력에 그려지는 파일명 라벨뿐이다.
- **Don't** 브러시를 `StaticResource`나 `(Brush)FindResource(...)` 대입으로 쓰지 않는다. 테마를 바꿔도 따라오지 않는다.
- **Don't** `tertiary-text`(2.4:1)를 글자에 쓰지 않는다.
- **Don't** Export Blue를 내보내기 버튼 밖에서 쓰지 않는다.
- **Don't** 둥근 모서리, 그림자, 그라데이션을 넣지 않는다.
- **Don't** D2Coding 외의 글꼴을 들이지 않는다.

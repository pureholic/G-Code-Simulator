# G-Code Simulator

WPF 기반의 G-Code 시각화 및 시뮬레이션 프로그램입니다. CNC 머신이나 3D 프린터의 가공 경로(G-Code)를 2D 및 3D 환경에서 미리 확인하고, 실제 가공 과정을 시뮬레이션할 수 있는 도구입니다.

## 🚀 주요 기능 (Features)

*   **G-Code 파싱 및 시각화**
    *   표준 G-Code 및 NC 파일을 로드하여 경로를 시각화합니다.
    *   이동 유형에 따른 색상 구분 (급속 이송: 파랑, 직선 절삭: 빨강, 원호 절삭: 초록)
    *   3D 뷰(HelixToolkit)와 2D 평면 뷰 지원

*   **실시간 시뮬레이션 (Simulation)**
    *   재생, 일시정지, 초기화 기능 지원
    *   시뮬레이션 속도 조절 (1x ~ 10x)
    *   진행률바(Slider)를 통한 특정 구간 탐색

*   **상세 통계 정보 (Statistics)**
    *   총 가공 시간 및 실시간 경과/남은 시간 계산
    *   전체 이동 거리 및 타입별(Rapid, Linear, Arc) 이동 거리 분석
    *   현재 가공 중인 라인 및 좌표 추적

*   **CAD 파일 변환**
    *   DXF/DWG 파일을 불러와 G-Code로 변환하는 기능 지원

*   **내장 편집기**
    *   구문 강조(Syntax Highlighting) 기능이 포함된 G-Code 편집기 제공
    *   코드 수정 후 즉시 시뮬레이션 반영

## 🛠 기술 스택 (Tech Stack)

*   **Platform**: .NET 6.0 (Windows Presentation Foundation)
*   **Architecture**: MVVM (Model-View-ViewModel) 패턴
*   **Libraries**:
    *   [Helix Toolkit](https://github.com/helix-toolkit/helix-toolkit): 고성능 3D 가속 렌더링
    *   [AvalonEdit](https://github.com/icsharpcode/AvalonEdit): 코드 편집 및 구문 강조
    *   [IxMilia.Dxf](https://github.com/IxMilia/IxMilia.Dxf): DXF/DWG 파일 파싱

## 📦 설치 및 실행 방법 (Installation)

**요구 사항**
*   Windows 10 이상
*   .NET 6.0 Runtime 또는 SDK

**빌드 방법**
1. 저장소를 클론합니다.
   ```bash
   git clone https://github.com/username/GCodeSimulator.git
   ```
2. Visual Studio 2022를 실행하고 `GCodeSimulator.sln`을 엽니다.
3. 솔루션 탐색기에서 우클릭하여 **패키지 복원(Restore NuGet Packages)**을 수행합니다.
4. **시작(Start)** 버튼을 누르거나 `F5`를 눌러 빌드 및 실행합니다.

## 🎮 조작 방법 (Controls)

**3D 뷰포트**
*   **회전**: 마우스 우클릭 드래그
*   **이동 (Pan)**: 마우스 휠 클릭 드래그
*   **줌 (Zoom)**: 마우스 휠 스크롤

**파일 관리**
*   **파일 열기**: 기존 G-Code 파일(.gcode, .nc, .txt) 로드
*   **DWG 열기**: CAD 도면 파일을 로드하여 G-Code로 변환
*   **Save/Save As**: 편집된 G-Code 저장

## 📄 라이선스 (License)

이 프로젝트는 MIT License를 따릅니다. 자세한 내용은 `LICENSE` 파일을 참조하세요.

using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using GCodeSimulator.Helpers;
using GCodeSimulator.Models;
using Microsoft.Win32;
using System.Windows.Media.Media3D;

namespace GCodeSimulator.ViewModels
{
    /// <summary>
    /// 뷰 모드 열거형 (자동, 2D, 3D)
    /// </summary>
    public enum ViewMode
    {
        Auto,   // 자동 선택 (Z축 이동에 따라 2D/3D 자동 전환)
        View2D, // 강제 2D 뷰 (Z축 무시하고 XY 평면만 표시)
        View3D  // 강제 3D 뷰
    }

    /// <summary>
    /// 메인 윈도우의 ViewModel 클래스
    /// G-code 파일 관리, 파싱, 시뮬레이션 제어를 담당
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly GCodeParser _parser = new GCodeParser(); // G-code 파서 인스턴스
        private System.Threading.Timer? _simulationTimer; // 시뮬레이션 타이머

        private string _gcodeText = ""; // G-code 텍스트
        private string _currentFilePath = ""; // 현재 열린 파일 경로
        private string _windowTitle = "G-Code 시뮬레이터"; // 윈도우 제목
        private bool _hasUnsavedChanges = false; // 저장되지 않은 변경사항 여부
        private int _currentSegmentIndex = 0; // 현재 재생 중인 세그먼트 인덱스
        private double _currentSegmentProgress = 0; // 현재 세그먼트 내 진행률 (0~1)
        private int _maxSteps = 0; // 전체 세그먼트 수
        private bool _isPlaying = false; // 재생 중 여부
        private double _playbackSpeed = 1.0; // 재생 속도 배율
        private string _statusText = "파일을 열어주세요"; // 상태 텍스트
        private double _currentFeedRate = 0; // 현재 피더 속도
        private int _currentLineNumber = 0; // 현재 실행 중인 G-code 라인 번호
        private string _currentGCodeLine = ""; // 현재 실행 중인 G-code 라인
        private DateTime _lastTickTime = DateTime.Now; // 마지막 틱 시간
        private double _segmentElapsedSeconds = 0; // 세그먼트 경과 시간
        private bool _isInitialPlay = true; // 최초 재생 여부

        // 3D 그리드 속성
        private bool _isGridVisible = true; // 그리드 표시 여부
        private double _minorGridDistance = 1.0; // 보조 그리드 간격

        // 뷰 모드 속성
        private ViewMode _viewMode = ViewMode.Auto; // 뷰 모드 (자동, 2D, 3D)

        // 시뮬레이션 업데이트 감지
        private bool _isSimulationUpdating = false; // 시뮬레이션 타이머가 값을 업데이트 중인지 여부

        // 통계 정보 필드
        private double _totalMachiningTime = 0;        // 총 가공 시간 (초)
        private double _elapsedMachiningTime = 0;      // 경과 시간 (초)
        private double _totalDistance = 0;              // 총 이동 거리 (mm)
        private double _rapidDistance = 0;              // 급속 이송 거리 (mm)
        private double _cuttingDistance = 0;            // 절삭 거리 (mm)
        private double _arcDistance = 0;                // 원호 거리 (mm)

        /// <summary>
        /// 뷰 리셋을 요청하는 이벤트 (카메라 리셋용)
        /// </summary>
        public event Action? RequestViewReset;

        #region 3D Grid Properties
        
        private double _modelWidth;
        private double _modelLength;
        private System.Windows.Media.Media3D.Point3D _modelCenter;

        public double ModelWidth
        {
            get => _modelWidth;
            private set => SetProperty(ref _modelWidth, value);
        }

        public double ModelLength
        {
            get => _modelLength;
            private set => SetProperty(ref _modelLength, value);
        }

        public System.Windows.Media.Media3D.Point3D ModelCenter
        {
            get => _modelCenter;
            private set => SetProperty(ref _modelCenter, value);
        }

        #endregion
        
        #region Grid Control Properties

        public bool IsGridVisible
        {
            get => _isGridVisible;
            set => SetProperty(ref _isGridVisible, value);
        }

        public double MinorGridDistance
        {
            get => _minorGridDistance;
            set
            {
                if (SetProperty(ref _minorGridDistance, value))
                {
                    OnPropertyChanged(nameof(MajorGridDistance));
                }
            }
        }

        public double MajorGridDistance => MinorGridDistance * 10;

        #endregion

        #region View Mode Properties

        /// <summary>
        /// 현재 뷰 모드 (자동, 2D, 3D)
        /// </summary>
        public ViewMode CurrentViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    UpdateVisualization();
                }
            }
        }

        /// <summary>
        /// 자동 모드 선택 여부
        /// </summary>
        public bool IsAutoMode
        {
            get => CurrentViewMode == ViewMode.Auto;
            set { if (value) CurrentViewMode = ViewMode.Auto; }
        }

        /// <summary>
        /// 2D 모드 선택 여부
        /// </summary>
        public bool Is2DMode
        {
            get => CurrentViewMode == ViewMode.View2D;
            set { if (value) CurrentViewMode = ViewMode.View2D; }
        }

        /// <summary>
        /// 3D 모드 선택 여부
        /// </summary>
        public bool Is3DMode
        {
            get => CurrentViewMode == ViewMode.View3D;
            set { if (value) CurrentViewMode = ViewMode.View3D; }
        }

        #endregion

        /// <summary>
        /// MainViewModel 생성자
        /// 모든 명령(Command)을 초기화
        /// </summary>
        public MainViewModel()
        {
            LoadFileCommand = new RelayCommand(_ => LoadFile());
            LoadDwgFileCommand = new RelayCommand(_ => LoadDwgFile());
            SaveFileCommand = new RelayCommand(_ => SaveFile(), _ => !string.IsNullOrEmpty(CurrentFilePath));
            SaveAsCommand = new RelayCommand(_ => SaveFileAs());
            PlayPauseCommand = new RelayCommand(_ => PlayPause());
            ResetCommand = new RelayCommand(_ => Reset());
        }

        #region Properties

        public string GCodeText
        {
            get => _gcodeText;
            set
            {
                if (SetProperty(ref _gcodeText, value))
                {
                    HasUnsavedChanges = true;
                }
            }
        }

        public string CurrentFilePath
        {
            get => _currentFilePath;
            set
            {
                if (SetProperty(ref _currentFilePath, value))
                {
                    UpdateWindowTitle();
                }
            }
        }

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                if (SetProperty(ref _hasUnsavedChanges, value))
                {
                    UpdateWindowTitle();
                }
            }
        }

        public int CurrentSegmentIndex
        {
            get => _currentSegmentIndex;
            set
            {
                if (SetProperty(ref _currentSegmentIndex, value))
                {
                    OnPropertyChanged(nameof(CurrentStepDisplay));
                    UpdateCurrentFeedRate();
                    UpdateElapsedTime();

                    // 시뮬레이션 타이머가 아닌 사용자가 직접 슬라이더를 조작한 경우 재생 일시정지
                    if (!_isSimulationUpdating && IsPlaying)
                    {
                        IsPlaying = false;
                    }
                }
            }
        }

        public double CurrentSegmentProgress
        {
            get => _currentSegmentProgress;
            set
            {
                if (SetProperty(ref _currentSegmentProgress, value))
                {
                    UpdateVisualization();
                    UpdateElapsedTime();
                }
            }
        }

        public int MaxSteps
        {
            get => _maxSteps;
            set => SetProperty(ref _maxSteps, value);
        }

        public string CurrentStepDisplay => $"{CurrentSegmentIndex} / {MaxSteps}";

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    OnPropertyChanged(nameof(PlayPauseButtonText));
                }
            }
        }

        public string PlayPauseButtonText => IsPlaying ? "일시정지" : "재생";

        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set => SetProperty(ref _playbackSpeed, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public double CurrentFeedRate
        {
            get => _currentFeedRate;
            set => SetProperty(ref _currentFeedRate, value);
        }
        public string CurrentFeedRateDisplay => CurrentFeedRate > 0 ? $"{CurrentFeedRate:F0} mm/min" : "N/A";

        public int CurrentLineNumber
        {
            get => _currentLineNumber;
            set => SetProperty(ref _currentLineNumber, value);
        }

        public string CurrentGCodeLine
        {
            get => _currentGCodeLine;
            set => SetProperty(ref _currentGCodeLine, value);
        }

        public string CurrentLineDisplay => CurrentLineNumber > 0 ? $"라인 {CurrentLineNumber}" : "대기 중";

        public GCodeParser Parser => _parser;

        #endregion

        #region Statistics Properties

        /// <summary>
        /// 총 가공 시간 (초)
        /// </summary>
        public double TotalMachiningTime
        {
            get => _totalMachiningTime;
            private set => SetProperty(ref _totalMachiningTime, value);
        }

        /// <summary>
        /// 경과 시간 (초)
        /// </summary>
        public double ElapsedMachiningTime
        {
            get => _elapsedMachiningTime;
            private set
            {
                if (SetProperty(ref _elapsedMachiningTime, value))
                {
                    OnPropertyChanged(nameof(ElapsedMachiningTimeDisplay));
                    OnPropertyChanged(nameof(RemainingMachiningTime));
                    OnPropertyChanged(nameof(RemainingMachiningTimeDisplay));
                    OnPropertyChanged(nameof(MachiningProgress));
                    OnPropertyChanged(nameof(MachiningProgressDisplay));
                }
            }
        }

        /// <summary>
        /// 남은 시간 (초)
        /// </summary>
        public double RemainingMachiningTime => Math.Max(0, TotalMachiningTime - ElapsedMachiningTime);

        /// <summary>
        /// 진행률 (0~100%)
        /// </summary>
        public double MachiningProgress => TotalMachiningTime > 0
            ? Math.Min(100, (ElapsedMachiningTime / TotalMachiningTime) * 100)
            : 0;

        /// <summary>
        /// 총 이동 거리 (mm)
        /// </summary>
        public double TotalDistance
        {
            get => _totalDistance;
            private set => SetProperty(ref _totalDistance, value);
        }

        /// <summary>
        /// 급속 이송 거리 (mm)
        /// </summary>
        public double RapidDistance
        {
            get => _rapidDistance;
            private set => SetProperty(ref _rapidDistance, value);
        }

        /// <summary>
        /// 절삭 거리 (mm)
        /// </summary>
        public double CuttingDistance
        {
            get => _cuttingDistance;
            private set => SetProperty(ref _cuttingDistance, value);
        }

        /// <summary>
        /// 원호 거리 (mm)
        /// </summary>
        public double ArcDistance
        {
            get => _arcDistance;
            private set => SetProperty(ref _arcDistance, value);
        }

        // Display 속성 (포맷팅된 문자열)
        public string TotalMachiningTimeDisplay => FormatTime(TotalMachiningTime);
        public string ElapsedMachiningTimeDisplay => FormatTime(ElapsedMachiningTime);
        public string RemainingMachiningTimeDisplay => FormatTime(RemainingMachiningTime);
        public string MachiningProgressDisplay => $"{MachiningProgress:F1}%";
        public string TotalDistanceDisplay => $"{TotalDistance:F2} mm";
        public string RapidDistanceDisplay => $"{RapidDistance:F2} mm";
        public string CuttingDistanceDisplay => $"{CuttingDistance:F2} mm";
        public string ArcDistanceDisplay => $"{ArcDistance:F2} mm";

        #endregion

        #region Commands

        public ICommand LoadFileCommand { get; }
        public ICommand LoadDwgFileCommand { get; }
        public ICommand SaveFileCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand ResetCommand { get; }

        #endregion

        /// <summary>
        /// G-code 파일 열기 대화상자를 표시하고 파일 로드
        /// </summary>
        private void LoadFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "G-Code Files (*.gcode;*.nc;*.txt)|*.gcode;*.nc;*.txt|All Files (*.*)|*.*",
                Title = "G-Code 파일 열기"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    GCodeText = File.ReadAllText(dialog.FileName);
                    CurrentFilePath = dialog.FileName;
                    HasUnsavedChanges = false;
                    ParseGCode();
                }
                catch (Exception ex)
                {
                    StatusText = $"파일 로드 실패: {ex.Message}";
                }
            }
        }

        /// <summary>
        /// DWG/DXF 파일 열기 대화상자를 표시하고 G-code로 변환
        /// </summary>
        private void LoadDwgFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CAD Files (*.dwg;*.dxf)|*.dwg;*.dxf|All Files (*.*)|*.*",
                Title = "DWG/DXF 파일 열기"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    StatusText = "DWG/DXF 파일 읽는 중...";
                    var entities = DwgReader.ReadEntities(dialog.FileName);
                    StatusText = $"{entities.Count}개의 도형을 찾았습니다. G-code로 변환 중...";

                    GCodeText = GeometryToGCodeConverter.ConvertToGCode(entities);
                    CurrentFilePath = "";
                    HasUnsavedChanges = true;
                    StatusText = $"변환 완료. G-code를 확인하고 저장하세요.";
                    ParseGCode();
                }
                catch (Exception ex)
                {
                    StatusText = $"DWG/DXF 파일 처리 실패: {ex.Message}";
                }
            }
        }

        /// <summary>
        /// 현재 파일에 G-code 저장
        /// 파일 경로가 없으면 다른 이름으로 저장 호출
        /// </summary>
        private void SaveFile()
        {
            if (string.IsNullOrEmpty(CurrentFilePath))
            {
                SaveFileAs();
                return;
            }

            try
            {
                File.WriteAllText(CurrentFilePath, GCodeText);
                HasUnsavedChanges = false;
                StatusText = "파일이 저장되었습니다";
            }
            catch (Exception ex)
            {
                StatusText = $"파일 저장 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 다른 이름으로 저장 대화상자를 표시하고 G-code 저장
        /// </summary>
        private void SaveFileAs()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "G-Code Files (*.gcode)|*.gcode|All Files (*.*)|*.*",
                Title = "다른 이름으로 저장"
            };

            if (dialog.ShowDialog() == true)
            {
                CurrentFilePath = dialog.FileName;
                SaveFile();
            }
        }

        /// <summary>
        /// G-code 텍스트를 파싱하여 경로 데이터 생성
        /// 좌표 범위 계산 및 3D 모델 중심점 설정
        /// </summary>
        public void ParseGCode()
        {
            try
            {
                _parser.Parse(GCodeText);
                MaxSteps = _parser.GetTotalSegments();
                CurrentSegmentIndex = 0;
                CurrentSegmentProgress = 0;
                _isInitialPlay = true;

                StatusText = $"파싱 완료: {MaxSteps}개 경로, " +
                             $"X: [{_parser.MinX:F2}, {_parser.MaxX:F2}], " +
                             $"Y: [{_parser.MinY:F2}, {_parser.MaxY:F2}], " +
                             $"Z: [{_parser.MinZ:F2}, {_parser.MaxZ:F2}]";

                if (_parser.MaxX > _parser.MinX || _parser.MaxY > _parser.MinY)
                {
                    ModelWidth = Math.Abs(_parser.MaxX - _parser.MinX);
                    ModelLength = Math.Abs(_parser.MaxY - _parser.MinY);
                    ModelCenter = new System.Windows.Media.Media3D.Point3D(
                        _parser.MinX + ModelWidth / 2,
                        _parser.MinY + ModelLength / 2,
                        0);
                }
                else
                {
                    ModelWidth = 100;
                    ModelLength = 100;
                    ModelCenter = new System.Windows.Media.Media3D.Point3D(0, 0, 0);
                }

                CalculateStatistics();
                UpdateVisualization();
            }
            catch (Exception ex)
            {
                StatusText = $"파싱 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 시뮬레이션 재생/일시정지 토글
        /// </summary>
        private void PlayPause()
        {
            if (MaxSteps == 0 && !string.IsNullOrEmpty(GCodeText))
            {
                ParseGCode();
                if (MaxSteps == 0) return;
            }

            IsPlaying = !IsPlaying;

            if (IsPlaying)
            {
                if (_isInitialPlay)
                {
                    RequestViewReset?.Invoke();
                    _isInitialPlay = false;
                }

                if (CurrentSegmentIndex >= MaxSteps)
                {
                    Reset();
                    IsPlaying = true; // Start playing from beginning
                }

                _lastTickTime = DateTime.Now;

                _simulationTimer = new System.Threading.Timer(
                    SimulationTimer_Tick,
                    null,
                    0,
                    16
                );
            }
            else
            {
                _simulationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        /// <summary>
        /// 시뮬레이션을 처음으로 리셋
        /// </summary>
        private void Reset()
        {
            IsPlaying = false;
            CurrentSegmentIndex = 0;
            CurrentSegmentProgress = 0;
            _segmentElapsedSeconds = 0;
            ElapsedMachiningTime = 0;
            UpdateVisualization();
        }

        /// <summary>
        /// 시뮬레이션 타이머 틱 이벤트 핸들러
        /// 실시간으로 경로 진행률을 업데이트 (16ms 간격, 약 60 FPS)
        /// </summary>
        /// <param name="state">타이머 상태</param>
        private void SimulationTimer_Tick(object? state)
        {
            if (!IsPlaying || CurrentSegmentIndex >= MaxSteps)
            {
                Application.Current.Dispatcher.BeginInvoke(() => { 
                    IsPlaying = false; 
                    _simulationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                });
                return;
            }

            var now = DateTime.Now;
            var deltaTime = (now - _lastTickTime).TotalSeconds;
            _lastTickTime = now;
            
            var effectiveDeltaTime = deltaTime * PlaybackSpeed;
            
            if (CurrentSegmentIndex < _parser.Paths.Count)
            {
                var segment = _parser.Paths[CurrentSegmentIndex];
                
                _segmentElapsedSeconds += effectiveDeltaTime;

                double newProgress = segment.DurationSeconds > 0 ?
                    Math.Min(1.0, _segmentElapsedSeconds / segment.DurationSeconds) : 1.0;
                
                Application.Current.Dispatcher.BeginInvoke(() => {
                    CurrentSegmentProgress = newProgress;
                });

                if (newProgress >= 1.0)
                {
                    var nextIndex = CurrentSegmentIndex + 1;
                    _segmentElapsedSeconds = 0;

                    Application.Current.Dispatcher.BeginInvoke(() => {
                        // 시뮬레이션 타이머가 업데이트하는 것임을 표시
                        _isSimulationUpdating = true;
                        CurrentSegmentIndex = nextIndex;
                        _isSimulationUpdating = false;

                        CurrentSegmentProgress = 0;

                        if (nextIndex >= MaxSteps)
                        {
                            IsPlaying = false;
                            CurrentSegmentProgress = 1.0;
                        }
                    });
                }
            }
        }

        /// <summary>
        /// 윈도우 제목 업데이트
        /// 파일명과 저장되지 않은 변경사항 표시(*) 포함
        /// </summary>
        private void UpdateWindowTitle()
        {
            var fileName = string.IsNullOrEmpty(CurrentFilePath) ? "제목 없음" : Path.GetFileName(CurrentFilePath);
            WindowTitle = $"G-Code 시뮬레이터 - {fileName}{(HasUnsavedChanges ? "*" : "")}";
        }

        /// <summary>
        /// 시각화 업데이트 트리거
        /// Parser 속성 변경 알림을 통해 뷰에 업데이트 요청
        /// </summary>
        private void UpdateVisualization()
        {
            OnPropertyChanged(nameof(Parser));
        }

        /// <summary>
        /// 현재 세그먼트의 정보 업데이트 (피더 속도, 라인 번호, G-code 라인)
        /// </summary>
        private void UpdateCurrentFeedRate()
        {
            if (CurrentSegmentIndex >= 0 && CurrentSegmentIndex < _parser.Paths.Count)
            {
                var segment = _parser.Paths[CurrentSegmentIndex];
                CurrentFeedRate = segment.FeedRate;
                CurrentLineNumber = segment.LineNumber;
                CurrentGCodeLine = segment.OriginalGCodeLine;
            }
            else
            {
                CurrentFeedRate = 0;
                CurrentLineNumber = 0;
                CurrentGCodeLine = "";
            }
            OnPropertyChanged(nameof(CurrentFeedRateDisplay));
            OnPropertyChanged(nameof(CurrentLineDisplay));
        }

        /// <summary>
        /// 시간을 읽기 좋은 형식으로 포맷 (HH:MM:SS 또는 MM:SS)
        /// </summary>
        /// <param name="seconds">초 단위 시간</param>
        /// <returns>포맷된 시간 문자열</returns>
        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;

            int totalSeconds = (int)Math.Ceiling(seconds);
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;

            if (hours > 0)
                return $"{hours:D2}:{minutes:D2}:{secs:D2}";
            else
                return $"{minutes:D2}:{secs:D2}";
        }

        /// <summary>
        /// 통계 데이터 계산 (총 시간, 거리 등)
        /// ParseGCode() 실행 후 호출
        /// </summary>
        private void CalculateStatistics()
        {
            TotalMachiningTime = 0;
            TotalDistance = 0;
            RapidDistance = 0;
            CuttingDistance = 0;
            ArcDistance = 0;

            foreach (var segment in _parser.Paths)
            {
                // 시간 누적
                TotalMachiningTime += segment.DurationSeconds;

                // 거리 누적
                TotalDistance += segment.Distance;

                // 타입별 거리 분류
                switch (segment.PathType)
                {
                    case "rapid":
                        RapidDistance += segment.Distance;
                        break;
                    case "linear":
                        CuttingDistance += segment.Distance;
                        break;
                    case "arc":
                        ArcDistance += segment.Distance;
                        break;
                }
            }

            // Display 속성 업데이트 알림
            OnPropertyChanged(nameof(TotalMachiningTimeDisplay));
            OnPropertyChanged(nameof(TotalDistanceDisplay));
            OnPropertyChanged(nameof(RapidDistanceDisplay));
            OnPropertyChanged(nameof(CuttingDistanceDisplay));
            OnPropertyChanged(nameof(ArcDistanceDisplay));
        }

        /// <summary>
        /// 경과 시간 업데이트 (시뮬레이션 진행 시 호출)
        /// CurrentSegmentIndex와 CurrentSegmentProgress 기반으로 계산
        /// </summary>
        private void UpdateElapsedTime()
        {
            double elapsed = 0;

            // 완료된 세그먼트의 시간 합산
            for (int i = 0; i < CurrentSegmentIndex && i < _parser.Paths.Count; i++)
            {
                elapsed += _parser.Paths[i].DurationSeconds;
            }

            // 현재 진행 중인 세그먼트의 부분 시간 추가
            if (CurrentSegmentIndex >= 0 && CurrentSegmentIndex < _parser.Paths.Count)
            {
                var currentSegment = _parser.Paths[CurrentSegmentIndex];
                elapsed += currentSegment.DurationSeconds * CurrentSegmentProgress;
            }

            ElapsedMachiningTime = elapsed;
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml;
using GCodeSimulator.ViewModels;
using HelixToolkit.Wpf;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Media3D = System.Windows.Media.Media3D;

namespace GCodeSimulator
{
    /// <summary>
    /// 메인 윈도우 코드 비하인드 클래스
    /// 2D/3D 시각화 및 UI 업데이트를 담당
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext; // ViewModel 참조

        // 경로 타입별 3D 비주얼 객체 (성능 최적화를 위해 미리 생성)
        private readonly LinesVisual3D _rapidPathVisual = new LinesVisual3D { Color = Colors.Blue, Thickness = 1.0 }; // 급속 이송 경로
        private readonly LinesVisual3D _linearPathVisual = new LinesVisual3D { Color = Colors.Red, Thickness = 2.0 }; // 직선 이송 경로
        private readonly LinesVisual3D _arcPathVisual = new LinesVisual3D { Color = Colors.Green, Thickness = 2.0 }; // 원호 이송 경로
        private readonly LinesVisual3D _currentSegmentVisual = new LinesVisual3D { Thickness = 2.0 }; // 현재 재생 중인 세그먼트
        private readonly SphereVisual3D _currentPositionSphere = new SphereVisual3D { Radius = 0.5, Fill = Brushes.Magenta }; // 현재 위치 표시

        // 2D Canvas용 경로 객체 (성능 최적화를 위해 미리 생성하고 재사용)
        private readonly List<Polyline> _completedPaths2D = new List<Polyline>(); // 완료된 경로들
        private readonly Polyline _currentPath2D = new Polyline { StrokeThickness = 2 }; // 현재 진행 중인 경로
        private readonly Ellipse _currentPosition2D = new Ellipse // 현재 위치 마커
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.Magenta,
            Stroke = Brushes.Black,
            StrokeThickness = 1
        };
        private int _lastCompletedSegmentIndex = -1; // 마지막으로 완료된 세그먼트 인덱스 (증분 업데이트용)

        /// <summary>
        /// MainWindow 생성자
        /// ViewModel 초기화 및 2D/3D 비주얼 객체 설정
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            DataContext = vm;
            vm.PropertyChanged += ViewModel_PropertyChanged;
            vm.RequestViewReset += ResetCamera;

            // AvalonEdit 구문 강조 설정
            LoadSyntaxHighlighting();

            // AvalonEdit 데이터 바인딩 설정
            GCodeEditor.TextChanged += (s, e) =>
            {
                if (ViewModel != null)
                    ViewModel.GCodeText = GCodeEditor.Text;
            };

            // 3D 뷰포트에 비주얼 객체들을 한 번만 추가 (성능 최적화)
            Viewport3D.Children.Add(new DefaultLights());
            Viewport3D.Children.Add(_rapidPathVisual);
            Viewport3D.Children.Add(_linearPathVisual);
            Viewport3D.Children.Add(_arcPathVisual);
            Viewport3D.Children.Add(_currentSegmentVisual);
            Viewport3D.Children.Add(_currentPositionSphere);

            // 2D Canvas에 현재 경로와 위치 마커를 한 번만 추가 (성능 최적화)
            Canvas2D.Children.Add(_currentPath2D);
            Canvas2D.Children.Add(_currentPosition2D);
        }

        /// <summary>
        /// GCode.xshd 구문 강조 정의 파일 로드
        /// </summary>
        private void LoadSyntaxHighlighting()
        {
            try
            {
                // 임베디드 리소스에서 GCode.xshd 파일 로드
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "GCodeSimulator.Resources.GCode.xshd";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new XmlTextReader(stream))
                        {
                            var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                            GCodeEditor.SyntaxHighlighting = highlighting;
                        }
                    }
                    else
                    {
                        MessageBox.Show("구문 강조 파일을 찾을 수 없습니다.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"구문 강조 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ViewModel의 속성 변경 이벤트 핸들러
        /// 시각화 업데이트 및 그리드 표시 제어
        /// </summary>
        /// <param name="sender">이벤트 발생자</param>
        /// <param name="e">이벤트 인자</param>
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.Parser))
            {
                // 새 파일 로드 시 2D 경로 초기화
                Reset2DPaths();
                Application.Current.Dispatcher.BeginInvoke(new Action(UpdateVisualization));
            }
            else if (e.PropertyName == nameof(ViewModel.CurrentSegmentIndex) ||
                     e.PropertyName == nameof(ViewModel.CurrentSegmentProgress))
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(UpdateVisualization));
            }
            else if (e.PropertyName == nameof(ViewModel.IsGridVisible))
            {
                UpdateGridVisibility();
            }
            else if (e.PropertyName == nameof(ViewModel.GCodeText))
            {
                // ViewModel에서 GCodeText가 변경되면 TextEditor 업데이트 (무한 루프 방지)
                if (GCodeEditor.Text != ViewModel.GCodeText)
                {
                    GCodeEditor.Text = ViewModel.GCodeText;
                }
            }
        }

        /// <summary>
        /// 2D 경로 초기화 (새 파일 로드 시 호출)
        /// </summary>
        private void Reset2DPaths()
        {
            // 완료된 경로들을 Canvas에서 제거
            foreach (var polyline in _completedPaths2D)
            {
                Canvas2D.Children.Remove(polyline);
            }
            _completedPaths2D.Clear();

            // 현재 경로 초기화
            _currentPath2D.Points.Clear();
            _currentPosition2D.Visibility = Visibility.Collapsed;

            // 인덱스 리셋
            _lastCompletedSegmentIndex = -1;
        }

        /// <summary>
        /// 3D 그리드의 표시/숨김 상태 업데이트
        /// </summary>
        private void UpdateGridVisibility()
        {
            if (GridLines3D == null) return;

            bool shouldBeVisible = ViewModel.IsGridVisible;
            bool isCurrentlyInViewport = Viewport3D.Children.Contains(GridLines3D);

            if (shouldBeVisible && !isCurrentlyInViewport)
            {
                Viewport3D.Children.Add(GridLines3D);
            }
            else if (!shouldBeVisible && isCurrentlyInViewport)
            {
                Viewport3D.Children.Remove(GridLines3D);
            }
        }

        /// <summary>
        /// 3D 카메라를 모델 중심으로 리셋
        /// 모델 크기에 맞춰 적절한 거리로 카메라 위치 조정
        /// </summary>
        private void ResetCamera()
        {
            if (Viewport3D.Visibility != Visibility.Visible) return;
            var vm = ViewModel;
            var camera = Viewport3D.Camera as Media3D.ProjectionCamera;
            if (camera != null)
            { 
                double zPos = Math.Max(vm.ModelWidth, vm.ModelLength) * 1.2;
                if (zPos < 1) zPos = 100;
                var center = vm.ModelCenter;
                camera.Position = new Media3D.Point3D(center.X, center.Y, zPos);
                camera.LookDirection = new Media3D.Vector3D(0, 0, -zPos);
                camera.UpDirection = new Media3D.Vector3D(0, 1, 0);
            }
            Viewport3D.ZoomExtents(200);
        }

        /// <summary>
        /// 시각화 업데이트 (2D/3D 자동 전환 또는 수동 전환)
        /// ViewMode에 따라 2D 또는 3D 뷰를 선택
        /// </summary>
        private void UpdateVisualization()
        {
            var parser = ViewModel.Parser;
            if (parser == null || (parser.Paths.Count == 0 && !string.IsNullOrEmpty(GCodeEditor.Text))) return;

            // ViewMode에 따라 표시할 뷰 결정
            bool show3D = false;

            switch (ViewModel.CurrentViewMode)
            {
                case ViewModels.ViewMode.Auto:
                    // 자동 모드: Z축 이동에 따라 자동 선택
                    show3D = parser.Is3D;
                    break;
                case ViewModels.ViewMode.View2D:
                    // 강제 2D 모드: Z축 무시하고 XY 평면만 표시
                    show3D = false;
                    break;
                case ViewModels.ViewMode.View3D:
                    // 강제 3D 모드
                    show3D = true;
                    break;
            }

            if (show3D)
            {
                if (Canvas2D.Visibility == Visibility.Visible) Canvas2D.Visibility = Visibility.Collapsed;
                if (Viewport3D.Visibility != Visibility.Visible) Viewport3D.Visibility = Visibility.Visible;
                Update3DVisualization();
            }
            else
            {
                if (Viewport3D.Visibility == Visibility.Visible) Viewport3D.Visibility = Visibility.Collapsed;
                if (Canvas2D.Visibility != Visibility.Visible) Canvas2D.Visibility = Visibility.Visible;
                Update2DVisualization();
            }
        }

        /// <summary>
        /// 2D 시각화 업데이트 (Canvas + Polyline 사용, 3D처럼 증분 업데이트)
        /// 완료된 경로는 한 번만 추가하고, 현재 세그먼트만 매 프레임 업데이트
        /// </summary>
        private void Update2DVisualization()
        {
            try
            {
                var parser = ViewModel.Parser;
                if (parser == null || parser.Paths == null || parser.Paths.Count == 0)
                    return;

                int currentIndex = ViewModel.CurrentSegmentIndex;
                double currentProgress = ViewModel.CurrentSegmentProgress;

                // 좌표 범위 계산
                double rangeX = parser.MaxX - parser.MinX;
                double rangeY = parser.MaxY - parser.MinY;
                if (rangeX < 0.01) rangeX = 100;
                if (rangeY < 0.01) rangeY = 100;

                double margin = Math.Max(rangeX, rangeY) * 0.1;
                if (margin < 1) margin = 10;

                double worldMinX = parser.MinX - margin;
                double worldMaxX = parser.MaxX + margin;
                double worldMinY = parser.MinY - margin;
                double worldMaxY = parser.MaxY + margin;

                // Canvas 크기
                double canvasWidth = Canvas2D.ActualWidth;
                double canvasHeight = Canvas2D.ActualHeight;
                if (canvasWidth < 10 || canvasHeight < 10)
                {
                    canvasWidth = 800;
                    canvasHeight = 600;
                }

                // 축 여백 (레이블용)
                double axisMarginLeft = 60;
                double axisMarginBottom = 40;
                double axisMarginRight = 20;
                double axisMarginTop = 40;

                double drawWidth = canvasWidth - axisMarginLeft - axisMarginRight;
                double drawHeight = canvasHeight - axisMarginTop - axisMarginBottom;

                // 좌표 변환 함수 (World → Screen)
                Point WorldToScreen(double x, double y)
                {
                    double scaleX = drawWidth / (worldMaxX - worldMinX);
                    double scaleY = drawHeight / (worldMaxY - worldMinY);
                    double scale = Math.Min(scaleX, scaleY);

                    double screenX = axisMarginLeft + (x - worldMinX) * scale;
                    double screenY = canvasHeight - axisMarginBottom - (y - worldMinY) * scale;

                    return new Point(screenX, screenY);
                }

                // 증분 업데이트: 새로 완료된 세그먼트만 추가
                int maxIndex = Math.Min(currentIndex, parser.Paths.Count);
                for (int i = _lastCompletedSegmentIndex + 1; i < maxIndex; i++)
                {
                    var segment = parser.Paths[i];
                    if (segment == null || segment.Points == null || segment.Points.Count == 0)
                        continue;

                    // 새 Polyline 생성 (한 번만!)
                    var polyline = new Polyline
                    {
                        Stroke = GetBrushForPathType(segment.PathType),
                        StrokeThickness = segment.PathType == "rapid" ? 1 : 1.5,
                        StrokeDashArray = segment.PathType == "rapid" ? new DoubleCollection { 4, 2 } : null
                    };

                    // 점들을 Polyline에 추가
                    foreach (var point in segment.Points)
                    {
                        if (point != null)
                            polyline.Points.Add(WorldToScreen(point.X, point.Y));
                    }

                    // Canvas에 추가하고 리스트에 저장 (재사용)
                    Canvas2D.Children.Add(polyline);
                    _completedPaths2D.Add(polyline);
                }
                _lastCompletedSegmentIndex = maxIndex - 1;

                // 현재 진행 중인 세그먼트 업데이트 (매 프레임)
                _currentPath2D.Points.Clear();
                if (currentIndex >= 0 && currentIndex < parser.Paths.Count && currentProgress > 0)
                {
                    var segment = parser.Paths[currentIndex];
                    if (segment != null && segment.Points != null && segment.Points.Count > 0)
                    {
                        _currentPath2D.Stroke = GetBrushForPathType(segment.PathType);
                        _currentPath2D.StrokeDashArray = segment.PathType == "rapid" ? new DoubleCollection { 4, 2 } : null;

                        int pointsToShow = (int)(segment.Points.Count * currentProgress);
                        if (pointsToShow == 0 && segment.Points.Count > 0) pointsToShow = 1;
                        pointsToShow = Math.Min(pointsToShow, segment.Points.Count);

                        for (int j = 0; j < pointsToShow; j++)
                        {
                            var point = segment.Points[j];
                            if (point != null)
                                _currentPath2D.Points.Add(WorldToScreen(point.X, point.Y));
                        }

                        // 현재 위치 마커 업데이트
                        if (pointsToShow > 0)
                        {
                            var currentPoint = segment.Points[pointsToShow - 1];
                            if (currentPoint != null)
                            {
                                var screenPos = WorldToScreen(currentPoint.X, currentPoint.Y);
                                Canvas.SetLeft(_currentPosition2D, screenPos.X - _currentPosition2D.Width / 2);
                                Canvas.SetTop(_currentPosition2D, screenPos.Y - _currentPosition2D.Height / 2);
                                _currentPosition2D.Visibility = Visibility.Visible;
                            }
                        }
                        else
                        {
                            _currentPosition2D.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                else
                {
                    _currentPosition2D.Visibility = Visibility.Collapsed;
                }

                // 축과 그리드 그리기 (필요시 - 간단한 버전)
                DrawAxesAndGrid(worldMinX, worldMaxX, worldMinY, worldMaxY, axisMarginLeft, axisMarginBottom,
                    axisMarginTop, axisMarginRight, canvasWidth, canvasHeight);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"2D 시각화 오류: {ex.Message}");
                ViewModel.StatusText = $"2D 시각화 오류: {ex.Message}";
            }
        }

        /// <summary>
        /// 경로 타입에 따른 Brush 반환
        /// </summary>
        /// <param name="pathType">경로 타입 ("rapid", "linear", "arc")</param>
        /// <returns>해당하는 Brush</returns>
        private Brush GetBrushForPathType(string pathType)
        {
            switch (pathType)
            {
                case "rapid": return Brushes.Blue; // 급속 이송: 파란색
                case "linear": return Brushes.Red; // 직선 이송: 빨간색
                case "arc": return Brushes.Green; // 원호 이송: 초록색
                default: return Brushes.Gray;
            }
        }

        /// <summary>
        /// 값을 적절한 형식으로 포맷 (과학적 표기법, 단위 자동 조정)
        /// </summary>
        /// <param name="value">포맷할 값</param>
        /// <returns>포맷된 문자열</returns>
        private string FormatAxisValue(double value)
        {
            double absValue = Math.Abs(value);

            // 매우 작은 값 (0에 가까운 경우)
            if (absValue < 1e-10)
                return "0";

            // 과학적 표기법이 필요한 경우 (매우 큰 값 또는 매우 작은 값)
            if (absValue >= 10000 || (absValue < 0.001 && absValue > 0))
                return value.ToString("E1"); // 예: 1.2E+03

            // 정수 또는 정수에 가까운 값
            if (Math.Abs(value - Math.Round(value)) < 0.001)
                return Math.Round(value).ToString("F0"); // 예: 123

            // 일반적인 소수점 값
            if (absValue >= 100)
                return value.ToString("F0"); // 100 이상은 정수로
            else if (absValue >= 10)
                return value.ToString("F1"); // 10~100은 소수점 1자리
            else if (absValue >= 1)
                return value.ToString("F2"); // 1~10은 소수점 2자리
            else
                return value.ToString("F3"); // 1 미만은 소수점 3자리
        }

        /// <summary>
        /// 적절한 눈금 간격 계산 (깔끔한 숫자로)
        /// </summary>
        /// <param name="range">축의 범위</param>
        /// <param name="targetTicks">목표 눈금 개수</param>
        /// <returns>계산된 눈금 간격</returns>
        private double CalculateNiceTickInterval(double range, int targetTicks = 5)
        {
            double roughInterval = range / targetTicks;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughInterval)));

            // 1, 2, 5, 10의 배수로 간격 조정
            double[] niceNumbers = { 1, 2, 5, 10 };
            double normalizedInterval = roughInterval / magnitude;

            double niceInterval = niceNumbers[0];
            foreach (var nice in niceNumbers)
            {
                if (normalizedInterval <= nice)
                {
                    niceInterval = nice;
                    break;
                }
            }

            return niceInterval * magnitude;
        }

        /// <summary>
        /// 2D Canvas에 축과 그리드 그리기
        /// </summary>
        private void DrawAxesAndGrid(double worldMinX, double worldMaxX, double worldMinY, double worldMaxY,
            double axisMarginLeft, double axisMarginBottom, double axisMarginTop, double axisMarginRight,
            double canvasWidth, double canvasHeight)
        {
            // 이전에 그린 축/그리드 제거 (Tag로 식별)
            var toRemove = Canvas2D.Children.OfType<UIElement>()
                .Where(e => e is Line line && line.Tag?.ToString() == "Axis" ||
                            e is TextBlock tb && tb.Tag?.ToString() == "AxisLabel")
                .ToList();
            foreach (var element in toRemove)
            {
                Canvas2D.Children.Remove(element);
            }

            // X축 그리기
            var xAxis = new Line
            {
                X1 = axisMarginLeft,
                Y1 = canvasHeight - axisMarginBottom,
                X2 = canvasWidth - axisMarginRight,
                Y2 = canvasHeight - axisMarginBottom,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Tag = "Axis"
            };
            Canvas2D.Children.Add(xAxis);

            // Y축 그리기
            var yAxis = new Line
            {
                X1 = axisMarginLeft,
                Y1 = axisMarginTop,
                X2 = axisMarginLeft,
                Y2 = canvasHeight - axisMarginBottom,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Tag = "Axis"
            };
            Canvas2D.Children.Add(yAxis);

            // 축 레이블
            var xLabel = new TextBlock
            {
                Text = "X (mm)",
                FontSize = 12,
                Tag = "AxisLabel"
            };
            Canvas.SetLeft(xLabel, (canvasWidth - axisMarginLeft - axisMarginRight) / 2 + axisMarginLeft - 20);
            Canvas.SetTop(xLabel, canvasHeight - 20);
            Canvas2D.Children.Add(xLabel);

            var yLabel = new TextBlock
            {
                Text = "Y (mm)",
                FontSize = 12,
                Tag = "AxisLabel"
            };
            Canvas.SetLeft(yLabel, 10);
            Canvas.SetTop(yLabel, (canvasHeight - axisMarginTop - axisMarginBottom) / 2 + axisMarginTop - 10);
            Canvas2D.Children.Add(yLabel);

            // 동적 눈금 간격 계산 (깔끔한 숫자로)
            double rangeX = worldMaxX - worldMinX;
            double rangeY = worldMaxY - worldMinY;
            double tickIntervalX = CalculateNiceTickInterval(rangeX);
            double tickIntervalY = CalculateNiceTickInterval(rangeY);

            double drawWidth = canvasWidth - axisMarginLeft - axisMarginRight;
            double drawHeight = canvasHeight - axisMarginTop - axisMarginBottom;
            double scaleX = drawWidth / rangeX;
            double scaleY = drawHeight / rangeY;

            // X축 눈금 (깔끔한 간격으로)
            double startX = Math.Ceiling(worldMinX / tickIntervalX) * tickIntervalX;
            for (double worldX = startX; worldX <= worldMaxX; worldX += tickIntervalX)
            {
                double screenX = axisMarginLeft + (worldX - worldMinX) * scaleX;

                // 눈금선
                var xTick = new Line
                {
                    X1 = screenX,
                    Y1 = canvasHeight - axisMarginBottom,
                    X2 = screenX,
                    Y2 = canvasHeight - axisMarginBottom + 5,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Tag = "Axis"
                };
                Canvas2D.Children.Add(xTick);

                // 그리드선 (옅은 회색)
                var xGridLine = new Line
                {
                    X1 = screenX,
                    Y1 = axisMarginTop,
                    X2 = screenX,
                    Y2 = canvasHeight - axisMarginBottom,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 2 },
                    Tag = "Axis"
                };
                Canvas2D.Children.Add(xGridLine);

                // 레이블 (스마트 포맷팅)
                var xTickLabel = new TextBlock
                {
                    Text = FormatAxisValue(worldX),
                    FontSize = 10,
                    Tag = "AxisLabel"
                };
                Canvas.SetLeft(xTickLabel, screenX - 20);
                Canvas.SetTop(xTickLabel, canvasHeight - axisMarginBottom + 8);
                Canvas2D.Children.Add(xTickLabel);
            }

            // Y축 눈금 (깔끔한 간격으로)
            double startY = Math.Ceiling(worldMinY / tickIntervalY) * tickIntervalY;
            for (double worldY = startY; worldY <= worldMaxY; worldY += tickIntervalY)
            {
                double screenY = canvasHeight - axisMarginBottom - (worldY - worldMinY) * scaleY;

                // 눈금선
                var yTick = new Line
                {
                    X1 = axisMarginLeft - 5,
                    Y1 = screenY,
                    X2 = axisMarginLeft,
                    Y2 = screenY,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Tag = "Axis"
                };
                Canvas2D.Children.Add(yTick);

                // 그리드선 (옅은 회색)
                var yGridLine = new Line
                {
                    X1 = axisMarginLeft,
                    Y1 = screenY,
                    X2 = canvasWidth - axisMarginRight,
                    Y2 = screenY,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 2 },
                    Tag = "Axis"
                };
                Canvas2D.Children.Add(yGridLine);

                // 레이블 (스마트 포맷팅)
                var yTickLabel = new TextBlock
                {
                    Text = FormatAxisValue(worldY),
                    FontSize = 10,
                    Tag = "AxisLabel"
                };
                Canvas.SetLeft(yTickLabel, axisMarginLeft - 55);
                Canvas.SetTop(yTickLabel, screenY - 7);
                Canvas2D.Children.Add(yTickLabel);
            }
        }

        /// <summary>
        /// 3D 시각화 업데이트 (HelixToolkit 사용)
        /// 경로를 타입별로 분류하여 색상으로 구분하고 현재 위치를 표시
        /// </summary>
        private void Update3DVisualization()
        {
            try
            {
                var parser = ViewModel.Parser;
                if (parser == null || parser.Paths == null || parser.Paths.Count == 0)
                    return;

                int currentIndex = ViewModel.CurrentSegmentIndex;
                double currentProgress = ViewModel.CurrentSegmentProgress;

                var rapidPoints = new Media3D.Point3DCollection();
                var linearPoints = new Media3D.Point3DCollection();
                var arcPoints = new Media3D.Point3DCollection();

                // 완료된 세그먼트 그리기
                int maxIndex = Math.Min(currentIndex, parser.Paths.Count);
                for (int i = 0; i < maxIndex; i++)
                {
                    var segment = parser.Paths[i];
                    if (segment == null || segment.Points == null || segment.Points.Count == 0)
                        continue;

                    var targetCollection = GetCollectionForPathType(segment.PathType, rapidPoints, linearPoints, arcPoints);
                    foreach (var point in segment.Points)
                    {
                        if (point != null)
                            targetCollection.Add(new Media3D.Point3D(point.X, point.Y, point.Z));
                    }
                }

                _rapidPathVisual.Points = rapidPoints;
                _linearPathVisual.Points = linearPoints;
                _arcPathVisual.Points = arcPoints;

                // 현재 진행 중인 세그먼트 그리기
                var currentPoints = new Media3D.Point3DCollection();
                if (currentIndex >= 0 && currentIndex < parser.Paths.Count && currentProgress > 0)
                {
                    var segment = parser.Paths[currentIndex];
                    if (segment != null && segment.Points != null && segment.Points.Count > 0)
                    {
                        int pointsToShow = (int)(segment.Points.Count * currentProgress);
                        if (pointsToShow == 0 && segment.Points.Count > 0) pointsToShow = 1;
                        pointsToShow = Math.Min(pointsToShow, segment.Points.Count);

                        for (int j = 0; j < pointsToShow; j++)
                        {
                            var p = segment.Points[j];
                            if (p != null)
                                currentPoints.Add(new Media3D.Point3D(p.X, p.Y, p.Z));
                        }

                        _currentSegmentVisual.Points = currentPoints;
                        _currentSegmentVisual.Color = GetColorForPathType3D(segment.PathType);

                        // 현재 위치 마커
                        if (pointsToShow > 0 && pointsToShow <= segment.Points.Count)
                        {
                            var currentPoint = segment.Points[pointsToShow - 1];
                            if (currentPoint != null)
                            {
                                _currentPositionSphere.Center = new Media3D.Point3D(currentPoint.X, currentPoint.Y, currentPoint.Z);
                                double rangeX = parser.MaxX - parser.MinX;
                                _currentPositionSphere.Radius = Math.Max(rangeX * 0.015, 0.5);
                                _currentPositionSphere.Fill = Brushes.Magenta;
                            }
                            else
                            {
                                _currentPositionSphere.Fill = Brushes.Transparent;
                            }
                        }
                        else
                        {
                            _currentPositionSphere.Fill = Brushes.Transparent;
                        }
                    }
                    else
                    {
                        _currentSegmentVisual.Points = null;
                        _currentPositionSphere.Fill = Brushes.Transparent;
                    }
                }
                else
                {
                    _currentSegmentVisual.Points = null;
                    _currentPositionSphere.Fill = Brushes.Transparent;
                }
            }
            catch (Exception ex)
            {
                // 예외 발생 시 상태 표시줄에 표시
                System.Diagnostics.Debug.WriteLine($"3D 시각화 오류: {ex.Message}");
                ViewModel.StatusText = $"3D 시각화 오류: {ex.Message}";
            }
        }
        
        /// <summary>
        /// 경로 타입에 따라 해당하는 Point3D 컬렉션 반환
        /// </summary>
        /// <param name="pathType">경로 타입</param>
        /// <param name="rapid">급속 이송 점 컬렉션</param>
        /// <param name="linear">직선 이송 점 컬렉션</param>
        /// <param name="arc">원호 이송 점 컬렉션</param>
        /// <returns>경로 타입에 해당하는 컬렉션</returns>
        private Media3D.Point3DCollection GetCollectionForPathType(string pathType, Media3D.Point3DCollection rapid, Media3D.Point3DCollection linear, Media3D.Point3DCollection arc)
        {
            switch (pathType)
            {
                case "rapid": return rapid;
                case "linear": return linear;
                case "arc": return arc;
                default: return linear;
            }
        }

        /// <summary>
        /// 경로 타입에 따른 3D 색상 반환
        /// </summary>
        /// <param name="pathType">경로 타입</param>
        /// <returns>해당하는 색상</returns>
        private Color GetColorForPathType3D(string pathType)
        {
            switch (pathType)
            {
                case "rapid": return Colors.Blue; // 급속 이송: 파란색
                case "linear": return Colors.Red; // 직선 이송: 빨간색
                case "arc": return Colors.Green; // 원호 이송: 초록색
                default: return Colors.Gray;
            }
        }

        /// <summary>
        /// 윈도우 종료 시 이벤트 핸들러
        /// 저장되지 않은 변경사항이 있으면 사용자에게 확인
        /// </summary>
        /// <param name="sender">이벤트 발생자</param>
        /// <param name="e">이벤트 인자</param>
        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (ViewModel.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "저장하지 않은 변경사항이 있습니다. 저장하시겠습니까?",
                    "변경사항 저장",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ViewModel.SaveFileCommand.Execute(null);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }
    }
}
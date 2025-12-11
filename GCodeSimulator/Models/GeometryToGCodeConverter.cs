using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GCodeSimulator.Models
{
    /// <summary>
    /// DXF/DWG 기하학적 엔티티를 G-code로 변환하는 변환기 클래스
    /// </summary>
    public class GeometryToGCodeConverter
    {
        private const double FeedRate = 200.0; // 절삭 이송 속도 (mm/min)
        private const double PlungeFeedRate = 100.0; // 하강 이송 속도 (mm/min)
        private const double SafeZ = 5.0; // 안전 높이 (mm)
        private const double CutZ = -1.0; // 절삭 깊이 (mm)
        private const double MaxSegmentLength = 0.5; // 부드러운 곡선을 위한 세그먼트당 최대 길이 (mm)
        private const double MinSegmentsPerDegree = 0.5; // 작은 호를 위한 각도당 최소 세그먼트 수
        private const double MergeTolerance = 1e-6; // 경로 끝점 병합을 위한 허용 오차
        private const double RapidMoveFeedRate = 5000.0; // 급속 이동 속도 (mm/min)


        /// <summary>
        /// 기하학적 엔티티를 이동 가능한 경로로 표현하는 내부 클래스
        /// </summary>
        private class DrawablePath
        {
            public List<DxfPoint> Points { get; private set; } // 경로를 구성하는 점들의 리스트
            public bool IsClosed { get; } // 닫힌 경로 여부

            public DxfPoint StartPoint => Points.FirstOrDefault(); // 시작점
            public DxfPoint EndPoint => Points.LastOrDefault(); // 끝점

            /// <summary>
            /// DrawablePath 생성자
            /// </summary>
            /// <param name="points">경로 점 리스트</param>
            /// <param name="isClosed">닫힌 경로 여부</param>
            public DrawablePath(List<DxfPoint> points, bool isClosed)
            {
                Points = points ?? new List<DxfPoint>();
                IsClosed = isClosed;
            }

            /// <summary>
            /// 경로의 점 순서를 역순으로 뒤집기
            /// </summary>
            public void Reverse()
            {
                if (Points.Any())
                {
                    Points.Reverse();
                }
            }
        }

        /// <summary>
        /// DXF 엔티티 리스트를 G-code 텍스트로 변환
        /// 한붓그리기 최적화를 통해 이동 거리를 최소화
        /// </summary>
        /// <param name="entities">변환할 DXF 엔티티 리스트</param>
        /// <returns>생성된 G-code 텍스트</returns>
        public static string ConvertToGCode(IEnumerable<DxfEntity> entities)
        {
            var sb = new StringBuilder();

            // Initial G-code setup
            sb.AppendLine("G90 ; Absolute positioning");
            sb.AppendLine("G21 ; Millimeter units");
            sb.AppendLine("G17 ; XY plane selection");
            sb.AppendLine();

            // 1. Convert all entities to DrawablePath objects
            var unsortedPaths = new List<DrawablePath>();
            foreach (var entity in entities)
            {
                switch (entity.EntityType)
                {
                    case DxfEntityType.Line:
                        var line = (DxfLine)entity;
                        unsortedPaths.Add(new DrawablePath(new List<DxfPoint> { line.P1, line.P2 }, false));
                        break;
                    case DxfEntityType.Circle:
                        var circle = (DxfCircle)entity;
                        int circleSegments = CalculateOptimalSegments(circle.Radius, 360);
                        unsortedPaths.Add(new DrawablePath(LinearizeArc(circle.Center, circle.Radius, 0, 360, circleSegments), true));
                        break;
                    case DxfEntityType.Arc:
                        var arc = (DxfArc)entity;
                        double arcAngle = Math.Abs(arc.EndAngle - arc.StartAngle);
                        int arcSegments = CalculateOptimalSegments(arc.Radius, arcAngle);
                        unsortedPaths.Add(new DrawablePath(LinearizeArc(arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle, arcSegments), false));
                        break;
                    case DxfEntityType.LwPolyline:
                        var poly = (DxfLwPolyline)entity;
                        unsortedPaths.Add(new DrawablePath(LinearizePolyline(poly), poly.IsClosed));
                        break;
                }
            }
            
            // 2. Sort paths using Nearest-Neighbor heuristic
            var sortedPaths = new List<DrawablePath>();
            var currentPosition = new DxfPoint(0, 0, 0);

            while (unsortedPaths.Count > 0)
            {
                DrawablePath closestPath = null;
                double minDistance = double.MaxValue;
                bool reversePath = false;

                foreach (var path in unsortedPaths)
                {
                    double distToStart = PointDistance(currentPosition, path.StartPoint);
                    double distToEnd = PointDistance(currentPosition, path.EndPoint);

                    if (distToStart < minDistance)
                    {
                        minDistance = distToStart;
                        closestPath = path;
                        reversePath = false;
                    }

                    if (distToEnd < minDistance)
                    {
                        minDistance = distToEnd;
                        closestPath = path;
                        reversePath = true;
                    }
                }

                if (closestPath != null)
                {
                    if (reversePath)
                    {
                        closestPath.Reverse();
                    }
                    sortedPaths.Add(closestPath);
                    unsortedPaths.Remove(closestPath);
                    currentPosition = closestPath.EndPoint;
                }
                else
                {
                    // Should not happen if unsortedPaths is not empty
                    break;
                }
            }

            // 3. Merge contiguous paths
            var mergedPaths = new List<DrawablePath>();
            if (sortedPaths.Count > 0)
            {
                var currentMergedPath = new DrawablePath(new List<DxfPoint>(sortedPaths[0].Points), sortedPaths[0].IsClosed);
                
                for (int i = 1; i < sortedPaths.Count; i++)
                {
                    var nextPath = sortedPaths[i];
                    if (PointDistance(currentMergedPath.EndPoint, nextPath.StartPoint) < MergeTolerance)
                    {
                        // Merge: add points from the next path (excluding its start point)
                        currentMergedPath.Points.AddRange(nextPath.Points.Skip(1));
                    }
                    else
                    {
                        // Finish the current path and start a new one
                        mergedPaths.Add(currentMergedPath);
                        currentMergedPath = new DrawablePath(new List<DxfPoint>(nextPath.Points), nextPath.IsClosed);
                    }
                }
                mergedPaths.Add(currentMergedPath); // Add the last path
            }

            // 4. Generate G-Code from the optimized paths
            sb.AppendLine($"G00 F{RapidMoveFeedRate.ToString(CultureInfo.InvariantCulture)}");

            foreach (var path in mergedPaths)
            {
                if(path.Points.Count == 0) continue;

                sb.AppendLine($"; --- New Path ---");
                // Rapid move to the start of the path
                sb.AppendLine(RapidMoveTo(path.StartPoint.X, path.StartPoint.Y));
                
                // Plunge down to cutting depth
                sb.AppendLine(Plunge());

                // Linear moves for the rest of the path
                for (int i = 1; i < path.Points.Count; i++)
                {
                    sb.AppendLine(LinearMoveTo(path.Points[i].X, path.Points[i].Y));
                }

                // If the original path was closed, add a final move back to the start
                if (path.IsClosed)
                {
                    sb.AppendLine(LinearMoveTo(path.StartPoint.X, path.StartPoint.Y));
                }

                // Retract the tool
                sb.AppendLine(Retract());
                sb.AppendLine();
            }

            // Return to start position before ending
            sb.AppendLine("; --- Return to start position ---");
            sb.AppendLine($"G00 Z{SafeZ.ToString("F3", CultureInfo.InvariantCulture)} ; Retract to safe height");
            sb.AppendLine($"G00 X0.000 Y0.000 ; Return to origin");
            sb.AppendLine();
            sb.AppendLine("M30 ; End of program");
            return sb.ToString();
        }

        /// <summary>
        /// 부드러운 곡선을 위한 최적의 세그먼트 수 계산
        /// 반지름과 각도를 기반으로 호의 길이를 고려하여 일관된 품질 보장
        /// </summary>
        /// <param name="radius">호의 반지름</param>
        /// <param name="angleDegrees">호의 각도 (도 단위)</param>
        /// <returns>계산된 세그먼트 수 (4~360 범위)</returns>
        private static int CalculateOptimalSegments(double radius, double angleDegrees)
        {
            // Calculate arc length: L = r * θ (where θ is in radians)
            double angleRadians = angleDegrees * Math.PI / 180.0;
            double arcLength = radius * angleRadians;

            // Calculate segments based on max segment length
            int segmentsByLength = (int)Math.Ceiling(arcLength / MaxSegmentLength);

            // Calculate segments based on angle (more segments for larger angles)
            int segmentsByAngle = (int)Math.Ceiling(angleDegrees * MinSegmentsPerDegree);

            // Use the maximum of the two to ensure both length and angle requirements are met
            int segments = Math.Max(segmentsByLength, segmentsByAngle);

            // Ensure minimum of 4 segments for any arc and maximum of 360 for very large circles
            return Math.Max(4, Math.Min(360, segments));
        }

        /// <summary>
        /// 폴리라인을 선형화하여 점 리스트로 변환
        /// Bulge 값을 처리하여 곡선 세그먼트를 직선으로 근사화
        /// Bulge는 두 정점 사이의 곡률을 정의 (bulge = tan(angle/4))
        /// </summary>
        /// <param name="polyline">변환할 경량 폴리라인</param>
        /// <returns>변환된 점 리스트</returns>
        private static List<DxfPoint> LinearizePolyline(DxfLwPolyline polyline)
        {
            var points = new List<DxfPoint>();
            var vertices = polyline.Vertices.ToList();

            if (vertices.Count == 0) return points;

            // Add first point
            points.Add(new DxfPoint(vertices[0].X, vertices[0].Y, 0));

            // Process each segment
            for (int i = 0; i < vertices.Count - 1; i++)
            {
                var v1 = vertices[i];
                var v2 = vertices[i + 1];

                // If bulge is zero or very small, it's a straight line
                if (Math.Abs(v1.Bulge) < 1e-10)
                {
                    points.Add(new DxfPoint(v2.X, v2.Y, 0));
                }
                else
                {
                    // Bulge defines an arc: bulge = tan(angle/4)
                    // Calculate arc parameters from bulge
                    double bulge = v1.Bulge;
                    double angle = 4 * Math.Atan(Math.Abs(bulge));

                    double dx = v2.X - v1.X;
                    double dy = v2.Y - v1.Y;
                    double chordLength = Math.Sqrt(dx * dx + dy * dy);

                    // Calculate radius from bulge and chord length
                    double radius = chordLength * (1 + bulge * bulge) / (4 * Math.Abs(bulge));

                    // Calculate center point
                    double offsetDist = chordLength * bulge / 2;
                    double midX = (v1.X + v2.X) / 2;
                    double midY = (v1.Y + v2.Y) / 2;

                    // Perpendicular direction to chord
                    double perpX = -dy / chordLength;
                    double perpY = dx / chordLength;

                    double centerX = midX + perpX * offsetDist;
                    double centerY = midY + perpY * offsetDist;

                    // Calculate start and end angles
                    double startAngle = Math.Atan2(v1.Y - centerY, v1.X - centerX) * 180 / Math.PI;
                    double endAngle = Math.Atan2(v2.Y - centerY, v2.X - centerX) * 180 / Math.PI;

                    // Adjust angles based on bulge sign (direction)
                    if (bulge < 0)
                    {
                        // Swap for clockwise direction
                        double temp = startAngle;
                        startAngle = endAngle;
                        endAngle = temp;
                    }

                    // Calculate optimal segments for this arc
                    double arcAngle = Math.Abs(angle * 180 / Math.PI);
                    int segments = CalculateOptimalSegments(radius, arcAngle);

                    // Generate arc points (excluding start point as it's already added)
                    var arcPoints = LinearizeArc(new DxfPoint(centerX, centerY, 0), radius, startAngle, endAngle, segments);
                    points.AddRange(arcPoints.Skip(1));
                }
            }

            // Handle closed polyline
            if (polyline.IsClosed && vertices.Count > 0)
            {
                var lastVertex = vertices[vertices.Count - 1];
                var firstVertex = vertices[0];

                if (Math.Abs(lastVertex.Bulge) > 1e-10)
                {
                    // Process the closing arc
                    double bulge = lastVertex.Bulge;
                    double angle = 4 * Math.Atan(Math.Abs(bulge));

                    double dx = firstVertex.X - lastVertex.X;
                    double dy = firstVertex.Y - lastVertex.Y;
                    double chordLength = Math.Sqrt(dx * dx + dy * dy);

                    if (chordLength > 1e-10)
                    {
                        double radius = chordLength * (1 + bulge * bulge) / (4 * Math.Abs(bulge));

                        double offsetDist = chordLength * bulge / 2;
                        double midX = (lastVertex.X + firstVertex.X) / 2;
                        double midY = (lastVertex.Y + firstVertex.Y) / 2;

                        double perpX = -dy / chordLength;
                        double perpY = dx / chordLength;

                        double centerX = midX + perpX * offsetDist;
                        double centerY = midY + perpY * offsetDist;

                        double startAngle = Math.Atan2(lastVertex.Y - centerY, lastVertex.X - centerX) * 180 / Math.PI;
                        double endAngle = Math.Atan2(firstVertex.Y - centerY, firstVertex.X - centerX) * 180 / Math.PI;

                        if (bulge < 0)
                        {
                            double temp = startAngle;
                            startAngle = endAngle;
                            endAngle = temp;
                        }

                        double arcAngle = Math.Abs(angle * 180 / Math.PI);
                        int segments = CalculateOptimalSegments(radius, arcAngle);

                        var arcPoints = LinearizeArc(new DxfPoint(centerX, centerY, 0), radius, startAngle, endAngle, segments);
                        points.AddRange(arcPoints.Skip(1));
                    }
                }
            }

            return points;
        }

        /// <summary>
        /// 원호를 직선 세그먼트로 근사화하여 점 리스트로 변환
        /// </summary>
        /// <param name="center">원의 중심점</param>
        /// <param name="radius">반지름</param>
        /// <param name="startAngle">시작 각도 (도 단위)</param>
        /// <param name="endAngle">끝 각도 (도 단위)</param>
        /// <param name="segments">세그먼트 수</param>
        /// <returns>원호를 근사화한 점 리스트</returns>
        private static List<DxfPoint> LinearizeArc(DxfPoint center, double radius, double startAngle, double endAngle, int segments)
        {
            var points = new List<DxfPoint>();
            if (segments <= 0) segments = 1;

            if (endAngle < startAngle)
            {
                endAngle += 360;
            }

            double angleRange = endAngle - startAngle;
            double angleStep = angleRange / segments;

            for (int i = 0; i <= segments; i++)
            {
                double currentAngle = startAngle + i * angleStep;
                double angleInRadians = currentAngle * Math.PI / 180.0;
                double x = center.X + radius * Math.Cos(angleInRadians);
                double y = center.Y + radius * Math.Sin(angleInRadians);
                points.Add(new DxfPoint(x, y, center.Z));
            }
            return points;
        }

        /// <summary>
        /// 두 점 사이의 2D 유클리드 거리 계산
        /// </summary>
        /// <param name="p1">첫 번째 점</param>
        /// <param name="p2">두 번째 점</param>
        /// <returns>두 점 사이의 거리</returns>
        private static double PointDistance(DxfPoint p1, DxfPoint p2)
        {
            if (p1 == null || p2 == null) return double.MaxValue;
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 급속 이동 G-code 생성 (G00)
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>급속 이동 G-code 문자열</returns>
        private static string RapidMoveTo(double x, double y)
        {
            return $"G00 X{x.ToString("F3", CultureInfo.InvariantCulture)} Y{y.ToString("F3", CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// 직선 이동 G-code 생성 (G01)
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>직선 이동 G-code 문자열</returns>
        private static string LinearMoveTo(double x, double y)
        {
            return $"G01 X{x.ToString("F3", CultureInfo.InvariantCulture)} Y{y.ToString("F3", CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// 공구를 절삭 깊이로 하강시키는 G-code 생성
        /// </summary>
        /// <returns>하강 및 절삭 속도 설정 G-code 문자열</returns>
        private static string Plunge()
        {
            return $"G01 Z{CutZ.ToString("F3", CultureInfo.InvariantCulture)} F{PlungeFeedRate.ToString(CultureInfo.InvariantCulture)}\nG01 F{FeedRate.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// 공구를 안전 높이로 상승시키는 G-code 생성
        /// </summary>
        /// <returns>상승 G-code 문자열</returns>
        private static string Retract()
        {
            return $"G00 Z{SafeZ.ToString("F3", CultureInfo.InvariantCulture)}";
        }
    }
}
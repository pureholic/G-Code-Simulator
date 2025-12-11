using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GCodeSimulator.Models
{
    /// <summary>
    /// G-code 텍스트를 파싱하여 경로 데이터로 변환하는 파서 클래스
    /// </summary>
    public class GCodeParser
    {
        private Point3D currentPosition = new Point3D(0, 0, 0); // 현재 공구 위치
        private bool absoluteMode = true; // 절대 좌표 모드 (true) / 상대 좌표 모드 (false)
        private double currentFeedRate = 0; // 현재 설정된 피드 속도 (mm/min)
        private const double RapidMoveFeedRate = 5000.0; // 급속 이송을 위한 가상 피드 속도
        public List<PathSegment> Paths { get; private set; } = new List<PathSegment>(); // 파싱된 경로 세그먼트 리스트
        public double MinX { get; private set; } // X 좌표 최소값
        public double MaxX { get; private set; } // X 좌표 최대값
        public double MinY { get; private set; } // Y 좌표 최소값
        public double MaxY { get; private set; } // Y 좌표 최대값
        public double MinZ { get; private set; } // Z 좌표 최소값
        public double MaxZ { get; private set; } // Z 좌표 최대값
        public bool Is3D { get; private set; } // 3D 경로 여부

        /// <summary>
        /// G-code 텍스트를 파싱하여 경로 데이터를 생성
        /// </summary>
        /// <param name="gcodeText">파싱할 G-code 텍스트</param>
        public void Parse(string gcodeText)
        {
            Paths.Clear();
            currentPosition = new Point3D(0, 0, 0);
            absoluteMode = true;
            currentFeedRate = 0;

            MinX = MinY = MinZ = double.MaxValue;
            MaxX = MaxY = MaxZ = double.MinValue;

            var lines = gcodeText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var cleanedLine = RemoveComments(line).Trim();
                if (string.IsNullOrEmpty(cleanedLine))
                    continue;

                var command = ParseLine(cleanedLine);
                if (command != null)
                {
                    command.RawLine = line.Trim(); // 원본 라인 저장
                    ProcessCommand(command, i + 1, line.Trim()); // 라인 번호와 원본 라인 전달
                }
            }

            // 3D 판별: Z축 변화가 0.01mm 이상인 경우
            Is3D = (MaxZ - MinZ) > 0.01;
        }

        /// <summary>
        /// G-code 라인에서 주석을 제거
        /// </summary>
        /// <param name="line">주석을 제거할 라인</param>
        /// <returns>주석이 제거된 라인</returns>
        private string RemoveComments(string line)
        {
            // ; 주석 제거
            int semicolonIndex = line.IndexOf(';');
            if (semicolonIndex >= 0)
                line = line.Substring(0, semicolonIndex);

            // () 주석 제거
            line = Regex.Replace(line, @"\([^)]*\)", "");

            return line;
        }

        /// <summary>
        /// G-code 라인을 파싱하여 GCodeCommand 객체로 변환
        /// </summary>
        /// <param name="line">파싱할 G-code 라인</param>
        /// <returns>파싱된 GCodeCommand 객체</returns>
        private GCodeCommand? ParseLine(string line)
        {
            var command = new GCodeCommand { RawLine = line };

            // G 코드 추출
            var gMatch = Regex.Match(line, @"G(\d+)");
            if (gMatch.Success)
            {
                command.CommandType = "G" + gMatch.Groups[1].Value;
            }

            // 좌표 추출
            command.X = ExtractCoordinate(line, 'X');
            command.Y = ExtractCoordinate(line, 'Y');
            command.Z = ExtractCoordinate(line, 'Z');

            // 원호 파라미터
            var i = ExtractCoordinate(line, 'I');
            var j = ExtractCoordinate(line, 'J');
            var k = ExtractCoordinate(line, 'K');

            if (!double.IsNaN(i)) command.I = i;
            if (!double.IsNaN(j)) command.J = j;
            if (!double.IsNaN(k)) command.K = k;

            // 피드 속도 추출 (F 코드)
            var feedRate = ExtractCoordinate(line, 'F');
            if (!double.IsNaN(feedRate)) command.FeedRate = feedRate;

            return command;
        }

        /// <summary>
        /// G-code 라인에서 특정 축의 좌표 값을 추출
        /// </summary>
        /// <param name="line">G-code 라인</param>
        /// <param name="axis">추출할 축 (X, Y, Z, I, J, K, F 등)</param>
        /// <returns>추출된 좌표 값 (값이 없으면 NaN)</returns>
        private double ExtractCoordinate(string line, char axis)
        {
            var pattern = axis + @"([-+]?\d*\.?\d+)";
            var match = Regex.Match(line, pattern);
            if (match.Success)
            {
                return double.Parse(match.Groups[1].Value);
            }
            return double.NaN;
        }

        /// <summary>
        /// G-code 명령을 처리하여 경로 세그먼트를 생성
        /// </summary>
        /// <param name="command">처리할 GCodeCommand 객체</param>
        /// <param name="lineNumber">G-code 파일에서의 라인 번호</param>
        /// <param name="originalLine">원본 G-code 라인</param>
        private void ProcessCommand(GCodeCommand command, int lineNumber, string originalLine)
        {
            // F 코드가 있으면 현재 피드 속도 업데이트
            if (command.FeedRate.HasValue)
            {
                currentFeedRate = command.FeedRate.Value;
            }

            switch (command.CommandType)
            {
                case "G0":
                case "G00":
                    ProcessLinearMove(command, "rapid", lineNumber, originalLine);
                    break;
                case "G1":
                case "G01":
                    ProcessLinearMove(command, "linear", lineNumber, originalLine);
                    break;
                case "G2":
                case "G02":
                    ProcessArcMove(command, true, lineNumber, originalLine); // 시계방향
                    break;
                case "G3":
                case "G03":
                    ProcessArcMove(command, false, lineNumber, originalLine); // 반시계방향
                    break;
                case "G90":
                    absoluteMode = true;
                    break;
                case "G91":
                    absoluteMode = false;
                    break;
            }
        }

        /// <summary>
        /// 직선 이동(G0, G1) 명령을 처리
        /// </summary>
        /// <param name="command">G-code 명령</param>
        /// <param name="pathType">경로 타입 ("rapid" 또는 "linear")</param>
        /// <param name="lineNumber">G-code 파일에서의 라인 번호</param>
        /// <param name="originalLine">원본 G-code 라인</param>
        private void ProcessLinearMove(GCodeCommand command, string pathType, int lineNumber, string originalLine)
        {
            var targetPos = CalculateTargetPosition(command);

            // 먼저 거리 계산
            double dx = targetPos.X - currentPosition.X;
            double dy = targetPos.Y - currentPosition.Y;
            double dz = targetPos.Z - currentPosition.Z;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // 소요 시간 계산
            double durationSeconds;
            double effectiveFeedRate = currentFeedRate;

            if (pathType == "rapid")
            {
                effectiveFeedRate = RapidMoveFeedRate;
            }

            if (effectiveFeedRate > 0)
            {
                durationSeconds = (distance / effectiveFeedRate) * 60.0;
            }
            else
            {
                // 피드값이 없거나 0인 경우, 최소 시간으로 설정 (거의 즉시 이동)
                durationSeconds = distance > 0 ? 0.01 : 0;
            }

            // 시뮬레이션을 위한 점 개수 계산. 최소 2개의 점을 보장하여 선을 그림.
            int numPoints = Math.Max(2, (int)Math.Ceiling(durationSeconds / 0.016)); // 60fps 기준

            var segment = new PathSegment
            {
                PathType = pathType,
                FeedRate = effectiveFeedRate,
                Distance = distance,
                DurationSeconds = durationSeconds,
                LineNumber = lineNumber,
                OriginalGCodeLine = originalLine
            };

            // 점 생성
            for (int i = 0; i <= numPoints; i++)
            {
                double t = (double)i / numPoints;
                var point = new Point3D(
                    currentPosition.X + t * dx,
                    currentPosition.Y + t * dy,
                    currentPosition.Z + t * dz
                );
                segment.Points.Add(point);
                UpdateBounds(point);
            }

            Paths.Add(segment);
            currentPosition = targetPos;
        }

        /// <summary>
        /// 원호 이동(G2, G3) 명령을 처리
        /// </summary>
        /// <param name="command">G-code 명령</param>
        /// <param name="clockwise">시계방향 여부 (true: G2, false: G3)</param>
        /// <param name="lineNumber">G-code 파일에서의 라인 번호</param>
        /// <param name="originalLine">원본 G-code 라인</param>
        private void ProcessArcMove(GCodeCommand command, bool clockwise, int lineNumber, string originalLine)
        {
            var targetPos = CalculateTargetPosition(command);

            double i = command.I ?? 0;
            double j = command.J ?? 0;

            // 원의 중심점
            var centerX = currentPosition.X + i;
            var centerY = currentPosition.Y + j;

            // 반지름
            var radius = Math.Sqrt(i * i + j * j);

            // 시작/끝 각도
            var startAngle = Math.Atan2(currentPosition.Y - centerY, currentPosition.X - centerX);
            var endAngle = Math.Atan2(targetPos.Y - centerY, targetPos.X - centerX);

            // 각도 조정
            if (clockwise)
            {
                if (endAngle >= startAngle)
                    endAngle -= 2 * Math.PI;
            }
            else
            {
                if (endAngle <= startAngle)
                    endAngle += 2 * Math.PI;
            }

            // 원호 길이 계산
            double arcAngle = Math.Abs(endAngle - startAngle);
            double arcLength = radius * arcAngle;

            // Z축 이동 거리 추가 (3D 헬리컬 경로)
            double dz = targetPos.Z - currentPosition.Z;
            double totalDistance = Math.Sqrt(arcLength * arcLength + dz * dz);

            // 소요 시간 계산
            double durationSeconds;
            if (currentFeedRate <= 0)
                durationSeconds = 0.1;
            else
                durationSeconds = (totalDistance / currentFeedRate) * 60.0; // mm/min → 초

            // 0.1초당 1개 점 생성, 최소 2개 점 보장
            int numPoints = Math.Max(1, (int)Math.Ceiling(durationSeconds / 0.1));

            var segment = new PathSegment
            {
                PathType = "arc",
                FeedRate = currentFeedRate,
                Distance = totalDistance,
                DurationSeconds = durationSeconds,
                LineNumber = lineNumber,
                OriginalGCodeLine = originalLine
            };

            // 원호를 여러 점으로 근사
            for (int i_idx = 0; i_idx <= numPoints; i_idx++)
            {
                double t = (double)i_idx / numPoints;
                double angle = startAngle + t * (endAngle - startAngle);

                double x = centerX + radius * Math.Cos(angle);
                double y = centerY + radius * Math.Sin(angle);
                double z = currentPosition.Z + t * dz;

                var point = new Point3D(x, y, z);
                segment.Points.Add(point);
                UpdateBounds(point);
            }

            Paths.Add(segment);
            currentPosition = targetPos;
        }

        /// <summary>
        /// G-code 명령에서 목표 위치를 계산
        /// 절대 좌표 모드와 상대 좌표 모드를 고려하여 계산
        /// </summary>
        /// <param name="command">G-code 명령</param>
        /// <returns>계산된 목표 위치</returns>
        private Point3D CalculateTargetPosition(GCodeCommand command)
        {
            double x, y, z;

            if (absoluteMode)
            {
                x = double.IsNaN(command.X) ? currentPosition.X : command.X;
                y = double.IsNaN(command.Y) ? currentPosition.Y : command.Y;
                z = double.IsNaN(command.Z) ? currentPosition.Z : command.Z;
            }
            else
            {
                x = currentPosition.X + (double.IsNaN(command.X) ? 0 : command.X);
                y = currentPosition.Y + (double.IsNaN(command.Y) ? 0 : command.Y);
                z = currentPosition.Z + (double.IsNaN(command.Z) ? 0 : command.Z);
            }

            return new Point3D(x, y, z);
        }

        /// <summary>
        /// 점의 좌표를 사용하여 좌표 범위(경계)를 업데이트
        /// </summary>
        /// <param name="point">업데이트할 점</param>
        private void UpdateBounds(Point3D point)
        {
            MinX = Math.Min(MinX, point.X);
            MaxX = Math.Max(MaxX, point.X);
            MinY = Math.Min(MinY, point.Y);
            MaxY = Math.Max(MaxY, point.Y);
            MinZ = Math.Min(MinZ, point.Z);
            MaxZ = Math.Max(MaxZ, point.Z);
        }

        /// <summary>
        /// 전체 경로 세그먼트 개수를 반환
        /// </summary>
        /// <returns>경로 세그먼트 개수</returns>
        public int GetTotalSegments()
        {
            return Paths.Count;
        }
    }
}

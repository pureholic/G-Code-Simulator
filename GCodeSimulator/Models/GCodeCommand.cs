using System;
using System.Collections.Generic;

namespace GCodeSimulator.Models
{
    /// <summary>
    /// G-code 명령어를 표현하는 모델 클래스
    /// </summary>
    public class GCodeCommand
    {
        public string CommandType { get; set; } = ""; // 명령 타입 (G0, G1, G2, G3 등)
        public double X { get; set; } // X 좌표
        public double Y { get; set; } // Y 좌표
        public double Z { get; set; } // Z 좌표
        public double? I { get; set; } // 원호의 X축 중심 오프셋
        public double? J { get; set; } // 원호의 Y축 중심 오프셋
        public double? K { get; set; } // 원호의 Z축 중심 오프셋
        public double? FeedRate { get; set; }  // 이송 속도 (mm/min)
        public string RawLine { get; set; } = ""; // 원본 G-code 라인

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public GCodeCommand()
        {
        }
    }

    /// <summary>
    /// 3D 공간의 점을 표현하는 클래스
    /// </summary>
    public class Point3D
    {
        public double X { get; set; } // X 좌표
        public double Y { get; set; } // Y 좌표
        public double Z { get; set; } // Z 좌표

        /// <summary>
        /// 3D 점 생성자
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <param name="z">Z 좌표</param>
        public Point3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// 현재 점의 복사본을 생성
        /// </summary>
        /// <returns>복사된 Point3D 객체</returns>
        public Point3D Clone()
        {
            return new Point3D(X, Y, Z);
        }
    }

    /// <summary>
    /// 공구 경로의 한 구간(세그먼트)을 표현하는 클래스
    /// </summary>
    public class PathSegment
    {
        public List<Point3D> Points { get; set; } = new List<Point3D>(); // 경로를 구성하는 점들의 리스트
        public string PathType { get; set; } = ""; // 경로 타입: "rapid"(급속이송), "linear"(직선), "arc"(원호)
        public double FeedRate { get; set; } = 0; // 이송 속도 (mm/min)
        public double Distance { get; set; } = 0; // 경로의 총 거리 (mm)
        public double DurationSeconds { get; set; } = 0; // 이동 소요 시간 (초)
        public int LineNumber { get; set; } = 0; // G-code 파일에서의 라인 번호
        public string OriginalGCodeLine { get; set; } = ""; // 원본 G-code 라인

        /// <summary>
        /// 경로의 총 거리를 계산
        /// 각 점 사이의 유클리드 거리를 합산하여 전체 경로 거리를 구함
        /// </summary>
        public void CalculateDistance()
        {
            Distance = 0;
            for (int i = 0; i < Points.Count - 1; i++)
            {
                var p1 = Points[i];
                var p2 = Points[i + 1];
                var dx = p2.X - p1.X;
                var dy = p2.Y - p1.Y;
                var dz = p2.Z - p1.Z;
                Distance += Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        /// <summary>
        /// 이동 소요 시간을 계산 (FeedRate 기반)
        /// FeedRate가 mm/min 단위이므로 초 단위로 변환
        /// </summary>
        public void CalculateDuration()
        {
            if (FeedRate > 0)
            {
                // FeedRate는 mm/min이므로 초 단위로 변환
                DurationSeconds = (Distance / FeedRate) * 60.0;
            }
            else
            {
                // 피드 속도가 없으면 급속 이송으로 가정 (매우 빠름)
                DurationSeconds = 0.1; // 기본 0.1초
            }
        }
    }
}

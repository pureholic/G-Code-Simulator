using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GCodeSimulator.Models
{
    /// <summary>
    /// DWG/DXF 파일을 읽어 엔티티를 추출하는 리더 클래스
    /// </summary>
    public class DwgReader
    {
        /// <summary>
        /// DWG/DXF 파일에서 지원되는 엔티티를 읽어 리스트로 반환
        /// G-code로 변환 가능한 엔티티만 필터링
        /// </summary>
        /// <param name="filePath">읽을 DWG/DXF 파일 경로</param>
        /// <returns>추출된 DXF 엔티티 리스트</returns>
        public static List<DxfEntity> ReadEntities(string filePath)
        {
            DxfFile dxfFile;
            // 파일 스트림으로 DXF 파일 로드
            using (FileStream fs = new FileStream(filePath, FileMode.Open))
            {
                dxfFile = DxfFile.Load(fs);
            }

            if (dxfFile == null)
            {
                return new List<DxfEntity>();
            }

            // G-code로 쉽게 변환 가능한 엔티티 타입만 지원
            var supportedEntityTypes = new[]
            {
                typeof(DxfLine),        // 직선
                typeof(DxfArc),         // 원호
                typeof(DxfCircle),      // 원
                typeof(DxfLwPolyline),  // 경량 폴리라인 (가장 일반적)
                typeof(DxfPolyline)     // 폴리라인
            };

            // 지원되는 타입의 엔티티만 필터링하여 반환
            return dxfFile.Entities
                .Where(e => supportedEntityTypes.Contains(e.GetType()))
                .ToList();
        }
    }
}


using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models;

public class PdfDynamicEdit
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string ReportType { get; set; } = "";

    public int ReportId { get; set; }

    [MaxLength(30)]
    public string EditType { get; set; } = "text";

    public int PageNumber { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public string Content { get; set; } = "";
    public string TableDataJson { get; set; } = "{}";

    public float FontSize { get; set; } = 12;
    public string FontWeight { get; set; } = "regular";
    public string Alignment { get; set; } = "left";

    public bool AutoBullet { get; set; }
    public bool CoverOriginal { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


}
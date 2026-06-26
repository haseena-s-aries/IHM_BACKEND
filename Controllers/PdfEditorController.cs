using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Survey_Certification_App.Controllers;

[ApiController]

[Route("api/pdf-editor")]

public class PdfEditorController : ControllerBase

{

    private readonly ApplicationDbContext _db;

    public PdfEditorController(ApplicationDbContext db)

    {

        _db = db;

    }

    // GET api/pdf-editor/ihm/10/edits

    [HttpGet("{reportType}/{reportId:int}/edits")]

    public async Task<IActionResult> GetEdits(

        string reportType,

        int reportId)

    {

        var type = NormalizeType(reportType);

        if (type == null)

            return BadRequest("Invalid report type.");

        var edits = await _db.PdfDynamicEdits

            .AsNoTracking()

            .Where(x => x.ReportType == type && x.ReportId == reportId)

            .OrderBy(x => x.PageNumber)

            .ThenBy(x => x.Id)

            .ToListAsync();

        return Ok(edits);

    }

    // POST api/pdf-editor/ihm/10/edits

    [HttpPost("{reportType}/{reportId:int}/edits")]

    public async Task<IActionResult> SaveEdit(

        string reportType,

        int reportId,

        [FromBody] PdfEditRequest request)

    {

        var type = NormalizeType(reportType);

        if (type == null)

            return BadRequest("Invalid report type.");

        if (request.PageNumber < 1)

            return BadRequest("Invalid page number.");

        var edit = new PdfDynamicEdit

        {

            ReportType = type,

            ReportId = reportId,

            EditType = request.EditType?.Trim().ToLowerInvariant() ?? "text",

            PageNumber = request.PageNumber,

            X = Math.Clamp(request.X, 0, 1),

            Y = Math.Clamp(request.Y, 0, 1),

            Width = Math.Clamp(request.Width, 0.01, 1),

            Height = Math.Clamp(request.Height, 0.01, 1),

            Content = request.Content ?? "",

            TableDataJson = request.TableDataJson ?? "{}",

            FontSize = Math.Clamp(request.FontSize, 6, 72),

            FontWeight = request.FontWeight ?? "regular",

            Alignment = request.Alignment ?? "left",

            AutoBullet = request.AutoBullet,

            CoverOriginal = request.CoverOriginal,

            CreatedAt = DateTime.UtcNow

        };

        _db.PdfDynamicEdits.Add(edit);

        await _db.SaveChangesAsync();

        return Ok(edit);

    }

    // PUT api/pdf-editor/ihm/10/edits/5

    [HttpPut("{reportType}/{reportId:int}/edits/{editId:int}")]

    public async Task<IActionResult> UpdateEdit(

        string reportType,

        int reportId,

        int editId,

        [FromBody] PdfEditRequest request)

    {

        var type = NormalizeType(reportType);

        if (type == null)

            return BadRequest("Invalid report type.");

        var edit = await _db.PdfDynamicEdits.FirstOrDefaultAsync(x =>

            x.Id == editId &&

            x.ReportType == type &&

            x.ReportId == reportId);

        if (edit == null)

            return NotFound("PDF edit not found.");

        edit.EditType = request.EditType?.Trim().ToLowerInvariant() ?? "text";

        edit.PageNumber = request.PageNumber;

        edit.X = Math.Clamp(request.X, 0, 1);

        edit.Y = Math.Clamp(request.Y, 0, 1);

        edit.Width = Math.Clamp(request.Width, 0.01, 1);

        edit.Height = Math.Clamp(request.Height, 0.01, 1);

        edit.Content = request.Content ?? "";

        edit.TableDataJson = request.TableDataJson ?? "{}";

        edit.FontSize = Math.Clamp(request.FontSize, 6, 72);

        edit.FontWeight = request.FontWeight ?? "regular";

        edit.Alignment = request.Alignment ?? "left";

        edit.AutoBullet = request.AutoBullet;

        edit.CoverOriginal = request.CoverOriginal;

        await _db.SaveChangesAsync();

        return Ok(edit);

    }

    // DELETE api/pdf-editor/ihm/10/edits/5

    [HttpDelete("{reportType}/{reportId:int}/edits/{editId:int}")]

    public async Task<IActionResult> DeleteEdit(

        string reportType,

        int reportId,

        int editId)

    {

        var type = NormalizeType(reportType);

        if (type == null)

            return BadRequest("Invalid report type.");

        var edit = await _db.PdfDynamicEdits.FirstOrDefaultAsync(x =>

            x.Id == editId &&

            x.ReportType == type &&

            x.ReportId == reportId);

        if (edit == null)

            return NotFound("PDF edit not found.");

        _db.PdfDynamicEdits.Remove(edit);

        await _db.SaveChangesAsync();

        return Ok(new { message = "PDF edit deleted." });

    }

    // DELETE api/pdf-editor/ihm/10/edits

    [HttpDelete("{reportType}/{reportId:int}/edits")]

    public async Task<IActionResult> DeleteAllEdits(

        string reportType,

        int reportId)

    {

        var type = NormalizeType(reportType);

        if (type == null)

            return BadRequest("Invalid report type.");

        var edits = await _db.PdfDynamicEdits

            .Where(x => x.ReportType == type && x.ReportId == reportId)

            .ToListAsync();

        _db.PdfDynamicEdits.RemoveRange(edits);

        await _db.SaveChangesAsync();

        return Ok(new { message = "All PDF edits deleted." });

    }

    [HttpPost("{reportType}/{reportId:int}/regenerate")]

    public async Task<IActionResult> RegeneratePdf(

    string reportType,

    int reportId)

    {

        var type = NormalizeType(reportType);

        if (type != "ihm")

            return BadRequest(

                "Advanced pagination is currently supported for IHM reports only.");

        var sourcePath = await GetPdfPath(type, reportId);

        if (string.IsNullOrWhiteSpace(sourcePath) ||

            !System.IO.File.Exists(sourcePath))

            return NotFound("Original PDF was not found.");

        var reportInputPath = Path.Combine(

            Path.GetDirectoryName(sourcePath)!,

            "ihm-report-input.json");

        if (!System.IO.File.Exists(reportInputPath))

            return NotFound("IHM report input JSON was not found.");

        var edits = await _db.PdfDynamicEdits

            .AsNoTracking()

            .Where(x =>

                x.ReportType == type &&

                x.ReportId == reportId)

            .OrderBy(x => x.Id)

            .ToListAsync();

        var outputFolder = Path.GetDirectoryName(sourcePath)!;

        var editsPath = Path.Combine(

            outputFolder,

            "pdf-dynamic-edits.json");

        var tempOutput = Path.Combine(

            outputFolder,

            $"{Guid.NewGuid():N}-edited.pdf");

        await System.IO.File.WriteAllTextAsync(

            editsPath,

            JsonSerializer.Serialize(

                edits,

                new JsonSerializerOptions

                {

                    WriteIndented = true,

                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase

                }));

        var scriptPath = Path.Combine(

            Directory.GetCurrentDirectory(),

            "PythonServices",

            "apply_pdf_dynamic_edits.py");

        if (!System.IO.File.Exists(scriptPath))

            return StatusCode(

                500,

                "Dynamic PDF editor Python script was not found.");

        var venvPython = Path.Combine(

            Directory.GetCurrentDirectory(),

            ".venv",

            "Scripts",

            "python.exe");

        var pythonExe = System.IO.File.Exists(venvPython)

            ? venvPython

            : "python";

        var startInfo = new ProcessStartInfo

        {

            FileName = pythonExe,

            Arguments =

                $"\"{scriptPath}\" " +

                $"\"{sourcePath}\" " +

                $"\"{editsPath}\" " +

                $"\"{reportInputPath}\" " +

                $"\"{tempOutput}\"",

            RedirectStandardOutput = true,

            RedirectStandardError = true,

            UseShellExecute = false,

            CreateNoWindow = true

        };

        using var process = Process.Start(startInfo);

        if (process == null)

            return StatusCode(

                500,

                "Unable to start the PDF editor.");

        var output = await process.StandardOutput.ReadToEndAsync();

        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0 ||

            !System.IO.File.Exists(tempOutput))

        {

            var message = string.IsNullOrWhiteSpace(error)

                ? output

                : error;

            return StatusCode(

                500,

                $"PDF regeneration failed: {message}");

        }

        System.IO.File.Move(

            tempOutput,

            sourcePath,

            overwrite: true);

        return Ok(new

        {

            message = "PDF updated successfully.",

            filePath = $"/reports/ihm/{reportId}/ihm-report.pdf"

        });

    }

    private async Task<string?> GetPdfPath(string type, int reportId)

    {

        if (type == "ihm")

        {

            return Path.Combine(

                Directory.GetCurrentDirectory(),

                "wwwroot",

                "reports",

                "ihm",

                reportId.ToString(),

                "ihm-report.pdf");

        }

        if (type == "towage")

        {

            return await _db.certificates

                .Where(x => x.Id == reportId)

                .Select(x => x.PdfFilePath)

                .FirstOrDefaultAsync();

        }

        if (type == "seafastening")

        {

            return await _db.SeaFastenings

                .Where(x => x.Id == reportId)

                .Select(x => x.PdfFilePath)

                .FirstOrDefaultAsync();

        }

        return await _db.loadoutCertificates

            .Where(x => x.Id == reportId)

            .Select(x => x.PdfFilePath)

            .FirstOrDefaultAsync();

    }

    private static string? NormalizeType(string? value)

    {

        var type = value?.Trim().ToLowerInvariant();

        return type is "ihm" or "towage" or "seafastening" or "loadout"

            ? type

            : null;

    }

}

public class PdfEditRequest

{

    public string? EditType { get; set; } = "text";

    public int PageNumber { get; set; } = 1;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 0.25;

    public double Height { get; set; } = 0.04;

    public string? Content { get; set; }

    public string? TableDataJson { get; set; } = "{}";

    public float FontSize { get; set; } = 12;

    public string? FontWeight { get; set; } = "regular";

    public string? Alignment { get; set; } = "left";

    public bool AutoBullet { get; set; }

    public bool CoverOriginal { get; set; } = true;

}

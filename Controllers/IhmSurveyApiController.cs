using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models.Survey;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Survey_Certification_App.Services;

namespace Survey_Certification_App.Controllers
{
    [Route("api/ihm-survey")]
    [ApiController]
    public class IhmSurveyApiController : ControllerBase
    {
        private static readonly ConcurrentDictionary<int, IhmPdfJobState> IhmPdfJobs = new();

        private sealed class IhmPdfJobState
        {
            public string Status { get; set; } = "processing";
            public string? Error { get; set; }
            public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
            public DateTime? FinishedUtc { get; set; }
            public string? FilePath { get; set; }
        }
        private readonly ApplicationDbContext _db;
        private readonly AccessScopeService _access;
        public IhmSurveyApiController(ApplicationDbContext db, AccessScopeService access)
        {
            _db = db;
            _access = access;
        }

        [HttpGet("jobs/{jobId:int}/areas")]
        public async Task<IActionResult> GetAreas(int jobId)
        {
            var areas = await _db.IhmAreas
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.SortOrder)
                .Select(x => ToAreaDto(x))
                .ToListAsync();

            return Ok(areas);
        }

        [HttpPost("jobs/{jobId:int}/areas")]
        public async Task<IActionResult> CreateArea(
            int jobId,
            [FromBody] CreateIhmAreaRequest request)
        {
            var jobExists = await CanAccessIhmJobAsync(jobId);

            if (!jobExists)
                return NotFound("Survey job not found.");

            var area = new IhmArea
            {
                SurveyJobId = jobId,
                AreaName = request.AreaName,
                Description = request.Description,
                SortOrder = request.SortOrder
            };

            _db.IhmAreas.Add(area);
            await _db.SaveChangesAsync();

            return Ok(ToAreaDto(area));
        }

        [HttpGet("areas/{areaId:int}/checkpoints")]
        public async Task<IActionResult> GetCheckpoints(int areaId)
        {
            var checkpoints = await _db.IhmCheckpoints
                .Where(x => x.IhmAreaId == areaId)
                .OrderBy(x => x.Sample_No)
                .Select(x => ToCheckpointDto(x))
                .ToListAsync();

            return Ok(checkpoints);
        }

        [HttpPost("areas/{areaId:int}/checkpoints")]
        public async Task<IActionResult> CreateCheckpoint(
            int areaId,
            [FromBody] CreateIhmCheckpointRequest request)
        {
            var area = await _db.IhmAreas.FirstOrDefaultAsync(x => x.Id == areaId);

            if (area == null)
                return NotFound("IHM area not found.");

            var checkpoint = new IhmCheckpoint
            {
                SurveyJobId = area.SurveyJobId,
                IhmAreaId = area.Id,

                Sample_No = request.CheckpointCode,
                Description = request.Description,

                TableType = request.TableType,
                Location = request.Location,
                EquipmentName = request.EquipmentName,
                Component = request.Component,

                Table_A_B = request.Table_A_B,
                HazardousMaterial = request.HazardousMaterial,

                QuantityUnitKg = request.QuantityUnitKg,
                QuantityNo = request.QuantityNo,
                QuantityTotalKg = request.QuantityTotalKg,

                ManufacturerBrandName = request.ManufacturerBrandName,
                DocumentAnalysisResult = request.DocumentAnalysisResult,
                SamplingCheck = request.SamplingCheck,
                VisualCheck = request.VisualCheck,
                CheckResult = request.CheckResult,
                Remarks = request.Remarks,

                SourceRowNumber = request.SourceRowNumber,
                Result = IhmCheckpointResult.Pending
            };

            _db.IhmCheckpoints.Add(checkpoint);
            await _db.SaveChangesAsync();

            return Ok(ToCheckpointDto(checkpoint));
        }

        [HttpPut("checkpoints/{checkpointId:int}/result")]
        public async Task<IActionResult> UpdateCheckpointResult(
            int checkpointId,
            [FromBody] UpdateIhmCheckpointResultRequest request)
        {
            var checkpoint = await _db.IhmCheckpoints.FindAsync(checkpointId);

            if (checkpoint == null)
                return NotFound("IHM checkpoint not found.");

            checkpoint.Result = request.Result;
            checkpoint.Remarks = request.Remarks;
            checkpoint.UpdatedByUserId = request.UpdatedByUserId ?? GetCurrentUserId();
            checkpoint.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(ToCheckpointDto(checkpoint));
        }


        [HttpPost("markers")]
        public async Task<IActionResult> CreateMarker([FromBody] CreateIhmMarkerRequest request)
        {
            var checkpoint = request.IhmCheckpointId.HasValue
                ? await _db.IhmCheckpoints.FirstOrDefaultAsync(x =>
                    x.Id == request.IhmCheckpointId.Value &&
                    x.SurveyJobId == request.SurveyJobId &&
                    x.IhmAreaId == request.IhmAreaId)
                : null;

            if (request.IhmCheckpointId.HasValue && checkpoint == null)
                return BadRequest("Checkpoint does not belong to the selected job and area.");

            if (string.IsNullOrWhiteSpace(request.ImagePath))
                return BadRequest("Layout image path is required.");

            var markerLabel = checkpoint == null
                ? request.MarkerLabel
                : await GetMarkerLabelForCheckpointAsync(checkpoint);

            if (request.XPercent < 0 || request.XPercent > 1 || request.YPercent < 0 || request.YPercent > 1)
                return BadRequest("Marker coordinates must be normalized values between 0 and 1.");

            var normalizedImagePath = NormalizeMarkerPath(request.ImagePath);
            var marker = request.IhmCheckpointId.HasValue
    ? await _db.IhmImageMarkers.FirstOrDefaultAsync(x =>
        x.SurveyJobId == request.SurveyJobId &&
        x.IhmAreaId == request.IhmAreaId &&
        x.IhmCheckpointId == request.IhmCheckpointId)
    : null;

            if (marker == null)
            {
                marker = new IhmImageMarker
                {
                    SurveyJobId = request.SurveyJobId,
                    IhmAreaId = request.IhmAreaId,
                    IhmCheckpointId = request.IhmCheckpointId,
                    ImagePath = normalizedImagePath,
                    Notes = request.Notes,
                    Source = request.Source,
                    SuppressRender = request.SuppressRender,
                    IsManualForExistingImage = request.IsManualForExistingImage,
                    CreatedByUserId = request.CreatedByUserId ?? GetCurrentUserId() ?? 0,
                    CreatedAt = DateTime.UtcNow
                };
                _db.IhmImageMarkers.Add(marker);
            }

            marker.ImagePath = normalizedImagePath;
            marker.XPercent = request.XPercent;
            marker.YPercent = request.YPercent;
            marker.MarkerLabel = markerLabel;
            marker.Notes = request.Notes;
            marker.Source = request.Source;
            marker.SuppressRender = request.SuppressRender;
            marker.IsManualForExistingImage = request.IsManualForExistingImage;

            await _db.SaveChangesAsync();

            if (request.IhmCheckpointId.HasValue)
            {
                var duplicateMarkers = await _db.IhmImageMarkers
                    .Where(x =>
                        x.Id != marker.Id &&
                        x.SurveyJobId == request.SurveyJobId &&
                        x.IhmAreaId == request.IhmAreaId &&
                        x.IhmCheckpointId == request.IhmCheckpointId)
                    .ToListAsync();

                if (duplicateMarkers.Count > 0)
                {
                    _db.IhmImageMarkers.RemoveRange(duplicateMarkers);
                    await _db.SaveChangesAsync();
                }
            }

            return Ok(ToMarkerDto(marker));
        }
        [HttpPost("jobs/{jobId:int}/layouts/auto-map-markers")]
        public async Task<IActionResult> AutoMapLayoutMarkers(
    int jobId,
    [FromBody] AutoMapLayoutMarkersRequest request)
        {
            if (request.AreaId <= 0)
                return BadRequest("AreaId is required.");

            if (string.IsNullOrWhiteSpace(request.ImagePath))
                return BadRequest("ImagePath is required.");

            var normalizedImagePath = NormalizeMarkerPath(request.ImagePath);
            var physicalImagePath = ResolveWebPath(normalizedImagePath);

            if (!System.IO.File.Exists(physicalImagePath))
                return NotFound($"Layout image not found: {normalizedImagePath}");

            var checkpoints = await _db.IhmCheckpoints
                .Where(x => x.SurveyJobId == jobId)
                .Where(x => !string.IsNullOrWhiteSpace(x.Sample_No))
                .Where(x => x.CheckResult != null && x.CheckResult.Trim().ToUpper() == "YES")
                .OrderBy(x => x.SourceRowNumber)
                .ThenBy(x => x.Sample_No)
                .ToListAsync();

            if (checkpoints.Count == 0)
                return BadRequest("No I-2/sample checkpoint records found for this area.");

            var regions = await DetectLayoutMarkerRegionsAsync(physicalImagePath);

            if (regions.Count == 0)
            {
                return Ok(new
                {
                    mapped = 0,
                    message = "No marker regions detected. Manual mapping is required.",
                    regions,
                    samples = checkpoints.Select(x => new
                    {
                        checkpointId = x.Id,
                        sampleNo = x.Sample_No
                    })
                });
            }

            var expectedByCode = checkpoints
    .Where(x => !string.IsNullOrWhiteSpace(x.Sample_No))
    .ToDictionary(
        x => NormalizeMarkerCode(x.Sample_No!),
        x => x
    );

            var matchedRegions = regions
                .Where(r => !string.IsNullOrWhiteSpace(r.Text))
                .Select(r => new
                {
                    Region = r,
                    Code = NormalizeMarkerCode(r.Text!)
                })
                .Where(x => expectedByCode.ContainsKey(x.Code))
                .GroupBy(x => x.Code)
                .Select(g => g.OrderByDescending(x => x.Region.Confidence).First())
                .ToList();

            if (matchedRegions.Count == 0)
            {
                return Ok(new
                {
                    mapped = 0,
                    detected = regions.Count,
                    expected = checkpoints.Count,
                    message = "No detected marker text matched I-2 sample/visual numbers.",
                    regions,
                    samples = checkpoints.Select(x => new
                    {
                        checkpointId = x.Id,
                        sampleNo = x.Sample_No
                    }),
                    needsReview = true
                });
            }

            foreach (var matched in matchedRegions)
            {
                var checkpoint = expectedByCode[matched.Code];
                var region = matched.Region;

                var marker = await _db.IhmImageMarkers.FirstOrDefaultAsync(x =>
                    x.SurveyJobId == jobId &&
                    x.IhmAreaId == request.AreaId &&
                    x.IhmCheckpointId == checkpoint.Id);

                if (marker == null)
                {
                    marker = new IhmImageMarker
                    {
                        SurveyJobId = jobId,
                        IhmAreaId = request.AreaId,
                        IhmCheckpointId = checkpoint.Id,
                        CreatedByUserId = request.UserId ?? GetCurrentUserId() ?? 0,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.IhmImageMarkers.Add(marker);
                }

                marker.ImagePath = normalizedImagePath;
                marker.MarkerLabel = checkpoint.Sample_No;
                marker.XPercent = Math.Clamp(region.XPercent, 0, 1);
                marker.YPercent = Math.Clamp(region.YPercent, 0, 1);

                // OCR coordinates point to an existing label already printed in the uploaded image.
                // This marker is a checkpoint-to-existing-label mapping only; renderers must not draw it again.
                marker.Notes = "ocr-existing-label";
                marker.Source = "existing-image";
                marker.SuppressRender = false;
                marker.IsManualForExistingImage = false;
            }

            await _db.SaveChangesAsync();

            var savedMarkers = await _db.IhmImageMarkers
                .Where(x =>
                    x.SurveyJobId == jobId &&
                    x.IhmAreaId == request.AreaId &&
                    x.ImagePath == normalizedImagePath)
                .Select(x => ToMarkerDto(x))
                .ToListAsync();

            return Ok(new
            {
                mapped = matchedRegions.Count,
                detected = regions.Count,
                expected = checkpoints.Count,
                markers = savedMarkers,
                message = $"{matchedRegions.Count} marker text(s) matched and mapped.",
                needsReview = matchedRegions.Count != checkpoints.Count
            });
        }

        [HttpGet("jobs/{jobId:int}/markers")]
        public async Task<IActionResult> GetMarkers(int jobId)
        {
            await NormalizeMarkerLabelsAsync(jobId);

            var markers = await _db.IhmImageMarkers
                .Where(x => x.SurveyJobId == jobId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => ToMarkerDto(x))
                .ToListAsync();

            return Ok(markers);
        }

        private async Task<string?> GetMarkerLabelForCheckpointAsync(
            IhmCheckpoint checkpoint)
        {
            var markerType = GetMarkerType(checkpoint);
            if (markerType == null)
                return null;

            var checkpointCode = checkpoint.Sample_No?.Trim();

            if (markerType == "sample" && !string.IsNullOrWhiteSpace(checkpointCode))
                return checkpointCode;

            if (markerType == "visual" &&
                !string.IsNullOrWhiteSpace(checkpointCode) &&
                checkpointCode.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                return checkpointCode;
            }

            var visualCheckpointIds = await _db.IhmCheckpoints
                .Where(x =>
                    x.SurveyJobId == checkpoint.SurveyJobId &&
                    x.IhmAreaId == checkpoint.IhmAreaId &&
                    x.VisualCheck != null &&
                    x.VisualCheck.Trim().ToUpper() == "V")
                .OrderBy(x => x.SourceRowNumber ?? int.MaxValue)
                .ThenBy(x => x.Sample_No)
                .ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            var index = visualCheckpointIds.IndexOf(checkpoint.Id);
            return index >= 0 ? $"V{index + 1}" : checkpointCode;
        }

        private async Task NormalizeMarkerLabelsAsync(int jobId)
        {
            var markers = await _db.IhmImageMarkers
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .ToListAsync();

            var checkpointIds = markers
                .Where(x => x.IhmCheckpointId.HasValue)
                .Select(x => x.IhmCheckpointId!.Value)
                .Distinct()
                .ToList();

            if (checkpointIds.Count == 0)
                return;

            var checkpoints = await _db.IhmCheckpoints
                .Where(x => checkpointIds.Contains(x.Id))
                .ToListAsync();

            var checkpointById = checkpoints.ToDictionary(x => x.Id);
            var changed = false;

            foreach (var marker in markers)
            {
                if (!marker.IhmCheckpointId.HasValue ||
                    !checkpointById.TryGetValue(
                        marker.IhmCheckpointId.Value,
                        out var checkpoint))
                {
                    continue;
                }

                var expectedLabel = await GetMarkerLabelForCheckpointAsync(checkpoint);

                if (marker.MarkerLabel == expectedLabel)
                    continue;

                marker.MarkerLabel = expectedLabel;
                changed = true;
            }

            if (changed)
                await _db.SaveChangesAsync();
        }
        private static string NormalizeMarkerCode(string value)
        {
            var text = (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "");

            text = text.Replace("O", "0");

            if (text.Length >= 2 && (text[0] == 'S' || text[0] == 'V'))
            {
                var digits = new string(text.Skip(1).Where(char.IsDigit).ToArray());

                if (int.TryParse(digits, out var number))
                    return $"{text[0]}{number:00}";
            }

            return text;
        }
        private static string NormalizeMarkerPath(string path)
        {
            return (path ?? string.Empty)
                .Trim()
                .Replace("\\", "/")
                .TrimStart('/')
                .ToLowerInvariant();
        }

        private string ResolveWebPath(string webPath)
        {
            var cleanPath = (webPath ?? string.Empty)
                .Replace("\\", "/")
                .TrimStart('/');

            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString())
            );
        }

        private async Task<List<DetectedLayoutMarkerRegionDto>> DetectLayoutMarkerRegionsAsync(string physicalImagePath)
        {
            var scriptPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "PythonServices",
                "detect_layout_marker_regions.py"
            );

            if (!System.IO.File.Exists(scriptPath))
                return new List<DetectedLayoutMarkerRegionDto>();

            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{physicalImagePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return new List<DetectedLayoutMarkerRegionDto>();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            var completed = await Task.Run(() => process.WaitForExit(30000));
            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                Console.WriteLine("[IHM] marker region detection timed out.");
                return new List<DetectedLayoutMarkerRegionDto>();
            }

            if (process.ExitCode != 0)
            {
                Console.WriteLine("[IHM] marker region detection failed: " + error);
                return new List<DetectedLayoutMarkerRegionDto>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<DetectedLayoutMarkerRegionDto>>(
                    output,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? new List<DetectedLayoutMarkerRegionDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[IHM] marker region detection parse failed: " + ex.Message);
                return new List<DetectedLayoutMarkerRegionDto>();
            }
        }
        private static string? GetMarkerType(IhmCheckpoint checkpoint) =>
            GetMarkerType(checkpoint.SamplingCheck, checkpoint.VisualCheck);

        private static string? GetMarkerType(
            string? samplingCheck,
            string? visualCheck)
        {
            if (string.Equals(
                samplingCheck?.Trim(),
                "S",
                StringComparison.OrdinalIgnoreCase))
            {
                return "sample";
            }

            if (string.Equals(
                visualCheck?.Trim(),
                "V",
                StringComparison.OrdinalIgnoreCase))
            {
                return "visual";
            }

            return null;
        }
        [HttpPost("jobs/{jobId:int}/import-excel")]
        public async Task<IActionResult> ImportExcel(int jobId, IFormFile file)
        {
            try
            {
                var jobExists = await CanAccessIhmJobAsync(jobId);

                if (!jobExists)
                    return NotFound("Survey job not found.");

                if (file == null || file.Length == 0)
                    return BadRequest("Excel file is required.");

                var extension = Path.GetExtension(file.FileName).ToLower();

                if (extension == ".xls")
                    return BadRequest("Old .xls files are not supported. Please save the Excel file as .xlsx and upload again.");

                if (extension != ".xlsx")
                    return BadRequest("Only .xlsx Excel files are allowed.");

                var uploadFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "ihm-excel"
            );

                Directory.CreateDirectory(uploadFolder);

                var storedFileName = $"{jobId}_{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadFolder, storedFileName);

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var parserPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "PythonServices",
                    "parse_ihm_excel.py"
                );

                if (!System.IO.File.Exists(parserPath))
                    return StatusCode(500, "Python parser file not found.");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{parserPath}\" \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);

                if (process == null)
                    return StatusCode(500, "Unable to start Python parser.");

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    return StatusCode(500, string.IsNullOrWhiteSpace(error) ? output : error);

                var parsed = JsonSerializer.Deserialize<IhmExcelParseResult>(
                    output,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

                if (parsed == null || !parsed.Success)
                    return BadRequest(parsed?.Message ?? "Unable to parse Excel file.");

                if (parsed.Areas == null || parsed.Areas.Count == 0 || parsed.Areas.Sum(x => x.Checkpoints.Count) == 0)
                    return BadRequest("Invalid Excel format. Please upload the VSCP Checklist Template file.");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var oldMarkers = await _db.IhmImageMarkers
                    .Where(x => x.SurveyJobId == jobId)
                    .ToListAsync();

                var oldCheckpoints = await _db.IhmCheckpoints
                    .Where(x => x.SurveyJobId == jobId)
                    .ToListAsync();

                var oldAreas = await _db.IhmAreas
                    .Where(x => x.SurveyJobId == jobId)
                    .ToListAsync();

                var oldEquipmentRows = await _db.IhmEquipmentMachineryReportRows
                    .Where(x => x.SurveyJobId == jobId && x.IhmCheckpointId.HasValue)
                    .ToListAsync();

                foreach (var row in oldEquipmentRows)
                {
                    row.IhmCheckpointId = null;
                }

                var oldSamplingPhotos = await _db.IhmSamplingPhotos
                    .Where(x => x.SurveyJobId == jobId &&
                                (x.IhmCheckpointId.HasValue || x.IhmAreaId.HasValue))
                    .ToListAsync();

                foreach (var photo in oldSamplingPhotos)
                {
                    photo.IhmCheckpointId = null;
                    photo.IhmAreaId = null;
                }

                _db.IhmImageMarkers.RemoveRange(oldMarkers);
                _db.IhmCheckpoints.RemoveRange(oldCheckpoints);
                _db.IhmAreas.RemoveRange(oldAreas);

                await _db.SaveChangesAsync();

                foreach (var areaDto in parsed.Areas)
                {
                    var area = new IhmArea
                    {
                        SurveyJobId = jobId,
                        AreaName = areaDto.AreaName,
                        Description = areaDto.Description,
                        SortOrder = areaDto.SortOrder
                    };

                    _db.IhmAreas.Add(area);
                    await _db.SaveChangesAsync();

                    foreach (var checkpointDto in areaDto.Checkpoints)
                    {
                        var checkpoint = new IhmCheckpoint
                        {
                            SurveyJobId = jobId,
                            IhmAreaId = area.Id,

                            Sample_No = checkpointDto.CheckpointCode,
                            Description = checkpointDto.Description,

                            TableType = checkpointDto.TableType,
                            Location = checkpointDto.Location,
                            EquipmentName = checkpointDto.EquipmentName,
                            Component = checkpointDto.Component,

                            Table_A_B = checkpointDto.MaterialCategory,
                            HazardousMaterial = checkpointDto.HazardousMaterial,

                            QuantityUnitKg = checkpointDto.QuantityUnitKg,
                            QuantityNo = checkpointDto.QuantityNo,
                            QuantityTotalKg = checkpointDto.QuantityTotalKg,

                            ManufacturerBrandName = checkpointDto.ManufacturerBrandName,
                            DocumentAnalysisResult = checkpointDto.DocumentAnalysisResult,
                            SamplingCheck = checkpointDto.SamplingCheck,
                            VisualCheck = checkpointDto.VisualCheck,
                            CheckResult = checkpointDto.CheckResult,
                            Remarks = checkpointDto.Remarks,

                            SourceRowNumber = checkpointDto.SourceRowNumber,
                            Result = IhmCheckpointResult.Pending
                        };

                        _db.IhmCheckpoints.Add(checkpoint);
                    }

                    await _db.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                return Ok(new
                {
                    message = "IHM Excel imported and saved as job-wise table data.",
                    jobId,
                    excelFile = $"/uploads/ihm-excel/{storedFileName}",
                    areaCount = parsed.Areas.Count,
                    checkpointCount = parsed.Areas.Sum(x => x.Checkpoints.Count)
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("========================================");
                Console.WriteLine("IMPORT EXCEL EXCEPTION");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("========================================");

                return StatusCode(500, new
                {
                    Message = ex.Message,
                    InnerException = ex.InnerException?.Message,
                    StackTrace = ex.ToString()
                });
            }
        }



        [HttpPut("checkpoints/{checkpointId:int}/procedure")]
        public async Task<IActionResult> UpdateCheckpointProcedure(
            int checkpointId,
            [FromBody] UpdateIhmCheckpointProcedureRequest request)
        {
            var checkpoint = await _db.IhmCheckpoints.FindAsync(checkpointId);

            if (checkpoint == null)
                return NotFound("IHM checkpoint not found.");

            var type = request.ProcedureType.Trim().ToLower();

            if (type == "visual")
            {
                checkpoint.VisualCheck = "V";
                checkpoint.SamplingCheck = "-";
            }
            else if (type == "sample")
            {
                checkpoint.SamplingCheck = "S";
                checkpoint.VisualCheck = "-";
            }
            else
            {
                return BadRequest("ProcedureType must be Visual or Sample.");
            }

            checkpoint.UpdatedByUserId = request.UpdatedByUserId ?? GetCurrentUserId();
            checkpoint.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(ToCheckpointDto(checkpoint));
        }

        [HttpGet("checkpoints/{checkpointId:int}/photos")]
        public async Task<IActionResult> GetCheckpointPhotos(int checkpointId)
        {
            var photos = await _db.IhmSamplingPhotos
                .Where(x => x.IhmCheckpointId == checkpointId)
                .OrderByDescending(x => x.UploadedAt)
                .Select(x => ToSamplingPhotoDto(x))
                .ToListAsync();

            return Ok(photos);
        }

        [HttpPost("jobs/{jobId:int}/checkpoints/{checkpointId:int}/photos")]
        public async Task<IActionResult> UploadCheckpointPhoto(
            int jobId,
            int checkpointId,
            IFormFile? file,
            List<IFormFile>? files,
            [FromForm] string? caption,
            [FromForm] int? uploadedByUserId)
        {
            var checkpoint = await _db.IhmCheckpoints
                .FirstOrDefaultAsync(x => x.Id == checkpointId && x.SurveyJobId == jobId);

            if (checkpoint == null)
                return NotFound("IHM checkpoint not found.");

            var uploadFiles = new List<IFormFile>();

            if (files != null)
                uploadFiles.AddRange(files.Where(x => x != null && x.Length > 0));

            if (file != null && file.Length > 0)
                uploadFiles.Add(file);

            if (uploadFiles.Count == 0)
                return BadRequest("Image file is required.");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            var uploadFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "ihm-sampling",
                jobId.ToString(),
                checkpointId.ToString()
            );

            Directory.CreateDirectory(uploadFolder);

            var uploadedPhotos = new List<IhmSamplingPhoto>();

            foreach (var uploadFile in uploadFiles)
            {
                var extension = Path.GetExtension(uploadFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                    return BadRequest("Only image files are allowed.");

                var storedFileName = $"{Guid.NewGuid()}.jpg";
                var filePath = Path.Combine(uploadFolder, storedFileName);

                await SaveCompressedPhotoAsync(uploadFile, filePath);

                var relativePath = $"/uploads/ihm-sampling/{jobId}/{checkpointId}/{storedFileName}";

                uploadedPhotos.Add(new IhmSamplingPhoto
                {
                    SurveyJobId = jobId,
                    IhmAreaId = checkpoint.IhmAreaId,
                    IhmCheckpointId = checkpoint.Id,
                    OriginalFileName = uploadFile.FileName,
                    StoredFileName = storedFileName,
                    FilePath = relativePath,
                    ContentType = "image/jpeg",
                    Caption = caption,
                    UploadedByUserId = uploadedByUserId ?? GetCurrentUserId() ?? 0,
                    UploadedAt = DateTime.UtcNow
                });
            }

            _db.IhmSamplingPhotos.AddRange(uploadedPhotos);
            await _db.SaveChangesAsync();

            if (uploadedPhotos.Count == 1)
                return Ok(ToSamplingPhotoDto(uploadedPhotos[0]));

            return Ok(uploadedPhotos.Select(ToSamplingPhotoDto).ToList());
        }
        [HttpDelete("sampling-photos/{photoId:int}")]
        public async Task<IActionResult> DeleteSamplingPhoto(int photoId)
        {
            var photo = await _db.IhmSamplingPhotos.FindAsync(photoId);

            if (photo == null)
                return NotFound("Sampling photo not found.");

            var absolutePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                photo.FilePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)
            );

            if (System.IO.File.Exists(absolutePath))
                System.IO.File.Delete(absolutePath);

            _db.IhmSamplingPhotos.Remove(photo);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Sampling photo deleted.", id = photoId });
        }


        public class UpdateIhmCheckpointProcedureRequest
        {
            public string ProcedureType { get; set; } = string.Empty;
            public int? UpdatedByUserId { get; set; }
        }

        [HttpGet("jobs/{jobId:int}/areas-with-checkpoints")]
        public async Task<IActionResult> GetAreasWithCheckpoints(int jobId)
        {
            var areas = await _db.IhmAreas
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.SortOrder)
                .Select(area => new
                {
                    area.Id,
                    area.SurveyJobId,
                    area.AreaName,
                    area.Description,
                    area.SortOrder,

                    Checkpoints = _db.IhmCheckpoints
                        .Where(cp => cp.IhmAreaId == area.Id)
                        .OrderBy(cp => cp.Sample_No)
                        .Select(cp => new
                        {
                            cp.Id,
                            cp.SurveyJobId,
                            cp.IhmAreaId,

                            cp.Sample_No,
                            SampleNo = cp.Sample_No,
                            cp.Description,

                            cp.TableType,
                            cp.Location,
                            cp.EquipmentName,
                            cp.Component,

                            cp.Table_A_B,
                            cp.HazardousMaterial,

                            cp.QuantityUnitKg,
                            cp.QuantityNo,
                            cp.QuantityTotalKg,

                            cp.ManufacturerBrandName,
                            cp.DocumentAnalysisResult,

                            cp.SamplingCheck,
                            cp.VisualCheck,
                            cp.CheckResult,

                            Result = cp.Result.ToString(),
                            cp.Remarks,
                            cp.IsPchm,
                            cp.LocationRemarks,
                            cp.SourceRowNumber

                        })
                        .ToList()
                })
                .ToListAsync();

            return Ok(areas);
        }



        [HttpPut("checkpoints/{checkpointId:int}/details")]
        public async Task<IActionResult> UpdateCheckpointDetails(
            int checkpointId,
            [FromBody] UpdateIhmCheckpointDetailsRequest request)
        {
            var checkpoint = await _db.IhmCheckpoints.FindAsync(checkpointId);

            if (checkpoint == null)
                return NotFound("IHM checkpoint not found.");

            checkpoint.IsPchm = request.IsPchm;
            checkpoint.Sample_No = request.SampleNo.Trim();
            checkpoint.Description = request.Description.Trim();
            checkpoint.TableType = request.TableType;
            checkpoint.Location = request.Location;
            checkpoint.EquipmentName = request.EquipmentName;
            checkpoint.Component = request.Component;
            checkpoint.Table_A_B = request.MaterialCategory;
            checkpoint.HazardousMaterial = request.HazardousMaterial;
            checkpoint.QuantityUnitKg = request.QuantityUnitKg;
            checkpoint.QuantityNo = request.QuantityNo;
            checkpoint.QuantityTotalKg = request.QuantityTotalKg;
            checkpoint.ManufacturerBrandName = request.ManufacturerBrandName;
            checkpoint.DocumentAnalysisResult = request.DocumentAnalysisResult;
            checkpoint.SamplingCheck = request.SamplingCheck;
            checkpoint.VisualCheck = request.VisualCheck;
            checkpoint.CheckResult = request.CheckResult;
            if (request.Result.HasValue)
                checkpoint.Result = request.Result.Value;
            checkpoint.Remarks = request.Remarks;
            checkpoint.LocationRemarks = request.LocationRemarks;
            checkpoint.UpdatedByUserId = request.UpdatedByUserId ?? GetCurrentUserId();
            checkpoint.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(ToCheckpointDto(checkpoint));
        }


        [HttpGet("jobs/{jobId:int}/checkpoint-submissions")]
        public async Task<IActionResult> GetCheckpointSubmissions(int jobId)
        {
            var submissions = await _db.IhmCheckpointSubmissions
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.IhmCheckpointId)
                .ThenBy(x => x.SurveyorUserId)
                .Select(x => new
                {
                    x.Id,
                    x.IhmCheckpointId,
                    x.SurveyorUserId,
                    SurveyorName = _db.users
                        .Where(user => user.Id == x.SurveyorUserId)
                        .Select(user => user.FullName)
                        .FirstOrDefault() ?? $"User {x.SurveyorUserId}",
                    Status = x.Status.ToString(),
                    x.DataJson,
                    x.SubmittedAt,
                    x.ReviewedByUserId,
                    x.ReviewedAt,
                    x.ReviewNotes
                })
                .ToListAsync();
            return Ok(submissions);
        }

        [HttpPost("checkpoint-submissions/{submissionId:int}/approve")]
        public async Task<IActionResult> ApproveCheckpointSubmission(
            int submissionId,
            [FromBody] ReviewIhmCheckpointSubmissionRequest request)
        {
            var submission = await _db.IhmCheckpointSubmissions
                .Include(x => x.IhmCheckpoint)
                .FirstOrDefaultAsync(x => x.Id == submissionId);
            if (submission?.IhmCheckpoint == null)
                return NotFound("Checkpoint submission not found.");

            var reviewerId = request.ReviewedByUserId ?? GetCurrentUserId();
            if (!reviewerId.HasValue || reviewerId.Value <= 0)
                return BadRequest("Reviewer user id is required.");

            var reviewer = await _db.users.FindAsync(reviewerId.Value);
            var isAdmin = reviewer?.Role?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true ||
                          reviewer?.Role?.Equals("Manager", StringComparison.OrdinalIgnoreCase) == true;
            var primarySurveyorId = await _db.SurveyAssignments
                .Where(x => x.SurveyJobId == submission.SurveyJobId &&
                            x.Status != SurveyAssignmentStatus.Returned)
                .OrderBy(x => x.AssignedAt)
                .Select(x => (int?)x.SurveyorUserId)
                .FirstOrDefaultAsync();
            if (!isAdmin && primarySurveyorId != reviewerId.Value)
                return Forbid();

            var snapshot = JsonSerializer.Deserialize<UpdateIhmCheckpointDetailsRequest>(
                submission.DataJson);
            if (snapshot == null)
                return BadRequest("Submission data is invalid.");

            ApplyCheckpointSnapshot(submission.IhmCheckpoint, snapshot);
            submission.Status = IhmCheckpointSubmissionStatus.Approved;
            submission.ReviewedByUserId = reviewerId;
            submission.ReviewedAt = DateTime.UtcNow;
            submission.ReviewNotes = request.Notes;

            var rejected = await _db.IhmCheckpointSubmissions
                .Where(x => x.IhmCheckpointId == submission.IhmCheckpointId &&
                            x.Id != submission.Id)
                .ToListAsync();
            foreach (var item in rejected)
                item.Status = IhmCheckpointSubmissionStatus.Rejected;

            await _db.SaveChangesAsync();
            return Ok(new { message = "Checkpoint submission approved.", submissionId });
        }

        [HttpPost("jobs/{jobId:int}/areas/{areaId:int}/layout")]
        public async Task<IActionResult> UploadAreaLayout(
                 int jobId,
                 int areaId,
                 IFormFile file,
                 [FromForm] int? uploadedByUserId)
        {
            var area = await _db.IhmAreas
                .FirstOrDefaultAsync(x => x.Id == areaId && x.SurveyJobId == jobId);

            if (area == null)
                return NotFound("IHM area not found.");

            if (file == null || file.Length == 0)
                return BadRequest("Layout file is required.");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                return BadRequest("Only image or PDF files are allowed.");

            var uploadFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "ihm-area-layouts",
                jobId.ToString(),
                areaId.ToString()
            );

            Directory.CreateDirectory(uploadFolder);

            var isPdf = extension == ".pdf";
            var baseName = Guid.NewGuid().ToString();
            var storedFileName = isPdf ? $"{baseName}.pdf" : $"{baseName}.jpg";
            var physicalPath = Path.Combine(uploadFolder, storedFileName);

            string markerFileName;
            string markerPhysicalPath;

            if (isPdf)
            {
                await using (var stream = new FileStream(physicalPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                markerFileName = $"{baseName}.jpg";
                markerPhysicalPath = Path.Combine(uploadFolder, markerFileName);
                await ConvertPdfFirstPageToImageAsync(physicalPath, markerPhysicalPath);
            }
            else
            {
                await SaveCompressedPhotoAsync(file, physicalPath);
                markerFileName = storedFileName;
                markerPhysicalPath = physicalPath;
            }

            var filePath = $"/uploads/ihm-area-layouts/{jobId}/{areaId}/{markerFileName}";

            var existingLayout = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId == areaId &&
                            x.IhmCheckpointId == null)
                .OrderByDescending(x => x.UploadedAt)
                .FirstOrDefaultAsync();

            if (existingLayout == null)
            {
                existingLayout = new IhmSamplingPhoto
                {
                    SurveyJobId = jobId,
                    IhmAreaId = areaId,
                    IhmCheckpointId = null
                };
                _db.IhmSamplingPhotos.Add(existingLayout);
            }

            existingLayout.OriginalFileName = file.FileName;
            existingLayout.StoredFileName = storedFileName;
            existingLayout.FilePath = filePath;
            existingLayout.ContentType = "image/jpeg";
            existingLayout.Caption = "Area layout";
            existingLayout.UploadedByUserId = uploadedByUserId ?? GetCurrentUserId() ?? 0;
            existingLayout.UploadedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Area layout saved successfully.",
                id = existingLayout.Id,
                jobId = existingLayout.SurveyJobId,
                areaId = existingLayout.IhmAreaId,
                filePath = existingLayout.FilePath
            });
        }



        [HttpGet("jobs/{jobId:int}/areas/{areaId:int}/layout")]
        public async Task<IActionResult> GetLatestLayout(int jobId, int areaId)
        {
            var photo = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId == areaId &&
                            x.IhmCheckpointId == null)
                .OrderByDescending(x => x.UploadedAt)
                .FirstOrDefaultAsync();

            if (photo == null)
                return NotFound("No layout uploaded for this area.");

            return Ok(new
            {
                areaId = photo.IhmAreaId,
                filePath = photo.FilePath
            });
        }

        [HttpGet("jobs/{jobId:int}/areas/{areaId:int}/layouts")]
        public async Task<IActionResult> GetApplicableLayouts(int jobId, int areaId)
        {
            var areaLayout = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmCheckpointId == null &&
                            x.IhmAreaId == areaId)
                .OrderByDescending(x => x.UploadedAt)
                .Select(x => new
                {
                    x.Id,
                    x.IhmAreaId,
                    x.FilePath,
                    x.OriginalFileName,
                    IsCommon = x.IhmAreaId == null
                })
                .FirstOrDefaultAsync();

            if (areaLayout != null)
                return Ok(new[] { areaLayout });

            var commonLayout = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId == null &&
                            x.IhmCheckpointId == null &&
                            x.Caption == "Full GA layout")
                .OrderByDescending(x => x.UploadedAt)
                .Select(x => new
                {
                    x.Id,
                    x.IhmAreaId,
                    x.FilePath,
                    x.OriginalFileName,
                    IsCommon = true
                })
                .FirstOrDefaultAsync();

            return Ok(commonLayout == null ? Array.Empty<object>() : new object[] { commonLayout });
        }

        [HttpGet("jobs/{jobId:int}/layout-attachments")]
        public async Task<IActionResult> GetLayoutAttachments(int jobId)
        {
            var photos = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmCheckpointId == null &&
                            (x.IhmAreaId != null || x.Caption == "Full GA layout"))
                .OrderByDescending(x => x.UploadedAt)
                .ToListAsync();

            var latest = photos
                .GroupBy(x => x.IhmAreaId?.ToString() ?? "common")
                .Select(x => x.First())
                .Select(x => new
                {
                    Target = x.IhmAreaId?.ToString() ?? "common",
                    x.OriginalFileName,
                    FilePath = $"{Request.Scheme}://{Request.Host}{x.FilePath}"
                });

            return Ok(latest);
        }


        [HttpDelete("markers/{markerId:int}")]
        public async Task<IActionResult> DeleteMarker(int markerId)
        {
            var marker = await _db.IhmImageMarkers.FindAsync(markerId);

            if (marker == null)
                return NotFound("Marker not found.");

            var markersToDelete = marker.IhmCheckpointId.HasValue
                ? await _db.IhmImageMarkers
                    .Where(x =>
                        x.SurveyJobId == marker.SurveyJobId &&
                        x.IhmAreaId == marker.IhmAreaId &&
                        x.IhmCheckpointId == marker.IhmCheckpointId &&
                        x.ImagePath == marker.ImagePath)
                    .ToListAsync()
                : new List<IhmImageMarker> { marker };

            _db.IhmImageMarkers.RemoveRange(markersToDelete);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Marker deleted.",
                id = markerId,
                deletedCount = markersToDelete.Count
            });
        }

        [HttpPost("jobs/{jobId:int}/ga-layout")]
        public async Task<IActionResult> UploadGaLayout(
            int jobId,
            IFormFile file,
            [FromForm] int? uploadedByUserId)
        {
            var jobExists = await CanAccessIhmJobAsync(jobId);

            if (!jobExists)
                return NotFound("Survey job not found.");

            if (file == null || file.Length == 0)
                return BadRequest("GA layout file is required.");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                return BadRequest("Only image or PDF files are allowed.");

            var uploadFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "ihm-ga-layouts",
                jobId.ToString()
            );

            Directory.CreateDirectory(uploadFolder);

            var isPdf = extension == ".pdf";
            var baseName = Guid.NewGuid().ToString();
            var storedFileName = isPdf ? $"{baseName}.pdf" : $"{baseName}{extension}";
            var physicalPath = Path.Combine(uploadFolder, storedFileName);

            await using (var stream = new FileStream(physicalPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            string markerFileName = storedFileName;

            if (isPdf)
            {
                markerFileName = $"{baseName}.jpg";
                var markerPhysicalPath = Path.Combine(uploadFolder, markerFileName);
                await ConvertPdfFirstPageToImageAsync(physicalPath, markerPhysicalPath);
            }

            var filePath = $"/uploads/ihm-ga-layouts/{jobId}/{markerFileName}";

            var existingGa = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId == null &&
                            x.IhmCheckpointId == null &&
                            x.Caption == "Full GA layout")
                .OrderByDescending(x => x.UploadedAt)
                .FirstOrDefaultAsync();

            if (existingGa == null)
            {
                existingGa = new IhmSamplingPhoto
                {
                    SurveyJobId = jobId,
                    IhmAreaId = null,
                    IhmCheckpointId = null,
                    Caption = "Full GA layout"
                };
                _db.IhmSamplingPhotos.Add(existingGa);
            }

            existingGa.OriginalFileName = file.FileName;
            existingGa.StoredFileName = storedFileName;
            existingGa.FilePath = filePath;
            existingGa.ContentType = "image/jpeg";
            existingGa.UploadedByUserId = uploadedByUserId ?? GetCurrentUserId() ?? 0;
            existingGa.UploadedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "GA layout uploaded successfully.",
                id = existingGa.Id,
                jobId = existingGa.SurveyJobId,
                filePath = existingGa.FilePath
            });
        }

        [HttpPost("jobs/{jobId:int}/generate-ga-marker-map")]
        public async Task<IActionResult> GenerateGaMarkerMap(int jobId)
        {
            var gaLayout = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId == null &&
                            x.IhmCheckpointId == null &&
                            x.Caption == "Full GA layout")
                .OrderByDescending(x => x.UploadedAt)
                .FirstOrDefaultAsync();

            if (gaLayout == null)
                return BadRequest("Full GA layout is not uploaded.");

            var areaLayouts = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId != null &&
                            x.IhmCheckpointId == null)
                .OrderBy(x => x.IhmAreaId)
                .Select(x => new
                {
                    x.Id,
                    x.IhmAreaId,
                    AreaName = x.IhmArea != null ? x.IhmArea.AreaName : null,
                    x.OriginalFileName,
                    x.FilePath
                })
                .ToListAsync();

            var markers = await _db.IhmImageMarkers
                .Where(x => x.SurveyJobId == jobId && x.IhmAreaId != null)
                .Select(x => new
                {
                    x.IhmAreaId,
                    x.IhmCheckpointId,
                    x.ImagePath,
                    x.MarkerLabel,
                    x.XPercent,
                    x.YPercent,
                    x.Notes,
                    x.Source,
                    x.SuppressRender,
                    x.IsManualForExistingImage
                })
                .ToListAsync();

            var placements = await _db.IhmAreaLayoutPlacements
                .Where(x => x.SurveyJobId == jobId)
                .Select(x => new
                {
                    x.IhmAreaId,
                    x.XPercent,
                    x.YPercent,
                    x.WidthPercent,
                    x.HeightPercent,
                    x.MatchConfidence,
                    x.IsAutoMatched,
                    x.IsManuallyAdjusted
                })
                .ToListAsync();

            var outputFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "reports",
                "ihm",
                jobId.ToString()
            );

            Directory.CreateDirectory(outputFolder);

            var inputJsonPath = Path.Combine(outputFolder, "ga-map-input.json");
            var outputImagePath = Path.Combine(outputFolder, "appendix4-ga-marker-map.png");
            var placementJsonPath = Path.Combine(outputFolder, "ga-map-placements.json");

            var payload = new
            {
                GaLayout = gaLayout.FilePath,
                AreaLayouts = areaLayouts,
                Markers = markers,
                Placements = placements
            };

            await System.IO.File.WriteAllTextAsync(
                inputJsonPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            );

            var scriptPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "PythonServices",
                "generate_ga_marker_map.py"
            );

            var venvPython = Path.Combine(
                Directory.GetCurrentDirectory(),
                ".venv",
                "Scripts",
                "python.exe"
            );

            var pythonExe = System.IO.File.Exists(venvPython)
                ? venvPython
                : "python";

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" \"{inputJsonPath}\" \"{outputImagePath}\" \"{placementJsonPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            if (process == null)
                return StatusCode(500, "Unable to start GA map generator.");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                return StatusCode(500, string.IsNullOrWhiteSpace(error) ? output : error);

            if (System.IO.File.Exists(placementJsonPath))
            {
                var placementJson = await System.IO.File.ReadAllTextAsync(placementJsonPath);

                var generatedPlacements = JsonSerializer.Deserialize<List<GeneratedPlacementDto>>(
                    placementJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? new();

                foreach (var item in generatedPlacements)
                {
                    var existing = await _db.IhmAreaLayoutPlacements
                        .FirstOrDefaultAsync(x =>
                            x.SurveyJobId == jobId &&
                            x.IhmAreaId == item.IhmAreaId);

                    if (existing == null)
                    {
                        existing = new IhmAreaLayoutPlacement
                        {
                            SurveyJobId = jobId,
                            IhmAreaId = item.IhmAreaId
                        };

                        _db.IhmAreaLayoutPlacements.Add(existing);
                    }

                    if (!existing.IsManuallyAdjusted)
                    {
                        existing.XPercent = item.XPercent;
                        existing.YPercent = item.YPercent;
                        existing.WidthPercent = item.WidthPercent;
                        existing.HeightPercent = item.HeightPercent;
                        existing.MatchConfidence = item.MatchConfidence;
                        existing.IsAutoMatched = item.IsAutoMatched;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                }

                await _db.SaveChangesAsync();
            }

            return Ok(new
            {
                message = "GA marker map generated successfully.",
                filePath = $"/reports/ihm/{jobId}/appendix4-ga-marker-map.png"
            });
        }


        [HttpPost("jobs/{jobId:int}/generate-report")]
        public async Task<IActionResult> GenerateReport(int jobId)
        {
            var unresolvedConflicts = await _db.IhmCheckpointSubmissions.CountAsync(x =>
                x.SurveyJobId == jobId &&
                x.Status == IhmCheckpointSubmissionStatus.Conflict);
            if (unresolvedConflicts > 0)
            {
                return Conflict(new
                {
                    message = "Resolve all surveyor checkpoint conflicts before generating the final report.",
                    unresolvedConflicts
                });
            }

            var job = await _db.SurveyJobs
                .Include(x => x.Assignments)
                .FirstOrDefaultAsync(x => x.Id == jobId);

            if (job == null)
                return NotFound("Survey job not found.");

            var reportInfo = await _db.IhmReportInfos
                .FirstOrDefaultAsync(x => x.SurveyJobId == jobId);

            var revisions = await _db.IhmReportRevisions
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.RevisionNo)
                .ToListAsync();

            var paintAndCoatingSystems = await _db.IhmPaintCoatingSystems
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.ApplicationOfPaint,
                    x.NameOfPaint,
                    x.Location,
                    x.MaterialClassification,
                    x.Mass,
                    x.Unit,
                    x.Remarks
                })
                .ToListAsync();

            var structureAndHull = await _db.IhmStructureHulls
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.EquipmentName,
                    x.Location,
                    x.MaterialClassification,
                    x.PartsWereUsed,
                    x.ApproximateQuantity,
                    x.Remarks
                })
                .ToListAsync();
            var ohsPeople = await _db.IhmOhsPeople
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.RoleType)
                .ThenBy(x => x.SortOrder)
                .Select(x => new
                {
                    x.RoleType,
                    x.Name,
                    x.SortOrder
                })
                .ToListAsync();
            var ohsRiskAssessmentRows = await _db.IhmOhsRiskAssessmentRows
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Activity,
                    x.HazardDescription,
                    x.Risks,
                    x.InitialRiskP,
                    x.InitialRiskS,
                    x.InitialRiskR,
                    x.InitialAlarp,
                    x.ControlMeasures,
                    x.ResidualRiskP,
                    x.ResidualRiskS,
                    x.ResidualRiskR,
                    x.ResidualAlarp,
                    x.ResponsiblePerson,
                    x.SortOrder
                })
                .ToListAsync();
            var appendixOneResultPoints = await _db.IhmAppendixOneResultPoints
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Title,
                    x.Description,
                    x.SortOrder
                })
                .ToListAsync();
            var appendixFiveInformationPoints = await _db.IhmAppendixFiveInformationPoints
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.Description,
                    x.SortOrder,
                    x.SurveyJobId
                })
                .ToListAsync();
            var appendixTwoAttachments = await _db.IhmAppendixTwoAttachments
                .Where(x => x.SurveyJobId == jobId && x.AppendixType == "AppendixTwo")
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.OriginalFileName,
                    x.FilePath,
                    x.ContentType,
                    x.SortOrder
                })
                .ToListAsync();

            var appendixFiveAttachments = await _db.IhmAppendixTwoAttachments
                .Where(x => x.SurveyJobId == jobId && x.AppendixType == "AppendixFive")
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.OriginalFileName,
                    x.FilePath,
                    x.ContentType,
                    x.SortOrder
                })
                .ToListAsync();
            var appendixSevenAttachments = await _db.IhmAppendixTwoAttachments
                .Where(x => x.SurveyJobId == jobId && x.AppendixType == "AppendixSeven")
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.OriginalFileName,
                    x.FilePath,
                    x.ContentType,
                    x.SortOrder
                })
                .ToListAsync();
            var customAppendices = await _db.IhmAppendixTwoAttachments
                .Where(x => x.SurveyJobId == jobId && x.AppendixType == "Custom")
                .OrderBy(x => x.AppendixNo)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.AppendixNo,
                    x.AppendixTitle,
                    x.OriginalFileName,
                    x.FilePath,
                    x.ContentType,
                    x.SortOrder
                })
                .ToListAsync();
            var checkpointEquipmentRows = await _db.IhmCheckpoints
                .Where(x => x.SurveyJobId == jobId && x.CheckResult == "Yes")
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    IhmCheckpointId = x.Id,
                    Sample_No = x.Sample_No,
                    x.EquipmentName,
                    x.Location,
                    MaterialClassification = x.HazardousMaterial,
                    PartsWereUsed = x.Component,
                    UnitKg = x.QuantityUnitKg,
                    QuantityNo = x.QuantityNo,
                    TotalKg = x.QuantityTotalKg,
                    x.Remarks
                })
                .ToListAsync();
            var ihmCheckpoints = await _db.IhmCheckpoints
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.Sample_No,
                    x.Description,
                    x.TableType,
                    x.HazardousMaterial,
                    x.Location,
                    x.EquipmentName,
                    x.Component,
                    x.QuantityUnitKg,
                    x.QuantityNo,
                    x.QuantityTotalKg,
                    x.ManufacturerBrandName,
                    x.DocumentAnalysisResult,
                    x.SamplingCheck,
                    x.VisualCheck,
                    x.CheckResult,
                    x.Remarks,
                    x.LocationRemarks,
                })
            .ToListAsync();
            var savedEquipmentRows = await _db.IhmEquipmentMachineryReportRows
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync();

            var savedByCheckpointId = savedEquipmentRows
                .Where(x => x.IhmCheckpointId.HasValue)
                .ToDictionary(x => x.IhmCheckpointId!.Value);

            var equipmentAndMachinery = checkpointEquipmentRows
            .Select((x, index) =>
            {
                if (savedByCheckpointId.TryGetValue(x.IhmCheckpointId, out var saved))
                {
                    if (saved.IsHidden)
                        return null;

                    return new
                    {
                        x.IhmCheckpointId,
                        x.Sample_No,
                        saved.EquipmentName,
                        saved.Location,
                        saved.MaterialClassification,
                        saved.PartsWereUsed,
                        saved.UnitKg,
                        saved.QuantityNo,
                        saved.TotalKg,
                        saved.Remarks
                    };
                }

                return new
                {
                    x.IhmCheckpointId,
                    x.Sample_No,
                    x.EquipmentName,
                    x.Location,
                    x.MaterialClassification,
                    x.PartsWereUsed,
                    x.UnitKg,
                    x.QuantityNo,
                    x.TotalKg,
                    x.Remarks
                };
            })
            .Where(x => x != null)
            .ToList();

            var manualEquipmentRows = savedEquipmentRows
             .Where(x => !x.IhmCheckpointId.HasValue && !x.IsHidden)
            .Select(x => new
            {
                IhmCheckpointId = 0,
                Sample_No = "",
                x.EquipmentName,
                x.Location,
                x.MaterialClassification,
                x.PartsWereUsed,
                x.UnitKg,
                x.QuantityNo,
                x.TotalKg,
                x.Remarks
            })
            .ToList();
            var necessaryInformationPoints = await _db.IhmNecessaryInformationPoints
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Description,
                    x.SortOrder
                })
                .ToListAsync();
            equipmentAndMachinery.AddRange(manualEquipmentRows);
            var vessel = job.VesselId.HasValue
                ? await _db.Vessels.FirstOrDefaultAsync(x => x.Id == job.VesselId.Value)
                : null;
            var samplingCheckCount = await _db.IhmCheckpoints
                .Where(x => x.SurveyJobId == jobId && x.SamplingCheck == "S")
                .CountAsync();

            var visualCheckCount = await _db.IhmCheckpoints
                .Where(x => x.SurveyJobId == jobId && x.VisualCheck == "V")
                .CountAsync();
            var areas = await _db.IhmAreas
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.SortOrder)
                .Select(area => new
                {
                    area.Id,
                    area.AreaName,
                    area.Description,
                    area.SortOrder,
                    Checkpoints = _db.IhmCheckpoints
                        .Where(cp => cp.IhmAreaId == area.Id)
                        .OrderBy(cp => cp.Sample_No)
                        .Select(cp => new
                        {
                            cp.Id,
                            cp.Sample_No,
                            cp.Description,
                            cp.TableType,
                            cp.Location,
                            cp.EquipmentName,
                            cp.Component,
                            cp.Table_A_B,
                            cp.HazardousMaterial,
                            cp.QuantityUnitKg,
                            cp.QuantityNo,
                            cp.QuantityTotalKg,
                            cp.ManufacturerBrandName,
                            cp.DocumentAnalysisResult,
                            cp.SamplingCheck,
                            cp.VisualCheck,
                            cp.CheckResult,
                            Result = cp.Result.ToString(),
                            cp.Remarks,
                            cp.IsPchm,
                            cp.LocationRemarks,
                            cp.SourceRowNumber
                        })
                        .ToList()
                })
                .ToListAsync();

            var markers = await _db.IhmImageMarkers
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.IhmAreaId,
                    x.IhmCheckpointId,
                    x.ImagePath,
                    x.XPercent,
                    x.YPercent,
                    x.MarkerLabel,
                    x.Notes,
                    x.Source,
                    x.SuppressRender,
                    x.IsManualForExistingImage
                })
                .ToListAsync();

            var photos = await _db.IhmSamplingPhotos
                .Where(x => _db.IhmCheckpoints.Any(cp =>
                    cp.Id == x.IhmCheckpointId &&
                    cp.SurveyJobId == jobId))
                .OrderBy(x => x.UploadedAt)
                .Select(x => new
                {
                    x.Id,
                    x.IhmCheckpointId,
                    x.OriginalFileName,
                    x.FilePath,
                    x.ContentType,
                    x.UploadedAt
                })
                .ToListAsync();

            var areaLayouts = await _db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId != null &&
                            x.IhmCheckpointId == null)
                .OrderBy(x => x.UploadedAt)
                .Select(x => new
                {
                    x.Id,
                    x.IhmAreaId,
                    x.IhmCheckpointId,
                    x.OriginalFileName,
                    x.FilePath,
                    x.ContentType,
                    x.UploadedAt
                })
                .ToListAsync();

            var latestAreaLayout = areaLayouts
                .OrderByDescending(x => x.UploadedAt)
                .FirstOrDefault();

            var areaLayoutPlacements = await _db.IhmAreaLayoutPlacements
                .Where(x => x.SurveyJobId == jobId)
                .OrderBy(x => x.IhmAreaId)
                .Select(x => new
                {
                    x.IhmAreaId,
                    x.XPercent,
                    x.YPercent,
                    x.WidthPercent,
                    x.HeightPercent,
                    x.MatchConfidence,
                    x.IsAutoMatched,
                    x.IsManuallyAdjusted,
                    x.UpdatedAt
                })
                .ToListAsync();

            var outputFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "reports",
                "ihm",
                jobId.ToString()
            );

            Directory.CreateDirectory(outputFolder);

            var inputJsonPath = Path.Combine(outputFolder, "ihm-report-input.json");
            var outputPdfPath = Path.Combine(outputFolder, "ihm-report.pdf");

            var payload = new
            {
                Job = new
                {
                    job.Id,
                    job.JobNumber,
                    job.ClientName,
                    job.ProjectName,
                    job.VesselName,
                    job.ImoNumber,
                    job.SiteName,
                    job.AuditPort,
                    job.AuditAddress,
                    job.StartDate,
                    job.SurveyDate,
                    job.EndDate,
                    Status = job.Status.ToString()
                },
                Vessel = vessel == null ? null : new
                {
                    vessel.Id,
                    vessel.VesselName,
                    vessel.ImoNumber,
                    vessel.OfficialNumber,
                    vessel.VesselType,
                    vessel.Flag,
                    vessel.PortOfRegistry,
                    vessel.ClassSociety,
                    vessel.GrossTonnage,
                    vessel.NetTonnage,
                    vessel.Deadweight,
                    vessel.LengthOverall,
                    vessel.Breadth,
                    vessel.Depth,
                    vessel.YearBuilt,
                    vessel.OwnerName,
                    vessel.ManagerName,
                    vessel.ClientName,
                    vessel.CallSign,
                    vessel.Builder,
                    vessel.DateOfBuild,
                    vessel.LengthBetweenPerpendiculars,
                    vessel.DraughtMoulded
                },
                ReportInfo = reportInfo == null ? null : new
                {
                    reportInfo.CompanyName,
                    reportInfo.CompanyAddress,
                    reportInfo.CompanyTel,
                    reportInfo.CompanyFax,
                    reportInfo.CompanyWebsite,
                    reportInfo.CompanyEmail,
                    reportInfo.CompanyLogoPath,
                    reportInfo.ClientName,
                    reportInfo.ClientLogoPath,
                    reportInfo.PreparedBy,
                    reportInfo.CheckedBy,
                    reportInfo.ApprovedBy,
                    reportInfo.Uid,
                    reportInfo.DocumentNo,
                    reportInfo.DocumentDate,
                    reportInfo.RevisionNo,
                    reportInfo.LabReportNo,
                    reportInfo.LabReportingDate,
                    reportInfo.HazardousMaterialNoFindings,
                    reportInfo.HazardousMaterialFindings,
                    reportInfo.ShipManagerContact,
                    reportInfo.HazmatExpert,
                    reportInfo.SamplingMethodology,
                    reportInfo.SamplingOfFragmentsOfPaints,
                    reportInfo.Laboratory,
                    reportInfo.ChemicalAnalysisMethod,
                    reportInfo.LocationOfVisualSamplingCheck,
                    reportInfo.OhsPreparedBy,
                    reportInfo.OhsPreparedBySignPath,
                    reportInfo.OhsPreparedDate,
                    reportInfo.OhsApprovedBy,
                    reportInfo.OhsApprovedBySignPath,
                    reportInfo.OhsApprovedDate,
                    reportInfo.OhsJobDetail,
                    reportInfo.OhsClient,
                    reportInfo.OhsJobLocation,
                    reportInfo.OhsJobNo,
                    reportInfo.OhsProjectInCharge,
                    reportInfo.OhsDivisionBranch,
                    reportInfo.OhsRaNo,
                    reportInfo.OhsRevNo,
                    reportInfo.OhsRevisionDate,
                },
                IhmSummary = new
                {
                    SamplingCheckCount = samplingCheckCount,
                    VisualCheckCount = visualCheckCount
                },
                Revisions = revisions.Select(x => new
                {
                    x.Uid,
                    x.RevisionNo,
                    x.RevisionDate,
                    x.Description,
                    x.PreparedBy,
                    x.CheckedBy,
                    x.ApprovedBy
                }).ToList(),
                PaintAndCoatingSystems = paintAndCoatingSystems,
                StructureAndHull = structureAndHull,
                EquipmentAndMachinery = equipmentAndMachinery,
                NecessaryInformationPoints = necessaryInformationPoints,
                IhmCheckpoints = ihmCheckpoints,
                OhsPeople = ohsPeople,
                OhsRiskAssessmentRows = ohsRiskAssessmentRows,
                AppendixOneResultPoints = appendixOneResultPoints,
                AppendixTwoAttachments = appendixTwoAttachments,
                AppendixFiveInformationPoints = appendixFiveInformationPoints,
                AppendixFiveAttachments = appendixFiveAttachments,
                AppendixSevenAttachments = appendixSevenAttachments,
                CustomAppendices = customAppendices,
                Areas = areas,
                Markers = markers,
                Photos = photos,
                AreaLayouts = areaLayouts,
                LatestAreaLayout = latestAreaLayout,
                AreaLayoutPlacements = areaLayoutPlacements
            };

            var scriptPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "PythonServices",
                "generate_ihm_report.py"
            );

            if (!System.IO.File.Exists(scriptPath))
                return StatusCode(500, "Python report generator file not found. Create PythonServices/generate_ihm_report.py next.");

            var serializedPayload = JsonSerializer.Serialize(
                  payload,
                  new JsonSerializerOptions
                  {
                      WriteIndented = false
                  });


            var existingPayload = System.IO.File.Exists(inputJsonPath)
                ? await System.IO.File.ReadAllTextAsync(inputJsonPath)
                : null;

            var pythonServicesFolder = Path.GetDirectoryName(scriptPath)!;
            var latestGeneratorChangeUtc = Directory
                  .EnumerateFiles(
                    pythonServicesFolder,
                    "*",
                    System.IO.SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}__pycache__{Path.DirectorySeparatorChar}"))
                .Select(System.IO.File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            if (System.IO.File.Exists(outputPdfPath) &&
                string.Equals(existingPayload, serializedPayload, StringComparison.Ordinal) &&
                System.IO.File.GetLastWriteTimeUtc(outputPdfPath) >= latestGeneratorChangeUtc)
            {
                return Ok(new
                {
                    message = "Existing IHM report is already up to date.",
                    cached = true,
                    filePath = $"/reports/ihm/{jobId}/ihm-report.pdf",
                    fileUrl = $"{Request.Scheme}://{Request.Host}/reports/ihm/{jobId}/ihm-report.pdf"
                });
            }

            await System.IO.File.WriteAllTextAsync(inputJsonPath, serializedPayload);

            if (IhmPdfJobs.TryGetValue(jobId, out var runningJob) &&
    string.Equals(runningJob.Status, "processing", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new { status = "processing" });
            }

            IhmPdfJobs[jobId] = new IhmPdfJobState
            {
                Status = "processing",
                StartedUtc = DateTime.UtcNow,
                FilePath = outputPdfPath
            };

            var requestScheme = Request.Scheme;
            var requestHost = Request.Host.ToString();

            _ = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();

                try
                {
                    var tempOutputPdfPath = outputPdfPath + ".tmp";

                    if (System.IO.File.Exists(tempOutputPdfPath))
                        System.IO.File.Delete(tempOutputPdfPath);

                    var venvPython = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        ".venv",
                        "Scripts",
                        "python.exe"
                    );

                    var pythonExe = System.IO.File.Exists(venvPython)
                        ? venvPython
                        : "python";

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = pythonExe,
                        Arguments = $"\"{scriptPath}\" \"{inputJsonPath}\" \"{tempOutputPdfPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);

                    if (process == null)
                        throw new InvalidOperationException("Unable to start Python report generator.");

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    var output = await outputTask;
                    var error = await errorTask;

                    if (process.ExitCode != 0)
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);

                    if (!System.IO.File.Exists(tempOutputPdfPath))
                        throw new FileNotFoundException("Temporary PDF was not generated.", tempOutputPdfPath);

                    if (System.IO.File.Exists(outputPdfPath))
                        System.IO.File.Delete(outputPdfPath);

                    System.IO.File.Move(tempOutputPdfPath, outputPdfPath);

                    IhmPdfJobs[jobId] = new IhmPdfJobState
                    {
                        Status = "completed",
                        StartedUtc = DateTime.UtcNow,
                        FinishedUtc = DateTime.UtcNow,
                        FilePath = outputPdfPath
                    };

                    Console.WriteLine($"IHM PDF job {jobId} completed in {sw.ElapsedMilliseconds} ms");
                }
                catch (Exception ex)
                {
                    IhmPdfJobs[jobId] = new IhmPdfJobState
                    {
                        Status = "failed",
                        StartedUtc = DateTime.UtcNow,
                        FinishedUtc = DateTime.UtcNow,
                        FilePath = outputPdfPath,
                        Error = ex.GetBaseException().Message
                    };

                    Console.Error.WriteLine($"IHM PDF job {jobId} failed after {sw.ElapsedMilliseconds} ms: {ex}");
                }
            });

            return Ok(new { status = "processing" });
        }

        [HttpGet("jobs/{jobId:int}/pdf-status")]
        public IActionResult GetIhmPdfStatus(int jobId)
        {
            var outputPdfPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "reports",
                "ihm",
                jobId.ToString(),
                "ihm-report.pdf"
            );

            if (IhmPdfJobs.TryGetValue(jobId, out var state))
            {
                return Ok(new
                {
                    status = state.Status,
                    error = state.Error,
                    filePath = System.IO.File.Exists(outputPdfPath)
                        ? $"/reports/ihm/{jobId}/ihm-report.pdf"
                        : null
                });
            }

            return Ok(new
            {
                status = System.IO.File.Exists(outputPdfPath) ? "completed" : "failed",
                filePath = System.IO.File.Exists(outputPdfPath)
                    ? $"/reports/ihm/{jobId}/ihm-report.pdf"
                    : null
            });
        }

        [HttpGet("jobs/{jobId:int}/report-pdf")]
        public async Task<IActionResult> GetGeneratedReportPdf(int jobId)
        {
            var currentUserId = GetCurrentUserId();
            var role =
                User.FindFirstValue(System.Security.Claims.ClaimTypes.Role) ??
                User.FindFirstValue("role");
            var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

            var canAccess = await CanAccessIhmJobAsync(jobId);

            if (!canAccess)
                return Forbid();

            var pdfPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "reports",
                "ihm",
                jobId.ToString(),
                "ihm-report.pdf"
            );

            if (!System.IO.File.Exists(pdfPath))
                return NotFound("IHM report PDF not found.");

            return PhysicalFile(pdfPath, "application/pdf", $"ihm-report-{jobId}.pdf");
        }
        private async Task ConvertPdfFirstPageToImageAsync(string pdfPath, string outputImagePath)
        {
            var scriptPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "PythonServices",
                "pdf_first_page_to_image.py"
            );

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{pdfPath}\" \"{outputImagePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Unable to start PDF converter.");

            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException("PDF to image conversion failed: " + error);
        }
        private static async Task SaveCompressedPhotoAsync(IFormFile file, string outputPath)
        {
            await using var inputStream = file.OpenReadStream();

            using var image = await ImageSharpImage.LoadAsync(inputStream);

            image.Mutate(x =>
            {
                x.AutoOrient();
                x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new SixLabors.ImageSharp.Size(1800, 1800)
                });
                x.BackgroundColor(SixLabors.ImageSharp.Color.White);
            });

            var encoder = new JpegEncoder
            {
                Quality = 75
            };

            await image.SaveAsJpegAsync(outputPath, encoder);
        }

        private static void AutoOrientImage(System.Drawing.Image image)
        {
            const int orientationPropertyId = 0x0112;

            if (!image.PropertyIdList.Contains(orientationPropertyId))
                return;

            var orientation = image.GetPropertyItem(orientationPropertyId)
                .Value[0];

            var rotateFlip = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };

            image.RotateFlip(rotateFlip);
        }


        public class UpdateIhmCheckpointDetailsRequest
        {
            public string SampleNo { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string? TableType { get; set; }
            public string? Location { get; set; }
            public string? EquipmentName { get; set; }
            public string? Component { get; set; }
            public string? MaterialCategory { get; set; }
            public string? HazardousMaterial { get; set; }
            public string? QuantityUnitKg { get; set; }
            public string? QuantityNo { get; set; }
            public string? QuantityTotalKg { get; set; }
            public string? ManufacturerBrandName { get; set; }
            public string? DocumentAnalysisResult { get; set; }
            public string? SamplingCheck { get; set; }
            public string? VisualCheck { get; set; }
            public string? CheckResult { get; set; }
            public IhmCheckpointResult? Result { get; set; }
            public bool IsPchm { get; set; }
            public string? Remarks { get; set; }
            public string? LocationRemarks { get; set; }
            public int? UpdatedByUserId { get; set; }
        }

        public class ReviewIhmCheckpointSubmissionRequest
        {
            public int? ReviewedByUserId { get; set; }
            public string? Notes { get; set; }
        }

        private static string ComputeStoredSubmissionDataHash(
            IhmCheckpointSubmission submission)
        {
            try
            {
                var storedRequest =
                    JsonSerializer.Deserialize<UpdateIhmCheckpointDetailsRequest>(
                        submission.DataJson);
                return storedRequest == null
                    ? submission.DataHash
                    : ComputeCheckpointDataHash(storedRequest);
            }
            catch (JsonException)
            {
                return submission.DataHash;
            }
        }

        private static string ComputeCheckpointDataHash(
            UpdateIhmCheckpointDetailsRequest request)
        {
            var comparisonSnapshot = new UpdateIhmCheckpointDetailsRequest
            {
                SampleNo = request.SampleNo,
                Description = request.Description,
                TableType = request.TableType,
                Location = request.Location,
                EquipmentName = request.EquipmentName,
                Component = request.Component,
                MaterialCategory = request.MaterialCategory,
                HazardousMaterial = request.HazardousMaterial,
                QuantityUnitKg = request.QuantityUnitKg,
                QuantityNo = request.QuantityNo,
                QuantityTotalKg = request.QuantityTotalKg,
                ManufacturerBrandName = request.ManufacturerBrandName,
                DocumentAnalysisResult = request.DocumentAnalysisResult,
                SamplingCheck = request.SamplingCheck,
                VisualCheck = request.VisualCheck,
                CheckResult = request.CheckResult,
                Result = request.Result ?? IhmCheckpointResult.Pending,
                IsPchm = request.IsPchm,
                Remarks = request.Remarks,
                LocationRemarks = request.LocationRemarks
            };
            var comparisonJson = JsonSerializer.Serialize(comparisonSnapshot);
            return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(comparisonJson)));
        }

        private static void ApplyCheckpointSnapshot(
            IhmCheckpoint checkpoint,
            UpdateIhmCheckpointDetailsRequest request)
        {
            checkpoint.IsPchm = request.IsPchm;
            checkpoint.Sample_No = request.SampleNo.Trim();
            checkpoint.Description = request.Description.Trim();
            checkpoint.TableType = request.TableType;
            checkpoint.Location = request.Location;
            checkpoint.EquipmentName = request.EquipmentName;
            checkpoint.Component = request.Component;
            checkpoint.Table_A_B = request.MaterialCategory;
            checkpoint.HazardousMaterial = request.HazardousMaterial;
            checkpoint.QuantityUnitKg = request.QuantityUnitKg;
            checkpoint.QuantityNo = request.QuantityNo;
            checkpoint.QuantityTotalKg = request.QuantityTotalKg;
            checkpoint.ManufacturerBrandName = request.ManufacturerBrandName;
            checkpoint.DocumentAnalysisResult = request.DocumentAnalysisResult;
            checkpoint.SamplingCheck = request.SamplingCheck;
            checkpoint.VisualCheck = request.VisualCheck;
            checkpoint.CheckResult = request.CheckResult;
            if (request.Result.HasValue)
                checkpoint.Result = request.Result.Value;
            checkpoint.Remarks = request.Remarks;
            checkpoint.LocationRemarks = request.LocationRemarks;
            checkpoint.UpdatedByUserId = request.UpdatedByUserId;
            checkpoint.UpdatedAt = DateTime.UtcNow;
        }

        private async Task<bool> CanAccessIhmJobAsync(int jobId)
        {
            return await _access
                .FilterJobs(_db.SurveyJobs.AsQueryable())
                .AnyAsync(x => x.Id == jobId && x.ModuleType == SurveyModuleType.IhmSurvey);
        }
        private int? GetCurrentUserId()
        {
            var claimValue =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("userId");

            return int.TryParse(claimValue, out var userId) ? userId : null;
        }

        private static IhmAreaDto ToAreaDto(IhmArea item)
        {
            return new IhmAreaDto
            {
                Id = item.Id,
                SurveyJobId = item.SurveyJobId,
                AreaName = item.AreaName,
                Description = item.Description,
                SortOrder = item.SortOrder
            };
        }

        private static IhmCheckpointDto ToCheckpointDto(IhmCheckpoint item)
        {
            return new IhmCheckpointDto
            {
                Id = item.Id,
                SurveyJobId = item.SurveyJobId,
                IhmAreaId = item.IhmAreaId,

                Sample_No = item.Sample_No,
                Description = item.Description,

                TableType = item.TableType,
                Location = item.Location,
                EquipmentName = item.EquipmentName,
                Component = item.Component,

                Table_A_B = item.Table_A_B,
                HazardousMaterial = item.HazardousMaterial,

                QuantityUnitKg = item.QuantityUnitKg,
                QuantityNo = item.QuantityNo,
                QuantityTotalKg = item.QuantityTotalKg,

                IsPchm = item.IsPchm,
                LocationRemarks = item.LocationRemarks,


                ManufacturerBrandName = item.ManufacturerBrandName,
                DocumentAnalysisResult = item.DocumentAnalysisResult,
                SamplingCheck = item.SamplingCheck,
                VisualCheck = item.VisualCheck,
                CheckResult = item.CheckResult,

                Result = item.Result.ToString(),
                Remarks = item.Remarks,
                SourceRowNumber = item.SourceRowNumber
            };
        }

        private static IhmMarkerDto ToMarkerDto(IhmImageMarker item)
        {
            return new IhmMarkerDto
            {
                Id = item.Id,
                SurveyJobId = item.SurveyJobId,
                IhmAreaId = item.IhmAreaId,
                IhmCheckpointId = item.IhmCheckpointId,
                ImagePath = item.ImagePath,
                XPercent = item.XPercent,
                YPercent = item.YPercent,
                MarkerLabel = item.MarkerLabel,
                Notes = item.Notes,
                Source = item.Source,
                SuppressRender = item.SuppressRender,
                IsManualForExistingImage = item.IsManualForExistingImage,
                CreatedByUserId = item.CreatedByUserId,
                CreatedAt = item.CreatedAt
            };
        }
        private static IhmSamplingPhotoDto ToSamplingPhotoDto(IhmSamplingPhoto item)
        {
            return new IhmSamplingPhotoDto
            {
                Id = item.Id,
                SurveyJobId = item.SurveyJobId,
                IhmAreaId = item.IhmAreaId,
                IhmCheckpointId = item.IhmCheckpointId,
                OriginalFileName = item.OriginalFileName,
                FilePath = item.FilePath,
                ContentType = item.ContentType,
                Caption = item.Caption,
                UploadedByUserId = item.UploadedByUserId,
                UploadedAt = item.UploadedAt
            };
        }
    }

    public class CreateIhmAreaRequest
    {
        public string AreaName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
    }

    public class CreateIhmCheckpointRequest
    {
        public string CheckpointCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string? TableType { get; set; }
        public string? Location { get; set; }
        public string? EquipmentName { get; set; }
        public string? Component { get; set; }

        public string? Table_A_B { get; set; }
        public string? HazardousMaterial { get; set; }

        public string? QuantityUnitKg { get; set; }
        public string? QuantityNo { get; set; }
        public string? QuantityTotalKg { get; set; }

        public string? ManufacturerBrandName { get; set; }
        public string? DocumentAnalysisResult { get; set; }
        public string? SamplingCheck { get; set; }
        public string? VisualCheck { get; set; }
        public string? CheckResult { get; set; }
        public string? Remarks { get; set; }

        public int? SourceRowNumber { get; set; }
    }

    public class UpdateIhmCheckpointResultRequest
    {
        public IhmCheckpointResult Result { get; set; }
        public string? Remarks { get; set; }
        public int? UpdatedByUserId { get; set; }
    }
    public class AutoMapLayoutMarkersRequest
    {
        public int AreaId { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public int? UserId { get; set; }
    }

    public class DetectedLayoutMarkerRegionDto
    {
        public decimal XPercent { get; set; }
        public decimal YPercent { get; set; }
        public decimal WidthPercent { get; set; }
        public decimal HeightPercent { get; set; }
        public decimal Confidence { get; set; }
        public string? Text { get; set; }
    }
    public class CreateIhmMarkerRequest
    {
        public int SurveyJobId { get; set; }
        public int? IhmAreaId { get; set; }
        public int? IhmCheckpointId { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public decimal XPercent { get; set; }
        public decimal YPercent { get; set; }
        public string? MarkerLabel { get; set; }
        public string? Notes { get; set; }
        public string? Source { get; set; }
        public bool SuppressRender { get; set; }
        public bool IsManualForExistingImage { get; set; }
        public int? CreatedByUserId { get; set; }
    }

    public class IhmAreaDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public string AreaName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
    }

    public class IhmCheckpointDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public int IhmAreaId { get; set; }
        public string Sample_No { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Table_A_B { get; set; }
        public string? HazardousMaterial { get; set; }
        public string Result { get; set; } = string.Empty;

        public string? TableType { get; set; }
        public string? Location { get; set; }
        public string? EquipmentName { get; set; }
        public string? Component { get; set; }

        public string? QuantityUnitKg { get; set; }
        public string? QuantityNo { get; set; }
        public string? QuantityTotalKg { get; set; }


        public bool IsPchm { get; set; }
        public string? LocationRemarks { get; set; }

        public string? ManufacturerBrandName { get; set; }
        public string? DocumentAnalysisResult { get; set; }
        public string? SamplingCheck { get; set; }
        public string? VisualCheck { get; set; }
        public string? CheckResult { get; set; }
        public int? SourceRowNumber { get; set; }


        public string? Remarks { get; set; }
    }

    public class IhmMarkerDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public int? IhmAreaId { get; set; }
        public int? IhmCheckpointId { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public decimal XPercent { get; set; }
        public decimal YPercent { get; set; }
        public string? MarkerLabel { get; set; }
        public string? Notes { get; set; }
        public string? Source { get; set; }
        public bool SuppressRender { get; set; }
        public bool IsManualForExistingImage { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    public class IhmExcelParseResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public List<IhmExcelAreaDto> Areas { get; set; } = new();
    }
    public class IhmSamplingPhotoDto
    {
        public int Id { get; set; }
        public int SurveyJobId { get; set; }
        public int? IhmAreaId { get; set; }
        public int? IhmCheckpointId { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public string? Caption { get; set; }
        public int UploadedByUserId { get; set; }
        public DateTime UploadedAt { get; set; }
    }
    public class IhmExcelAreaDto
    {
        public string AreaName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public int SortOrder { get; set; }

        public List<IhmExcelCheckpointDto> Checkpoints { get; set; } = new();
    }

    public class IhmExcelCheckpointDto
    {
        public string CheckpointCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string? TableType { get; set; }
        public string? Location { get; set; }
        public string? EquipmentName { get; set; }
        public string? Component { get; set; }

        public string? MaterialCategory { get; set; }
        public string? HazardousMaterial { get; set; }

        public string? QuantityUnitKg { get; set; }
        public string? QuantityNo { get; set; }
        public string? QuantityTotalKg { get; set; }

        public string? ManufacturerBrandName { get; set; }
        public string? DocumentAnalysisResult { get; set; }
        public string? SamplingCheck { get; set; }
        public string? VisualCheck { get; set; }
        public string? CheckResult { get; set; }
        public string? Remarks { get; set; }

        public int? SourceRowNumber { get; set; }
    }
    public class GeneratedPlacementDto
    {
        public int IhmAreaId { get; set; }
        public decimal XPercent { get; set; }
        public decimal YPercent { get; set; }
        public decimal WidthPercent { get; set; }
        public decimal HeightPercent { get; set; }
        public decimal MatchConfidence { get; set; }
        public bool IsAutoMatched { get; set; }
    }

}

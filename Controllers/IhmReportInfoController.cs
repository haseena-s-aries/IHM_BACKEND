using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Survey;
using System.Diagnostics;
using System.Text.RegularExpressions;

[Route("api/ihm-report-info")]
[ApiController]
public class IhmReportInfoController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public IhmReportInfoController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("companies")]
    public async Task<IActionResult> GetCompanies([FromQuery] string? search)
    {
        var query = _db.IhmCompanies.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(x =>
                x.CompanyName.Contains(term) ||
                (x.DisplayName != null && x.DisplayName.Contains(term)) ||
                (x.Email != null && x.Email.Contains(term)) ||
                (x.CityLocation != null && x.CityLocation.Contains(term)) ||
                (x.Country != null && x.Country.Contains(term)));
        }

        var companies = await query
            .OrderBy(x => x.DisplayName ?? x.CompanyName)
            .Take(30)
            .Select(x => new
            {
                id = x.Id,
                companyName = x.CompanyName,
                displayName = x.DisplayName ?? x.CompanyName,
                country = x.Country,
                cityLocation = x.CityLocation,
                companyAddress = x.FullAddress,
                companyTel = x.Tel,
                companyFax = x.Fax,
                companyWebsite = x.Website,
                companyEmail = x.Email,
                companyLogoPath = x.LogoPath
            })
            .ToListAsync();

        return Ok(companies);
    }

    [HttpPost("companies/import-tsv")]
    public async Task<IActionResult> ImportCompanies(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Choose the TSV file.");

        var imported = 0;
        var updated = 0;

        using var stream = file.OpenReadStream();
        using var parser = new TextFieldParser(stream);

        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters("\t");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = true;

        if (parser.EndOfData)
            return BadRequest("TSV file is empty.");

        var headers = parser.ReadFields();

        if (headers == null)
            return BadRequest("TSV header was not found.");

        var headerIndexes = headers
            .Select((name, index) => new
            {
                Name = name.Trim().Trim('"'),
                Index = index
            })
            .ToDictionary(
                x => x.Name,
                x => x.Index,
                StringComparer.OrdinalIgnoreCase);

        string? GetValue(string[] fields, string column)
        {
            if (!headerIndexes.TryGetValue(column, out var index))
                return null;

            if (index >= fields.Length)
                return null;

            var value = fields[index]?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        static string NormalizeBranchValue(string? value)
        {
            return (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant();
        }

        static string CreateBranchKey(
        string? companyName,
        string? displayName,
        string? address,
        string? email)
        {
            return string.Join("|",
                NormalizeBranchValue(companyName),
                NormalizeBranchValue(displayName),
                NormalizeBranchValue(address),
                NormalizeBranchValue(email));
        }

        var companyLookup = (await _db.IhmCompanies.ToListAsync())
            .GroupBy(company => CreateBranchKey(
                company.CompanyName,
                company.DisplayName,
                company.FullAddress,
                company.Email))
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        while (!parser.EndOfData)
        {
            string[]? fields;

            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException exception)
            {
                return BadRequest(
                    $"Invalid TSV data near line {exception.Message}");
            }

            if (fields == null)
                continue;

            var companyName = GetValue(fields, "company name");
            var displayName = GetValue(fields, "display in form dropdown");

            if (string.IsNullOrWhiteSpace(companyName))
                continue;

            var normalizedName = companyName.Trim();
            var country = GetValue(fields, "Country");
            var city = GetValue(fields, "City / Location");
            var address = GetValue(fields, "address");
            var email = GetValue(fields, "Email");

            var branchKey = CreateBranchKey(
            normalizedName,
            displayName,
            address,
            email);

            if (!companyLookup.TryGetValue(branchKey, out var company))
            {
                company = new IhmCompany
                {
                    CompanyName = normalizedName,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedName : displayName.Trim(),
                    Country = country,
                    CityLocation = city,
                    FullAddress = address
                };

                _db.IhmCompanies.Add(company);
                companyLookup[branchKey] = company;
                imported++;
            }
            else
            {
                updated++;
            }

            var logoName = GetValue(fields, "logo");

            company.Country = country;
            company.CityLocation = city;
            company.DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedName : displayName.Trim();
            company.FullAddress = address;
            company.Tel = GetValue(fields, "tel");
            company.Fax = GetValue(fields, "fax");
            company.Website = GetValue(fields, "website");
            company.Email = email;

            company.LogoPath = string.IsNullOrWhiteSpace(logoName)
                ? company.LogoPath
                : $"/company-logos/{System.IO.Path.GetFileName(logoName)}";

            company.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Company TSV import completed.",
            imported,
            updated
        });
    }
    [HttpGet("jobs/options")]
    public async Task<IActionResult> GetJobOptions([FromQuery] string? search)
    {
        var query = _db.SurveyJobs
            .AsNoTracking()
            .Where(x => x.ModuleType == SurveyModuleType.IhmSurvey)
            .GroupJoin(
                _db.Vessels.AsNoTracking(),
                job => job.VesselId,
                vessel => vessel.Id,
                (job, vessels) => new { job, vessels })
            .SelectMany(
                x => x.vessels.DefaultIfEmpty(),
                (x, vessel) => new
                {
                    JobId = x.job.Id,
                    x.job.JobNumber,
                    VesselName = vessel != null ? vessel.VesselName : x.job.VesselName,
                    ImoNumber = vessel != null ? vessel.ImoNumber : x.job.ImoNumber
                });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.VesselName.Contains(term) ||
                x.JobNumber.Contains(term) ||
                (x.ImoNumber != null && x.ImoNumber.Contains(term)));
        }

        var rows = await query
            .OrderBy(x => x.VesselName)
            .ThenByDescending(x => x.JobId)
            .Take(50)
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("jobs/{jobId:int}")]
    public async Task<IActionResult> Get(int jobId)
    {
        var job = await _db.SurveyJobs
    .AsNoTracking()
    .FirstOrDefaultAsync(x => x.Id == jobId);

        if (job == null)
        {
            return NotFound("Survey job not found.");
        }

        var vessel = await _db.Vessels
            .AsNoTracking()
            .Where(x => x.Id == job.VesselId)
            .Select(x => new
            {
                x.Id,
                x.VesselName,
                x.ImoNumber,
                x.OfficialNumber,
                x.VesselType,
                x.Flag,
                x.PortOfRegistry,
                x.ClassSociety,
                x.GrossTonnage,
                x.NetTonnage,
                x.Deadweight,
                x.YearBuilt,
                x.OwnerName,
                x.LengthOverall,
                x.Breadth,
                x.Depth,
                x.CallSign,
                x.Builder,
                x.DateOfBuild,
                x.LengthBetweenPerpendiculars,
                x.DraughtMoulded,
                x.ManagerName
            })
            .FirstOrDefaultAsync();
        var data = await _db.IhmReportInfos
    .FirstOrDefaultAsync(x => x.SurveyJobId == jobId);
        data ??= new IhmReportInfo { SurveyJobId = jobId };

    //    var displayHazardousMaterialNoFindings =
    //await BuildDefaultHazardousMaterialNoFindings(jobId);

    //    data.HazardousMaterialNoFindings = displayHazardousMaterialNoFindings;

        var revisions = await _db.IhmReportRevisions
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.RevisionNo)
            .ToListAsync();
        var paintAndCoatingSystems = await _db.IhmPaintCoatingSystems
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var structureAndHull = await _db.IhmStructureHulls
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.Id)
            .ToListAsync();
        var necessaryInformationPoints = await _db.IhmNecessaryInformationPoints
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();
        var ohsPeople = await _db.IhmOhsPeople
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.RoleType)
            .ThenBy(x => x.SortOrder)
            .ToListAsync();
        var ohsRiskAssessmentRows = await _db.IhmOhsRiskAssessmentRows
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();
        var appendixOneResultPoints = await _db.IhmAppendixOneResultPoints
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
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
            .ToListAsync();

        var appendixFiveAttachments = await _db.IhmAppendixTwoAttachments
            .Where(x => x.SurveyJobId == jobId && x.AppendixType == "AppendixFive")
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var appendixSevenAttachments = await _db.IhmAppendixTwoAttachments
            .Where(x => x.SurveyJobId == jobId && x.AppendixType == "AppendixSeven")
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var customAppendices = await _db.IhmAppendixTwoAttachments
            .Where(x => x.SurveyJobId == jobId && x.AppendixType == "Custom")
            .OrderBy(x => x.AppendixNo)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var appendixThreePhotos = await _db.IhmSamplingPhotos
            .Where(x => _db.IhmCheckpoints.Any(cp =>
                cp.Id == x.IhmCheckpointId &&
                cp.SurveyJobId == jobId))
            .OrderBy(x => x.UploadedAt)
            .ToListAsync();
        if (!appendixOneResultPoints.Any())
        {
            appendixOneResultPoints = BuildDefaultAppendixOnePoints(jobId);
            _db.IhmAppendixOneResultPoints.AddRange(appendixOneResultPoints);
            await _db.SaveChangesAsync();
        }
        var checkpointEquipmentRows = await _db.IhmCheckpoints
            .Where(x => x.SurveyJobId == jobId && x.CheckResult == "Yes")
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                IhmCheckpointId = (int?)x.Id,
                EquipmentName = x.EquipmentName,
                Location = x.Location,
                MaterialClassification = x.HazardousMaterial,
                PartsWereUsed = x.Component,
                UnitKg = x.QuantityUnitKg,
                QuantityNo = x.QuantityNo,
                TotalKg = x.QuantityTotalKg,
                Remarks = x.Remarks,
                SortOrder = 0
            })
            .ToListAsync();
        checkpointEquipmentRows = checkpointEquipmentRows
    .Where(x => HasEquipmentMachineryData(
        x.EquipmentName,
        x.Location,
        x.MaterialClassification,
        x.PartsWereUsed,
        x.UnitKg,
        x.QuantityNo,
        x.TotalKg,
        x.Remarks))
    .ToList();
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
                if (x.IhmCheckpointId.HasValue &&
                    savedByCheckpointId.TryGetValue(x.IhmCheckpointId.Value, out var saved))
                {
                    if (saved.IsHidden)
                        return null;
                    return new
                    {
                        saved.Id,
                        saved.IhmCheckpointId,
                        saved.EquipmentName,
                        saved.Location,
                        saved.MaterialClassification,
                        saved.PartsWereUsed,
                        saved.UnitKg,
                        saved.QuantityNo,
                        saved.TotalKg,
                        saved.Remarks,
                        saved.SortOrder,
                        saved.IsHidden
                    };
                }

                return new
                {
                    Id = 0,
                    x.IhmCheckpointId,
                    x.EquipmentName,
                    x.Location,
                    x.MaterialClassification,
                    x.PartsWereUsed,
                    x.UnitKg,
                    x.QuantityNo,
                    x.TotalKg,
                    x.Remarks,
                    SortOrder = index + 1,
                    IsHidden = false
                };
            })
            .Where(x => x != null && HasEquipmentMachineryData(
    x.EquipmentName,
    x.Location,
    x.MaterialClassification,
    x.PartsWereUsed,
    x.UnitKg,
    x.QuantityNo,
    x.TotalKg,
    x.Remarks))
.ToList();

        var manualEquipmentRows = savedEquipmentRows
    .Where(x => !x.IhmCheckpointId.HasValue && !x.IsHidden)
    .Where(x => HasEquipmentMachineryData(
        x.EquipmentName,
        x.Location,
        x.MaterialClassification,
        x.PartsWereUsed,
        x.UnitKg,
        x.QuantityNo,
        x.TotalKg,
        x.Remarks))
            .Select(x => new
            {
                x.Id,
                x.IhmCheckpointId,
                x.EquipmentName,
                x.Location,
                x.MaterialClassification,
                x.PartsWereUsed,
                x.UnitKg,
                x.QuantityNo,
                x.TotalKg,
                x.Remarks,
                x.SortOrder,
                x.IsHidden
            })
            .ToList();

        equipmentAndMachinery.AddRange(manualEquipmentRows);
        return Ok(new
        {
            reportInfo = data,
            vessel,
            job = new
            {
                job.JobNumber,
                job.VesselName,
                job.ImoNumber,
                job.ClientName,
                job.AuditPort,
                job.SiteName,
                job.StartDate,
                job.SurveyDate,
                job.ProjectName
            },
            revisions,
            paintAndCoatingSystems,
            equipmentAndMachinery,
            structureAndHull,
            necessaryInformationPoints,
            ohsPeople,
            ohsRiskAssessmentRows,
            appendixOneResultPoints,
            appendixTwoAttachments,
            appendixThreePhotos,
            appendixFiveAttachments,
            appendixFiveInformationPoints,
            appendixSevenAttachments,
            customAppendices
        });
    }
    private static bool HasEquipmentMachineryData(
    string? equipmentName,
    string? location,
    string? materialClassification,
    string? partsWereUsed,
    string? unitKg,
    string? quantityNo,
    string? totalKg,
    string? remarks)
    {
        return !string.IsNullOrWhiteSpace(equipmentName) ||
               !string.IsNullOrWhiteSpace(location) ||
               !string.IsNullOrWhiteSpace(materialClassification) ||
               !string.IsNullOrWhiteSpace(partsWereUsed) ||
               !string.IsNullOrWhiteSpace(unitKg) ||
               !string.IsNullOrWhiteSpace(quantityNo) ||
               !string.IsNullOrWhiteSpace(totalKg) ||
               !string.IsNullOrWhiteSpace(remarks);
    }

    private async Task<string?> BuildDefaultHazardousMaterialNoFindings(int jobId)
    {
        var materials = await _db.IhmCheckpoints
            .AsNoTracking()
            .Where(x => x.SurveyJobId == jobId &&
                        x.CheckResult != null &&
                        x.CheckResult.ToUpper() == "NO")
            .OrderBy(x => x.Id)
            .Select(x => x.HazardousMaterial)
            .ToListAsync();

        var uniqueMaterials = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var material in materials)
        {
            foreach (var part in SplitHazardousMaterials(material))
            {
                var key = Regex.Replace(part, @"[^A-Za-z0-9]+", "").ToUpperInvariant();

                if (key.Length == 0 || !seen.Add(key))
                    continue;

                uniqueMaterials.Add(part.Trim());
            }
        }

        if (uniqueMaterials.Count == 0)
            return null;

        return "After the analysis of the collected samples at an accredited laboratory, No " +
               JoinWithAnd(uniqueMaterials) +
               " and had been detected in samples.";
    }

    private static IEnumerable<string> SplitHazardousMaterials(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var commaPart in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ampParts = commaPart.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (ampParts.Length > 1 && ampParts.All(IsShortCodeMaterial))
            {
                foreach (var ampPart in ampParts)
                    yield return ampPart;
            }
            else
            {
                yield return commaPart.Trim();
            }
        }
    }

    private static bool IsShortCodeMaterial(string value)
    {
        var normalized = Regex.Replace(value, @"[^A-Za-z0-9]+", "");
        return normalized.Length is > 0 and <= 8 &&
               normalized.All(ch => !char.IsLetter(ch) || char.IsUpper(ch));
    }

    private static string JoinWithAnd(IReadOnlyList<string> values)
    {
        return values.Count switch
        {
            0 => string.Empty,
            1 => values[0],
            2 => $"{values[0]} and {values[1]}",
            _ => $"{string.Join(", ", values.Take(values.Count - 1))} and {values[^1]}"
        };
    }
    private static List<IhmAppendixOneResultPoint> BuildDefaultAppendixOnePoints(int jobId)
    {
        var rows = new List<(string Title, string Description)>
    {
        ("Asbestos", "Refer Appendix 2 for Analysis report for Asbestos."),
        ("Polychlorinated biphenyls (PCBs)", "Refer Appendix 2 for Analysis report for Polychlorinated biphenyls (PCBs)"),
        ("Lead and Lead Compounds", "Refer Appendix 2 for Analysis report for Lead and Lead Compounds."),
        ("Cadmium and Cadmium Compounds.", "Refer Appendix 2 for Analysis report for Cadmium and Cadmium Compounds."),
        ("Polychlorinated Napthalenes (PCNs)", "Refer Appendix 2 for Analysis report for Polychlorinated Napthalenes (PCNs)"),
        ("HBCDD - Brominated Flame Retardant", "Refer Appendix 2 for Analysis report for Polychlorinated biphenyls (HBCDD)"),
        ("PFOS - Perfluro Octane Sulfonic Acid", "Refer Appendix 2 for Analysis report for Perfluro Octane Sulfonic Acid (PFOS)."),
        ("Polybrominated biphenyl (PBBs)", "Refer Appendix 2 for Analysis report for Polybrominated biphenyl (PBBs)"),
        ("Polybrominated diphenyl ethers (PBDEs)", "Refer Appendix 2 for Analysis report for Polybrominated diphenyl ethers (PBDEs)"),
        ("Hexavalent Chromium and Hexavalent Chromium Compounds.", "Refer Appendix 2 for Analysis report for Hexavalent Chromium and Hexavalent Chromium Compounds."),
        ("SCCP - Certain shortchain chlorinated paraffins.", "Refer Appendix 2 for Analysis report for Certain shortchain chlorinated paraffins. (SCCP)."),
        ("Mercury & Mercury Compounds", "Refer Appendix 2 for Analysis report for Mercury & Mercury Compounds"),
        ("Ozone Depleting Substances (ODSs)", "Refer Appendix 2 for Analysis report for Ozone Depleting Substances (ODSs)")
    };

        return rows.Select((x, index) => new IhmAppendixOneResultPoint
        {
            SurveyJobId = jobId,
            Title = x.Title,
            Description = x.Description,
            SortOrder = index + 1
        }).ToList();
    }

    [HttpPost("jobs/{jobId:int}/assets/upload")]
    public async Task<IActionResult> UploadReportAsset(int jobId, IFormFile file)
    {
        var jobExists = await _db.SurveyJobs.AnyAsync(x => x.Id == jobId);
        if (!jobExists) return NotFound("Survey job not found.");

        if (file == null || file.Length == 0)
            return BadRequest("File is required.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            return BadRequest("Only JPG, JPEG, PNG files are allowed.");

        var uploadFolder = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "uploads",
            "ihm-report-assets",
            jobId.ToString()
        );

        Directory.CreateDirectory(uploadFolder);

        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var physicalPath = Path.Combine(uploadFolder, storedFileName);

        await using (var stream = new FileStream(physicalPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new
        {
            filePath = $"/uploads/ihm-report-assets/{jobId}/{storedFileName}",
            originalFileName = file.FileName
        });
    }
    [HttpPost("jobs/{jobId:int}/appendix-attachments/upload")]
    public async Task<IActionResult> UploadAppendixAttachments(
    int jobId,
    List<IFormFile> files,
    [FromForm] string? appendixType,
    [FromForm] string? appendixName,
    [FromForm] int? appendixNo)
    {
        var jobExists = await _db.SurveyJobs.AnyAsync(x => x.Id == jobId);
        if (!jobExists)
            return NotFound("Survey job not found.");

        if (files == null || files.Count == 0)
            return BadRequest("Files are required.");

        var normalizedAppendixType = string.IsNullOrWhiteSpace(appendixType)
            ? "AppendixTwo"
            : appendixType.Trim();

        var allowedAppendixTypes = new[]
        {
        "AppendixTwo",
        "AppendixFive",
        "AppendixSeven",
        "Custom"
    };

        if (!allowedAppendixTypes.Contains(normalizedAppendixType))
            return BadRequest("Invalid appendixType.");

        string? appendixTitle = null;

        if (normalizedAppendixType == "Custom")
        {
            if (string.IsNullOrWhiteSpace(appendixName))
                return BadRequest("Appendix name is required.");

            appendixTitle = appendixName.Trim();

            if (!appendixNo.HasValue)
            {
                appendixNo = (await _db.IhmAppendixTwoAttachments
                    .Where(x => x.SurveyJobId == jobId &&
                                x.AppendixType == "Custom" &&
                                x.AppendixNo.HasValue)
                    .MaxAsync(x => (int?)x.AppendixNo)) ?? 7;

                appendixNo++;
            }
        }

        var uploadFolder = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "uploads",
            "ihm-appendix-attachments",
            normalizedAppendixType,
            jobId.ToString(),
            appendixNo?.ToString() ?? "fixed"
        );

        Directory.CreateDirectory(uploadFolder);

        var currentMaxSort = await _db.IhmAppendixTwoAttachments
            .Where(x => x.SurveyJobId == jobId &&
                        x.AppendixType == normalizedAppendixType &&
                        x.AppendixNo == appendixNo)
            .MaxAsync(x => (int?)x.SortOrder) ?? 0;

        foreach (var file in files)
        {
            if (file.Length == 0)
                continue;

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (extension != ".pdf" &&
                extension != ".png" &&
                extension != ".jpg" &&
                extension != ".jpeg")
            {
                return BadRequest("Only PDF, PNG, JPG, JPEG files are allowed.");
            }

            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var physicalPath = Path.Combine(uploadFolder, storedFileName);

            if (extension == ".pdf")
            {
                var temporaryPath = Path.Combine(
                    uploadFolder,
                    $"{Guid.NewGuid()}-original.pdf");

                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true))
                {
                    await file.CopyToAsync(stream);
                }

                var compressed = await CompressPdfAsync(
                    temporaryPath,
                    physicalPath);

                if (!compressed)
                {
                    System.IO.File.Move(
                        temporaryPath,
                        physicalPath,
                        overwrite: true);
                }
                else
                {
                    System.IO.File.Delete(temporaryPath);
                }
            }
            else
            {
                await using var stream = new FileStream(
                    physicalPath,
                    FileMode.Create);

                await file.CopyToAsync(stream);
            }

            currentMaxSort++;

            _db.IhmAppendixTwoAttachments.Add(new IhmAppendixTwoAttachment
            {
                SurveyJobId = jobId,
                AppendixType = normalizedAppendixType,
                AppendixNo = appendixNo,
                AppendixTitle = appendixTitle,
                OriginalFileName = file.FileName,
                FilePath = $"/uploads/ihm-appendix-attachments/{normalizedAppendixType}/{jobId}/{appendixNo?.ToString() ?? "fixed"}/{storedFileName}",
                ContentType = file.ContentType,
                SortOrder = currentMaxSort
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Appendix files uploaded.",
            appendixType = normalizedAppendixType,
            appendixNo,
            appendixTitle
        });
    }
    [HttpDelete("jobs/{jobId:int}/appendix-attachments/{id:int}")]
    public async Task<IActionResult> DeleteAppendixAttachment(int jobId, int id)
    {
        var item = await _db.IhmAppendixTwoAttachments
            .FirstOrDefaultAsync(x => x.Id == id && x.SurveyJobId == jobId);

        if (item == null)
            return NotFound("Attachment not found.");

        var physicalPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            item.FilePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString())
        );

        _db.IhmAppendixTwoAttachments.Remove(item);
        await _db.SaveChangesAsync();

        if (System.IO.File.Exists(physicalPath))
            System.IO.File.Delete(physicalPath);

        return Ok(new { message = "Attachment deleted." });
    }

    [HttpPut("jobs/{jobId:int}/custom-appendices/{appendixNo:int}")]
    public async Task<IActionResult> UpdateCustomAppendixTitle(
        int jobId,
        int appendixNo,
        [FromBody] UpdateCustomAppendixTitleRequest request)
    {
        var items = await _db.IhmAppendixTwoAttachments
            .Where(x => x.SurveyJobId == jobId &&
                        x.AppendixType == "Custom" &&
                        x.AppendixNo == appendixNo)
            .ToListAsync();

        if (!items.Any())
            return NotFound("Custom appendix not found.");

        foreach (var item in items)
            item.AppendixTitle = request.AppendixTitle;

        await _db.SaveChangesAsync();

        return Ok(new { message = "Custom appendix updated." });
    }


    [HttpPost("jobs/{jobId:int}/copy-appendices-from/{sourceJobId:int}")]
    public async Task<IActionResult> CopyAppendicesFromJob(int jobId, int sourceJobId)
    {
        if (jobId == sourceJobId)
            return Ok(new { message = "Same job selected. Nothing copied." });

        var currentJobExists = await _db.SurveyJobs.AnyAsync(x => x.Id == jobId);
        var sourceJobExists = await _db.SurveyJobs.AnyAsync(x => x.Id == sourceJobId);

        if (!currentJobExists || !sourceJobExists)
            return NotFound("Job not found.");

        var oldItems = await _db.IhmAppendixTwoAttachments
            .Where(x => x.SurveyJobId == jobId)
            .ToListAsync();

        _db.IhmAppendixTwoAttachments.RemoveRange(oldItems);

        var sourceItems = await _db.IhmAppendixTwoAttachments
            .Where(x => x.SurveyJobId == sourceJobId)
            .OrderBy(x => x.AppendixType)
            .ThenBy(x => x.AppendixNo)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();

        foreach (var source in sourceItems)
        {
            var sourcePhysicalPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                source.FilePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString())
            );

            var extension = Path.GetExtension(source.OriginalFileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = Path.GetExtension(source.FilePath);

            var storedFileName = $"{Guid.NewGuid()}{extension}";

            var targetFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "ihm-appendix-attachments",
                source.AppendixType,
                jobId.ToString(),
                source.AppendixNo?.ToString() ?? "fixed"
            );

            Directory.CreateDirectory(targetFolder);

            var targetPhysicalPath = Path.Combine(targetFolder, storedFileName);

            if (System.IO.File.Exists(sourcePhysicalPath))
            {
                System.IO.File.Copy(sourcePhysicalPath, targetPhysicalPath, overwrite: false);
            }

            _db.IhmAppendixTwoAttachments.Add(new IhmAppendixTwoAttachment
            {
                SurveyJobId = jobId,
                AppendixType = source.AppendixType,
                AppendixNo = source.AppendixNo,
                AppendixTitle = source.AppendixTitle,
                OriginalFileName = source.OriginalFileName,
                FilePath = $"/uploads/ihm-appendix-attachments/{source.AppendixType}/{jobId}/{source.AppendixNo?.ToString() ?? "fixed"}/{storedFileName}",
                ContentType = source.ContentType,
                SortOrder = source.SortOrder
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = "Appendices copied to current job." });
    }

    [HttpPost("jobs/{jobId:int}")]
    public async Task<IActionResult> Save(int jobId, [FromBody] SaveIhmReportInfoRequest request)
    {
        var jobExists = await _db.SurveyJobs.AnyAsync(x => x.Id == jobId);
        if (!jobExists) return NotFound("Survey job not found.");

        var existing = await _db.IhmReportInfos
            .FirstOrDefaultAsync(x => x.SurveyJobId == jobId);

        if (existing == null)
        {
            existing = new IhmReportInfo
            {
                SurveyJobId = jobId
            };

            _db.IhmReportInfos.Add(existing);
        }
        existing.CompanyName = request.CompanyName?.Trim();
        existing.CompanyAddress = request.CompanyAddress;
        existing.CompanyTel = request.CompanyTel;
        existing.CompanyFax = request.CompanyFax;
        existing.CompanyWebsite = request.CompanyWebsite;
        existing.CompanyEmail = request.CompanyEmail;
        existing.CompanyLogoPath = request.CompanyLogoPath;
        await UpsertCompany(request);

        existing.ClientLogoPath = request.ClientLogoPath;
        existing.ClientName = string.IsNullOrWhiteSpace(request.ClientLogoPath)
            ? request.ClientName
            : null;

        existing.PreparedBy = request.PreparedBy;
        existing.CheckedBy = request.CheckedBy;
        existing.ApprovedBy = request.ApprovedBy;

        existing.Uid = request.Uid;
        existing.DocumentNo = request.DocumentNo;
        existing.DocumentDate = request.DocumentDate;
        existing.RevisionNo = request.RevisionNo;
        existing.LabReportNo = request.LabReportNo;
        existing.LabReportingDate = request.LabReportingDate;
        existing.HazardousMaterialNoFindings = request.HazardousMaterialNoFindings;
        existing.HazardousMaterialFindings = request.HazardousMaterialFindings;
        existing.ShipManagerContact = request.ShipManagerContact;
        existing.HazmatExpert = request.HazmatExpert;
        existing.SamplingMethodology = request.SamplingMethodology;
        existing.SamplingOfFragmentsOfPaints = request.SamplingOfFragmentsOfPaints;
        existing.Laboratory = request.Laboratory;
        existing.ChemicalAnalysisMethod = request.ChemicalAnalysisMethod;
        existing.LocationOfVisualSamplingCheck = request.LocationOfVisualSamplingCheck;
        existing.OhsJobDetail = request.OhsJobDetail;
        existing.OhsClient = request.OhsClient;
        existing.OhsJobLocation = request.OhsJobLocation;
        existing.OhsJobNo = request.OhsJobNo;
        existing.OhsProjectInCharge = request.OhsProjectInCharge;
        existing.OhsDivisionBranch = request.OhsDivisionBranch;
        existing.OhsRaNo = request.OhsRaNo;
        existing.OhsRevNo = request.OhsRevNo;
        existing.OhsRevisionDate = request.OhsRevisionDate;
        existing.OhsPreparedBy = request.OhsPreparedBy;
        existing.OhsPreparedBySignPath = request.OhsPreparedBySignPath;
        existing.OhsPreparedDate = request.OhsPreparedDate;

        existing.OhsApprovedBy = request.OhsApprovedBy;
        existing.OhsApprovedBySignPath = request.OhsApprovedBySignPath;
        existing.OhsApprovedDate = request.OhsApprovedDate;

        var oldRevisions = await _db.IhmReportRevisions
    .Where(x => x.SurveyJobId == jobId)
    .ToListAsync();

        _db.IhmReportRevisions.RemoveRange(oldRevisions);
        var oldPaintRows = await _db.IhmPaintCoatingSystems
            .Where(x => x.SurveyJobId == jobId)
            .ToListAsync();

        var oldStructureRows = await _db.IhmStructureHulls
            .Where(x => x.SurveyJobId == jobId)
            .ToListAsync();
        var oldEquipmentRows = await _db.IhmEquipmentMachineryReportRows
            .Where(x => x.SurveyJobId == jobId)
            .ToListAsync();

        _db.IhmEquipmentMachineryReportRows.RemoveRange(oldEquipmentRows);
        _db.IhmPaintCoatingSystems.RemoveRange(oldPaintRows);
        _db.IhmStructureHulls.RemoveRange(oldStructureRows);

        var oldNecessaryInformationPoints = await _db.IhmNecessaryInformationPoints
            .Where(x => x.SurveyJobId == jobId)
            .ToListAsync();

        _db.IhmNecessaryInformationPoints.RemoveRange(oldNecessaryInformationPoints);

        foreach (var item in request.NecessaryInformationPoints)
        {
            _db.IhmNecessaryInformationPoints.Add(new IhmNecessaryInformationPoint
            {
                SurveyJobId = jobId,
                Description = item.Description,
                SortOrder = item.SortOrder
            });
        }
        var oldAppendixFiveInformationPoints = await _db.IhmAppendixFiveInformationPoints
        .Where(x => x.SurveyJobId == jobId)
        .ToListAsync();

        _db.IhmAppendixFiveInformationPoints.RemoveRange(oldAppendixFiveInformationPoints);

        foreach (var item in request.AppendixFiveInformationPoints)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                continue;

            _db.IhmAppendixFiveInformationPoints.Add(new IhmAppendixFiveInformationPoint
            {
                SurveyJobId = jobId,
                Description = item.Description,
                SortOrder = item.SortOrder
            });
        }
        foreach (var item in request.Revisions)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                continue;
            var revision = new IhmReportRevision
            {
                SurveyJobId = jobId,
                Uid = string.IsNullOrWhiteSpace(item.Uid) ? existing.Uid : item.Uid,
                RevisionNo = item.RevisionNo ?? existing.RevisionNo,
                RevisionDate = item.RevisionDate ?? existing.DocumentDate,
                Description = item.Description,
                PreparedBy = string.IsNullOrWhiteSpace(item.PreparedBy) ? existing.PreparedBy : item.PreparedBy,
                CheckedBy = string.IsNullOrWhiteSpace(item.CheckedBy) ? existing.CheckedBy : item.CheckedBy,
                ApprovedBy = string.IsNullOrWhiteSpace(item.ApprovedBy) ? existing.ApprovedBy : item.ApprovedBy
            };

            _db.IhmReportRevisions.Add(revision);
        }
        foreach (var item in request.PaintAndCoatingSystems)
        {
            _db.IhmPaintCoatingSystems.Add(new IhmPaintCoatingSystem
            {
                SurveyJobId = jobId,
                ApplicationOfPaint = item.ApplicationOfPaint,
                NameOfPaint = item.NameOfPaint,
                Location = item.Location,
                MaterialClassification = item.MaterialClassification,
                Mass = item.Mass,
                Unit = item.Unit,
                Remarks = item.Remarks
            });
        }

        foreach (var item in request.StructureAndHull)
        {
            _db.IhmStructureHulls.Add(new IhmStructureHull
            {
                SurveyJobId = jobId,
                EquipmentName = item.EquipmentName,
                Location = item.Location,
                MaterialClassification = item.MaterialClassification,
                PartsWereUsed = item.PartsWereUsed,
                ApproximateQuantity = item.ApproximateQuantity,
                Remarks = item.Remarks
            });
        }
        var equipmentSortOrder = 1;

        foreach (var item in request.EquipmentAndMachinery.Where(x => HasEquipmentMachineryData(
    x.EquipmentName,
    x.Location,
    x.MaterialClassification,
    x.PartsWereUsed,
    x.UnitKg,
    x.QuantityNo,
    x.TotalKg,
    x.Remarks)))
        {
            _db.IhmEquipmentMachineryReportRows.Add(new IhmEquipmentMachineryReportRow
            {
                SurveyJobId = jobId,
                IhmCheckpointId = item.IhmCheckpointId,
                EquipmentName = item.EquipmentName,
                Location = item.Location,
                MaterialClassification = item.MaterialClassification,
                PartsWereUsed = item.PartsWereUsed,
                UnitKg = item.UnitKg,
                QuantityNo = item.QuantityNo,
                TotalKg = item.TotalKg,
                Remarks = item.Remarks,
                SortOrder = item.SortOrder > 0 ? item.SortOrder : equipmentSortOrder,
                IsHidden = item.IsHidden
            });

            equipmentSortOrder++;
        }
        var oldOhsPeople = await _db.IhmOhsPeople
        .Where(x => x.SurveyJobId == jobId)
        .ToListAsync();

        _db.IhmOhsPeople.RemoveRange(oldOhsPeople);

        foreach (var person in request.OhsPeople)
        {
            if (string.IsNullOrWhiteSpace(person.Name))
                continue;

            _db.IhmOhsPeople.Add(new IhmOhsPerson
            {
                SurveyJobId = jobId,
                RoleType = person.RoleType,
                Name = person.Name,
                SortOrder = person.SortOrder
            });
        }
        var oldOhsRows = await _db.IhmOhsRiskAssessmentRows
    .Where(x => x.SurveyJobId == jobId)
    .ToListAsync();

        _db.IhmOhsRiskAssessmentRows.RemoveRange(oldOhsRows);

        foreach (var row in request.OhsRiskAssessmentRows)
        {
            _db.IhmOhsRiskAssessmentRows.Add(new IhmOhsRiskAssessmentRow
            {
                SurveyJobId = jobId,
                Activity = row.Activity,
                HazardDescription = row.HazardDescription,
                Risks = row.Risks,
                InitialRiskP = row.InitialRiskP,
                InitialRiskS = row.InitialRiskS,
                InitialRiskR = row.InitialRiskR,
                InitialAlarp = row.InitialAlarp,
                ControlMeasures = row.ControlMeasures,
                ResidualRiskP = row.ResidualRiskP,
                ResidualRiskS = row.ResidualRiskS,
                ResidualRiskR = row.ResidualRiskR,
                ResidualAlarp = row.ResidualAlarp,
                ResponsiblePerson = row.ResponsiblePerson,
                SortOrder = row.SortOrder
            });
        }
        if (request.AppendixOneResultPoints != null && request.AppendixOneResultPoints.Any())
        {
            var oldAppendixOnePoints = await _db.IhmAppendixOneResultPoints
                .Where(x => x.SurveyJobId == jobId)
                .ToListAsync();

            _db.IhmAppendixOneResultPoints.RemoveRange(oldAppendixOnePoints);

            foreach (var item in request.AppendixOneResultPoints)
            {
                if (string.IsNullOrWhiteSpace(item.Title) &&
                    string.IsNullOrWhiteSpace(item.Description))
                    continue;

                _db.IhmAppendixOneResultPoints.Add(new IhmAppendixOneResultPoint
                {
                    SurveyJobId = jobId,
                    Title = item.Title,
                    Description = item.Description,
                    SortOrder = item.SortOrder
                });
            }
        }
        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    private async Task UpsertCompany(SaveIhmReportInfoRequest request)
    {
        if (!request.CompanyId.HasValue)
            return;

        var company = await _db.IhmCompanies
            .FirstOrDefaultAsync(x => x.Id == request.CompanyId.Value);

        if (company == null)
            return;

        var name = request.CompanyName?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return;

        company.CompanyName = name;
        company.DisplayName = string.IsNullOrWhiteSpace(request.CompanyDisplayName)
            ? name
            : request.CompanyDisplayName.Trim();

        company.FullAddress = request.CompanyAddress?.Trim();
        company.Tel = request.CompanyTel?.Trim();
        company.Fax = request.CompanyFax?.Trim();
        company.Website = request.CompanyWebsite?.Trim();
        company.Email = request.CompanyEmail?.Trim();
        company.LogoPath = request.CompanyLogoPath?.Trim();
        company.UpdatedAt = DateTime.UtcNow;
    }

    private static string? Merge(string? newValue, string? oldValue)
    {
        return string.IsNullOrWhiteSpace(newValue)
            ? oldValue
            : newValue.Trim();
    }

    private static async Task<bool> CompressPdfAsync(
    string inputPath,
    string outputPath)
    {
        var ghostscript = Directory
            .EnumerateFiles(
                @"C:\Program Files\gs",
                "gswin64c.exe",
                System.IO.SearchOption.AllDirectories)
            .OrderByDescending(path => path)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(ghostscript))
            return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = ghostscript,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-sDEVICE=pdfwrite");
        startInfo.ArgumentList.Add("-dCompatibilityLevel=1.6");
        startInfo.ArgumentList.Add("-dPDFSETTINGS=/ebook");
        startInfo.ArgumentList.Add("-dNOPAUSE");
        startInfo.ArgumentList.Add("-dQUIET");
        startInfo.ArgumentList.Add("-dBATCH");
        startInfo.ArgumentList.Add("-dDetectDuplicateImages=true");
        startInfo.ArgumentList.Add("-dCompressFonts=true");
        startInfo.ArgumentList.Add("-dSubsetFonts=true");
        startInfo.ArgumentList.Add($"-sOutputFile={outputPath}");
        startInfo.ArgumentList.Add(inputPath);

        using var process = Process.Start(startInfo);

        if (process == null)
            return false;

        await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 &&
               System.IO.File.Exists(outputPath) &&
               new FileInfo(outputPath).Length > 0;
    }
    public class SaveIhmReportInfoRequest
    {
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyDisplayName { get; set; }
        public string? CompanyAddress { get; set; }
        public string? CompanyTel { get; set; }
        public string? CompanyFax { get; set; }
        public string? CompanyWebsite { get; set; }
        public string? CompanyEmail { get; set; }
        public string? CompanyLogoPath { get; set; }

        public string? ClientName { get; set; }
        public string? ClientLogoPath { get; set; }

        public string? PreparedBy { get; set; }
        public string? CheckedBy { get; set; }
        public string? ApprovedBy { get; set; }

        public string? Uid { get; set; }
        public string? DocumentNo { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string? RevisionNo { get; set; }
        public string? LabReportNo { get; set; }
        public DateTime? LabReportingDate { get; set; }
        public string? HazardousMaterialNoFindings { get; set; }
        public string? HazardousMaterialFindings { get; set; }
        public string? ShipManagerContact { get; set; }
        public string? HazmatExpert { get; set; }
        public string? SamplingMethodology { get; set; }
        public string? SamplingOfFragmentsOfPaints { get; set; }
        public string? Laboratory { get; set; }
        public string? ChemicalAnalysisMethod { get; set; }
        public string? LocationOfVisualSamplingCheck { get; set; }
        public string? OhsJobDetail { get; set; }
        public string? OhsClient { get; set; }
        public string? OhsJobLocation { get; set; }
        public string? OhsJobNo { get; set; }
        public string? OhsProjectInCharge { get; set; }
        public string? OhsDivisionBranch { get; set; }
        public string? OhsRaNo { get; set; }
        public string? OhsRevNo { get; set; }
        public DateTime? OhsRevisionDate { get; set; }
        public string? OhsPreparedBy { get; set; }
        public string? OhsPreparedBySignPath { get; set; }
        public DateTime? OhsPreparedDate { get; set; }

        public string? OhsApprovedBy { get; set; }
        public string? OhsApprovedBySignPath { get; set; }
        public DateTime? OhsApprovedDate { get; set; }

        public List<SaveIhmOhsRiskAssessmentRowRequest> OhsRiskAssessmentRows { get; set; } = new();
        public List<SaveIhmAppendixOneResultPointRequest> AppendixOneResultPoints { get; set; } = new();
        public List<SaveIhmOhsPersonRequest> OhsPeople { get; set; } = new();
        public List<SaveIhmReportRevisionRequest> Revisions { get; set; } = new();
        public List<SaveIhmPaintCoatingSystemRequest> PaintAndCoatingSystems { get; set; } = new();
        public List<SaveIhmEquipmentMachineryReportRowRequest> EquipmentAndMachinery { get; set; } = new();
        public List<SaveIhmStructureHullRequest> StructureAndHull { get; set; } = new();
        public List<SaveIhmNecessaryInformationPointRequest> NecessaryInformationPoints { get; set; } = new();
        public List<SaveIhmAppendixFiveInformationPointRequest> AppendixFiveInformationPoints { get; set; } = new();
    }
    public class SaveIhmReportRevisionRequest
    {
        public string? Uid { get; set; }
        public string? RevisionNo { get; set; }
        public DateTime? RevisionDate { get; set; }
        public string? Description { get; set; }
        public string? PreparedBy { get; set; }
        public string? CheckedBy { get; set; }
        public string? ApprovedBy { get; set; }
    }

    public class SaveIhmPaintCoatingSystemRequest
    {
        public string? ApplicationOfPaint { get; set; }
        public string? NameOfPaint { get; set; }
        public string? Location { get; set; }
        public string? MaterialClassification { get; set; }
        public string? Mass { get; set; }
        public string? Unit { get; set; }
        public string? Remarks { get; set; }
    }

    public class SaveIhmStructureHullRequest
    {
        public string? EquipmentName { get; set; }
        public string? Location { get; set; }
        public string? MaterialClassification { get; set; }
        public string? PartsWereUsed { get; set; }
        public string? ApproximateQuantity { get; set; }
        public string? Remarks { get; set; }
    }
    public class SaveIhmEquipmentMachineryReportRowRequest
    {
        public int? IhmCheckpointId { get; set; }

        public string? EquipmentName { get; set; }
        public string? Location { get; set; }
        public string? MaterialClassification { get; set; }
        public string? PartsWereUsed { get; set; }

        public string? UnitKg { get; set; }
        public string? QuantityNo { get; set; }
        public string? TotalKg { get; set; }

        public string? Remarks { get; set; }
        public bool IsHidden { get; set; }
        public int SortOrder { get; set; }
    }
    public class SaveIhmNecessaryInformationPointRequest
    {
        public string? Description { get; set; }
        public int SortOrder { get; set; }
    }
    public class SaveIhmOhsPersonRequest
    {
        public string RoleType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }
    public class SaveIhmOhsRiskAssessmentRowRequest
    {
        public string? Activity { get; set; }
        public string? HazardDescription { get; set; }
        public string? Risks { get; set; }

        public string? InitialRiskP { get; set; }
        public string? InitialRiskS { get; set; }
        public string? InitialRiskR { get; set; }
        public string? InitialAlarp { get; set; }

        public string? ControlMeasures { get; set; }

        public string? ResidualRiskP { get; set; }
        public string? ResidualRiskS { get; set; }
        public string? ResidualRiskR { get; set; }
        public string? ResidualAlarp { get; set; }

        public string? ResponsiblePerson { get; set; }
        public int SortOrder { get; set; }
    }
    public class SaveIhmAppendixFiveInformationPointRequest
    {
        public string? Description { get; set; }
        public int SortOrder { get; set; }
    }
    public class SaveIhmAppendixOneResultPointRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }
    public class UpdateCustomAppendixTitleRequest
    {
        public string AppendixTitle { get; set; } = string.Empty;
    }

}

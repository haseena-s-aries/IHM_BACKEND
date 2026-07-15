using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Survey_Certification_App.Data;
using Survey_Certification_App.DTOS;
using Survey_Certification_App.Models;
using Survey_Certification_App.Models.Access;
using Survey_Certification_App.Models.Survey;
using Survey_Certification_App.Services;
using System.Security.Claims;



namespace Survey_Certification_App.Controllers
{
    [Route("api/[controller]")]

    [ApiController]
    [Authorize]
    public class AdminDashboardController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccessScopeService _access;

        public AdminDashboardController(ApplicationDbContext db, AccessScopeService access)
        {
            _db = db;
            _access = access;
        }

        [HttpGet("manageable-projects")]
        public async Task<IActionResult> GetManageableProjects()
        {
            var projects = _db.Projects
                .AsNoTracking()
                .Where(x => x.IsActive);

            if (!_access.IsAdmin)
            {
                var managedProjectIds = _access.ManagedProjectIdQuery();
                projects = projects.Where(x => managedProjectIds.Contains(x.Id));
            }

            var result = await projects
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    code = x.Code
                })
                .ToListAsync();

            return Ok(result);
        }

        [HttpGet("projects/{projectId:int}/roles")]
        public async Task<IActionResult> GetProjectRolesForUserManagement(int projectId)
        {
            if (!await _access.CanManageProjectAsync(projectId))
                return Forbid();

            var roles = await _db.ProjectRoles
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.RoleName)
                .Select(x => new
                {
                    id = x.Id,
                    roleName = x.RoleName
                })
                .ToListAsync();

            return Ok(roles);
        }

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAllUsers()
        {
            var scopedUsers = await _access
                .FilterUsers(_db.users.AsNoTracking())
                .OrderBy(x => x.FullName)
                .ToListAsync();

            var userIds = scopedUsers.Select(x => x.Id).ToList();
            var accessRows = await _db.UserProjectAccesses
                .AsNoTracking()
                .Include(x => x.Project)
                .Where(x => userIds.Contains(x.UserId))
                .ToListAsync();

            return Ok(scopedUsers.Select(user => ToUserDto(user, accessRows)));
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("create-certificate-with-pdf")]
        public async Task<IActionResult> CreateCertificateWithPdf(
    [FromForm] string certificateType,
    [FromForm] string certificateData,
    [FromForm] IFormFile pdfFile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateType))
                    return BadRequest("Certificate type is required");

                if (string.IsNullOrWhiteSpace(certificateData))
                    return BadRequest("Certificate data is required");

                if (pdfFile == null)
                    return BadRequest("PDF file is required");

                var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedPDFs");
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                var fileName = $"{certificateType}_{DateTime.Now:yyyyMMddHHmmss}_{pdfFile.FileName}";
                var filePath = Path.Combine(folderPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await pdfFile.CopyToAsync(stream);
                }

                switch (certificateType.ToLower())
                {
                    case "towage":
                        var towageDto = JsonConvert.DeserializeObject<Certificate>(certificateData);
                        if (towageDto == null) return BadRequest("Invalid Towage JSON");
                        towageDto.PdfFilePath = filePath;
                        _db.certificates.Add(towageDto);
                        break;

                    case "seafastening":
                        var sfDto = JsonConvert.DeserializeObject<SeaFastening>(certificateData);
                        if (sfDto == null) return BadRequest("Invalid SeaFastening JSON");
                        sfDto.PdfFilePath = filePath;
                        _db.SeaFastenings.Add(sfDto);
                        break;

                    case "loadout":
                        var loadDto = JsonConvert.DeserializeObject<LoadoutCertificate>(certificateData);
                        if (loadDto == null) return BadRequest("Invalid Loadout JSON");
                        loadDto.PdfFilePath = filePath;
                        _db.loadoutCertificates.Add(loadDto);
                        break;

                    default:
                        return BadRequest("Invalid certificate type.");
                }

                await _db.SaveChangesAsync();
                return Ok(new { message = "Certificate & PDF uploaded successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message} - {ex.StackTrace}");
            }
        }

        [HttpGet("view-pdf/{type}/{id}")]
        public async Task<IActionResult> ViewPdf(string type, int id)
        {
            var normalizedType = NormalizeReportType(type);
            if (normalizedType == null)
                return BadRequest("Invalid certificate type.");

            if (!await CanAccessReportAsync(normalizedType, id))
                return Forbid();

            object cert = null;

            switch (normalizedType)
            {
                case "towage":
                    cert = await _db.certificates.FindAsync(id);
                    break;
                case "seafastening":
                    cert = await _db.SeaFastenings.FindAsync(id);
                    break;
                case "loadout":
                    cert = await _db.loadoutCertificates.FindAsync(id);
                    break;
            }

            if (cert == null)
                return NotFound("Certificate not found.");

            var pdfPath = (string)cert.GetType().GetProperty("PdfFilePath")?.GetValue(cert);
            if (string.IsNullOrEmpty(pdfPath) || !System.IO.File.Exists(pdfPath))
                return NotFound("PDF file not found.");

            var pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
            return File(pdfBytes, "application/pdf", Path.GetFileName(pdfPath));
        }







        [HttpGet("GetById/{id}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            var user = await _access
    .FilterUsers(_db.users.AsNoTracking())
    .FirstOrDefaultAsync(x => x.Id == id);
            if (user == null) return NotFound("User not found");
            var accessRows = await _db.UserProjectAccesses
                .AsNoTracking()
                .Include(x => x.Project)
                .Where(x => x.UserId == id)
                .ToListAsync();
            return Ok(ToUserDto(user, accessRows));
        }



        [HttpPut("Save")]
        public async Task<IActionResult> SaveUser([FromBody] UserDto userDto)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors)
                                              .Select(e => e.ErrorMessage);
                return BadRequest(new { message = "Validation failed", errors });
            }

            var user = await _db.users.FindAsync(userDto.Id);
            if (user == null)
                return NotFound("User not found");

            var email = userDto.Email?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { message = "Email is required" });

            var emailExists = await _db.users.AnyAsync(u =>
                u.Id != userDto.Id &&
                u.Email.ToLower() == email);

            if (emailExists)
                return BadRequest(new { message = "Email already exists" });
            if (!_access.IsAdmin &&
    string.Equals(userDto.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            user.FullName = userDto.FullName;
            user.Email = email;
            var selectedRole = userDto.RoleName ?? userDto.Role;

            if (_access.IsAdmin && string.Equals(selectedRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                user.ProjectId = null;
                user.ProjectName = "All Projects";
                user.Role = "Admin";
                user.RoleName = "Admin";
                user.RoleId = userDto.RoleId;
                var existingAccesses = await _db.UserProjectAccesses
                    .Where(x => x.UserId == user.Id)
                    .ToListAsync();
                _db.UserProjectAccesses.RemoveRange(existingAccesses);
            }
            else if (userDto.ReplaceProjectAccesses || HasProjectAccessPayload(userDto))
            {
                try
                {
                    var activeAccesses = await ReconcileProjectAccessesAsync(user, userDto.ProjectAccesses);
                    await ApplyPrimaryProjectAccessAsync(user, activeAccesses);
                }
                catch (UnauthorizedAccessException)
                {
                    return Forbid();
                }
                catch (InvalidOperationException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }
            }
            else if (_access.IsAdmin)
            {
                if (!userDto.ProjectId.HasValue || userDto.ProjectId.Value <= 0)
                    return BadRequest(new { message = "Invalid project selected." });

                var project = await _db.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == userDto.ProjectId.Value && x.IsActive);

                if (project == null)
                    return BadRequest(new { message = "Invalid project selected." });

                var role = await _db.ProjectRoles.FirstOrDefaultAsync(x =>
                    x.ProjectId == project.Id &&
                    x.RoleName == selectedRole);
                if (role == null)
                    return BadRequest(new { message = "Invalid role selected for this project." });

                await UpsertProjectAccessAsync(user, project, role);
                user.ProjectId = project.Id;
                user.ProjectName = project.Name;
                user.Role = role.RoleName;
                user.RoleName = role.RoleName;
                user.RoleId = role.Id;
            }
            else
            {
                if (string.Equals(selectedRole, "Admin", StringComparison.OrdinalIgnoreCase))
                    return Forbid();

                if (!userDto.ProjectId.HasValue || userDto.ProjectId.Value <= 0)
                    return BadRequest(new { message = "Invalid project selected." });

                if (!await _access.CanManageProjectAsync(userDto.ProjectId.Value))
                    return Forbid();

                var project = await _db.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == userDto.ProjectId.Value && x.IsActive);
                if (project == null)
                    return BadRequest(new { message = "Invalid project selected." });

                var role = await _db.ProjectRoles.FirstOrDefaultAsync(x =>
                    x.ProjectId == project.Id &&
                    x.RoleName == selectedRole &&
                    x.RoleName != "Admin");
                if (role == null)
                    return BadRequest(new { message = "Invalid role selected for this project." });

                await UpsertProjectAccessAsync(user, project, role);

                if (!user.ProjectId.HasValue || user.ProjectId == project.Id)
                {
                    user.ProjectId = project.Id;
                    user.ProjectName = project.Name;
                    user.Role = role.RoleName;
                    user.RoleName = role.RoleName;
                    user.RoleId = role.Id;
                }
            }
            await _db.SaveChangesAsync();

            return Ok(new { message = "User updated successfully" });
        }

        [HttpPost("Save")]
        public async Task<IActionResult> CreateUser([FromBody] UserDto userDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = userDto.Email?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { message = "Email is required" });

            var emailExists = await _db.users.AnyAsync(u => u.Email.ToLower() == email);
            if (emailExists)
                return BadRequest(new { message = "Email already exists" });
            if (!_access.IsAdmin &&
    string.Equals(userDto.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }
            var selectedRole = userDto.RoleName ?? userDto.Role;

            if (string.Equals(selectedRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!_access.IsAdmin)
                    return Forbid();

                userDto.ProjectId = null;
                userDto.ProjectName = "All Projects";
                userDto.Role = "Admin";
                userDto.RoleName = "Admin";
            }
            else if (userDto.ReplaceProjectAccesses || HasProjectAccessPayload(userDto))
            {
                var firstAccess = userDto.ProjectAccesses
                    .Where(x => x.ProjectId > 0)
                    .GroupBy(x => x.ProjectId)
                    .Select(x => x.Last())
                    .FirstOrDefault();

                if (firstAccess == null)
                    return BadRequest(new { message = "Select at least one project access." });

                userDto.ProjectId = firstAccess.ProjectId;
                userDto.RoleId = firstAccess.RoleId;
                userDto.RoleName = firstAccess.RoleName;
                userDto.Role = firstAccess.RoleName;
                selectedRole = firstAccess.RoleName;
            }

            var creator = _access.UserId.HasValue
                ? await _db.users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == _access.UserId.Value)
                : null;

            int? projectId;
            string? projectName;
            ProjectRole? projectRole = null;
            Project? selectedProject = null;

            if (_access.IsAdmin && string.Equals(userDto.RoleName ?? userDto.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                projectId = null;
                projectName = "All Projects";
            }
            else if (_access.IsAdmin)
            {
                if (!userDto.ProjectId.HasValue || userDto.ProjectId.Value <= 0)
                    return BadRequest(new { message = "Invalid project selected." });

                var project = await _db.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == userDto.ProjectId.Value && x.IsActive);

                if (project == null)
                    return BadRequest(new { message = "Invalid project selected." });

                projectRole = await _db.ProjectRoles.FirstOrDefaultAsync(x =>
                    x.ProjectId == project.Id &&
                    x.RoleName == selectedRole);
                if (projectRole == null)
                    return BadRequest(new { message = "Invalid role selected for this project." });

                selectedProject = project;
                projectId = project.Id;
                projectName = project.Name;
            }
            else
            {
                if (!userDto.ProjectId.HasValue || userDto.ProjectId.Value <= 0)
                    return BadRequest(new { message = "Invalid project selected." });

                if (!await _access.CanManageProjectAsync(userDto.ProjectId.Value))
                    return Forbid();

                var project = await _db.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == userDto.ProjectId.Value && x.IsActive);
                if (project == null)
                    return BadRequest(new { message = "Invalid project selected." });

                projectRole = await _db.ProjectRoles.FirstOrDefaultAsync(x =>
                    x.ProjectId == project.Id &&
                    x.RoleName == selectedRole &&
                    x.RoleName != "Admin");
                if (projectRole == null)
                    return BadRequest(new { message = "Invalid role selected for this project." });

                selectedProject = project;
                projectId = project.Id;
                projectName = project.Name;
            }

            if (!_access.IsAdmin && !projectId.HasValue)
                return Forbid();

            if (string.Equals(userDto.Role, "Admin", StringComparison.OrdinalIgnoreCase) && !_access.IsAdmin)
                return Forbid();
            var user = new User
            {
                FullName = userDto.FullName,
                Email = userDto.Email.ToLower(),
                Role = projectRole?.RoleName ?? userDto.Role,
                IsApproved = true,
                RequestedRole = null,
                IsOtpVerified = true,
                ApprovedAt = DateTime.UtcNow,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(userDto.Password),
                ProjectId = projectId,
                ProjectName = projectName,
                CreatedByUserId = _access.UserId,
                RoleId = projectRole?.Id ?? userDto.RoleId,
                RoleName = projectRole?.RoleName ?? userDto.RoleName ?? userDto.Role
            };

            _db.users.Add(user);
            await _db.SaveChangesAsync();

            if (selectedProject != null && projectRole != null)
            {
                await UpsertProjectAccessAsync(user, selectedProject, projectRole);
                await _db.SaveChangesAsync();
            }

            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) &&
                (userDto.ReplaceProjectAccesses || HasProjectAccessPayload(userDto)))
            {
                try
                {
                    var activeAccesses = await ReconcileProjectAccessesAsync(user, userDto.ProjectAccesses);
                    await ApplyPrimaryProjectAccessAsync(user, activeAccesses);
                    await _db.SaveChangesAsync();
                }
                catch (UnauthorizedAccessException)
                {
                    return Forbid();
                }
                catch (InvalidOperationException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }
            }

            return Ok(new { message = "User created successfully" });
        }

        [HttpDelete("Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _access
            .FilterUsers(_db.users)
            .FirstOrDefaultAsync(x => x.Id == id);
            if (user == null)
            {
                return NotFound("User not found.");
            }
            if (!_access.IsAdmin &&
    string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }
            _db.users.Remove(user);
            await _db.SaveChangesAsync();
            return Ok("User deleted successfully.");
        }



        [HttpGet("certificate-report")]
        public async Task<IActionResult> GetCertificateReport()
        {
            int towageCount = _access.IsAdmin || _access.ProjectId == 3
    ? await _db.certificates.CountAsync()
    : 0;

            int seaFasteningCount = _access.IsAdmin || _access.ProjectId == 3
                ? await _db.SeaFastenings.CountAsync()
                : 0;

            int loadoutCount = _access.IsAdmin || _access.ProjectId == 3
                ? await _db.loadoutCertificates.CountAsync()
                : 0;

            var dto = new TotalCertificateCountDto
            {
                TowageCount = towageCount,
                SeaFasteningCount = seaFasteningCount,
                LoadoutCount = loadoutCount,
                TotalCertificates = towageCount + seaFasteningCount + loadoutCount
            };

            return Ok(dto);
        }

        [HttpGet("report-data/{type}/{id}")]
        public async Task<IActionResult> GetReportData(string type, int id)
        {
            var normalizedType = NormalizeReportType(type);
            if (normalizedType == null)
                return BadRequest("Invalid report type.");

            if (!await CanAccessReportAsync(normalizedType, id))
                return Forbid();

            object? report = normalizedType switch
            {
                "towage" => await _db.certificates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id),
                "seafastening" => await _db.SeaFastenings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id),
                "loadout" => await _db.loadoutCertificates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id),
                "exsurvey" => await _db.SurveyJobs.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == id && x.ModuleType == SurveyModuleType.ExSurvey),
                "ihm" => await _db.SurveyJobs.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == id && x.ModuleType == SurveyModuleType.IhmSurvey),
                _ => null
            };

            return report == null ? NotFound() : Ok(report);
        }

        [HttpGet("saved-reports")]
        public async Task<IActionResult> GetSavedReports()
        {
            var currentUserId = GetCurrentUserId();
            var isAdmin = IsCurrentUserAdmin();
            var userIdText = currentUserId?.ToString();

            if (_access.IsClient)
                return Ok(await GetClientAssignedSavedReportsAsync());

            var towageQuery = _db.certificates.AsNoTracking();
            if (!isAdmin && _access.ProjectId != 3)
            {
                // Towage certificates do not currently persist creator ownership.
                // Do not expose unowned reports to managers.
                towageQuery = towageQuery.Where(x => false);
            }

            var towage = await towageQuery.Select(x => new SavedReportDto
            {
                Id = x.Id,
                ReportType = "towage",
                Title = string.IsNullOrWhiteSpace(x.ProjectTitle) ? x.CertificateType : x.ProjectTitle,
                VesselName = string.IsNullOrWhiteSpace(x.TowName) ? x.TugName : x.TowName,
                Reference = x.CertificateNumber,
                Status = "Saved",
                CreatedBy = x.SurveyedBy,
                SavedDate = x.IssuedDate,
                PdfFilePath = x.PdfFilePath
            }).ToListAsync();

            var seaFasteningQuery = _db.SeaFastenings.AsNoTracking();
            if (!isAdmin && _access.ProjectId != 3)
            {
                seaFasteningQuery = seaFasteningQuery.Where(x => userIdText != null && x.UserId == userIdText);
            }

            var seaFastening = await seaFasteningQuery.Select(x => new SavedReportDto
            {
                Id = x.Id,
                ReportType = "seafastening",
                Title = string.IsNullOrWhiteSpace(x.ProjectTitle) ? x.CertificateTitle : x.ProjectTitle,
                VesselName = x.TransportUnit,
                Reference = x.CertificateNumber,
                Status = "Saved",
                CreatedBy = x.IssuedBy,
                SavedDate = x.IssuedDate,
                PdfFilePath = x.PdfFilePath
            }).ToListAsync();

            var loadoutQuery = _db.loadoutCertificates.AsNoTracking();
            if (!isAdmin && _access.ProjectId != 3)
            {
                // Loadout certificates do not currently persist creator ownership.
                // Do not expose unowned reports to managers.
                loadoutQuery = loadoutQuery.Where(x => false);
            }

            var loadout = await loadoutQuery.Select(x => new SavedReportDto
            {
                Id = x.Id,
                ReportType = "loadout",
                Title = string.IsNullOrWhiteSpace(x.ProjectTitle) ? x.CertificateTitle : x.ProjectTitle,
                VesselName = x.BargeVessel,
                Reference = x.CertificateNumber,
                Status = "Saved",
                CreatedBy = x.IssuedBy,
                SavedDate = x.IssuedDate,
                PdfFilePath = x.PdfFilePath
            }).ToListAsync();

            var ihmJobsQuery = _access
    .FilterJobs(_db.SurveyJobs.AsNoTracking())
    .Where(x => x.ModuleType == SurveyModuleType.IhmSurvey);



            var ihmJobs = await ihmJobsQuery
                .Select(x => new { x.Id, x.VesselName, x.ProjectName, x.JobNumber, x.Status, x.CreatedByUserId, x.CreatedAt })
                .ToListAsync();

            var createdUserIds = ihmJobs
                .Select(x => x.CreatedByUserId)
                .Where(x => x > 0)
                .Distinct()
                .ToList();
            var usersById = createdUserIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.users
                    .AsNoTracking()
                    .Where(x => createdUserIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.FullName ?? x.Email ?? $"User {x.Id}");

            var reportsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "reports", "ihm");
            var ihmReports = ihmJobs.Select(job => new
            {
                Job = job,
                PdfPath = Path.Combine(reportsRoot, job.Id.ToString(), "ihm-report.pdf")
            })
                .Where(x => System.IO.File.Exists(x.PdfPath))
                .Select(x => new SavedReportDto
                {
                    Id = x.Job.Id,
                    ReportType = "ihm",
                    Title = string.IsNullOrWhiteSpace(x.Job.ProjectName) ? "IHM Survey Report" : x.Job.ProjectName,
                    VesselName = x.Job.VesselName,
                    Reference = x.Job.JobNumber,
                    Status = x.Job.Status.ToString(),
                    CreatedByUserId = x.Job.CreatedByUserId,
                    CreatedBy = usersById.TryGetValue(x.Job.CreatedByUserId, out var creator)
                        ? creator
                        : x.Job.CreatedByUserId > 0 ? $"User {x.Job.CreatedByUserId}" : "Creator unavailable",
                    SavedAt = System.IO.File.GetLastWriteTimeUtc(x.PdfPath),
                    PdfFilePath = x.PdfPath
                });

            var exJobsQuery = _access
                .FilterJobs(_db.SurveyJobs.AsNoTracking())
                .Where(x => x.ModuleType == SurveyModuleType.ExSurvey &&
                            (x.Status == SurveyJobStatus.ReportGenerated ||
                             x.Status == SurveyJobStatus.Approved ||
                             x.Status == SurveyJobStatus.Closed ||
                             x.Status == SurveyJobStatus.Submitted));

            var exJobs = await exJobsQuery
                .Select(x => new { x.Id, x.VesselName, x.SiteName, x.ProjectName, x.JobNumber, x.Status, x.CreatedByUserId, x.UpdatedAt, x.CreatedAt })
                .ToListAsync();

            var exCreatedUserIds = exJobs
                .Select(x => x.CreatedByUserId)
                .Where(x => x > 0)
                .Distinct()
                .ToList();
            foreach (var id in exCreatedUserIds)
            {
                if (!usersById.ContainsKey(id))
                {
                    var name = await _db.users.AsNoTracking()
                        .Where(x => x.Id == id)
                        .Select(x => x.FullName ?? x.Email ?? $"User {x.Id}")
                        .FirstOrDefaultAsync();
                    if (!string.IsNullOrWhiteSpace(name))
                        usersById[id] = name;
                }
            }

            var exReports = exJobs.Select(job => new SavedReportDto
            {
                Id = job.Id,
                ReportType = "exsurvey",
                Title = string.IsNullOrWhiteSpace(job.ProjectName) ? "EX-Survey Report" : job.ProjectName,
                VesselName = string.IsNullOrWhiteSpace(job.VesselName) ? job.SiteName : job.VesselName,
                Reference = job.JobNumber,
                Status = job.Status.ToString(),
                CreatedByUserId = job.CreatedByUserId,
                CreatedBy = usersById.TryGetValue(job.CreatedByUserId, out var creator)
                    ? creator
                    : job.CreatedByUserId > 0 ? $"User {job.CreatedByUserId}" : "Creator unavailable",
                SavedAt = job.UpdatedAt ?? job.CreatedAt,
                ProjectName = job.ProjectName
            });

            var reports = towage
       .Concat(seaFastening)
       .Concat(loadout)
       .Concat(ihmReports)
       .Concat(exReports);

            if (!_access.IsAdmin)
            {
                var aurasProjectId = await _access.ResolveProjectIdAsync("AURAS");
                var ihmProjectId = await _access.ResolveProjectIdAsync("IHM");
                var exProjectId = await _access.ResolveProjectIdAsync("EX Survey");

                // Auras project
                if (_access.ProjectId == aurasProjectId)
                {
                    reports = reports.Where(x =>
                        x.ReportType == "towage" ||
                        x.ReportType == "seafastening" ||
                        x.ReportType == "loadout");
                }
                // IHM project
                else if (_access.ProjectId == ihmProjectId)
                {
                    reports = reports.Where(x => x.ReportType == "ihm");
                }
                else if (_access.ProjectId == exProjectId)
                {
                    reports = reports.Where(x => x.ReportType == "exsurvey");
                }
                else
                {
                    reports = Enumerable.Empty<SavedReportDto>();
                }
            }

            return Ok(reports
                .OrderByDescending(x => x.SortDate)
                .ThenByDescending(x => x.Id)
                .Take(50)
                .ToList());
        }

        private async Task<List<SavedReportDto>> GetClientAssignedSavedReportsAsync()
        {
            var clientUserId = _access.UserId;
            if (!clientUserId.HasValue)
                return new List<SavedReportDto>();

            var assignments = await _db.ReportAssignments
                .AsNoTracking()
                .Where(x => x.IsActive && x.ClientUserId == clientUserId.Value)
                .OrderByDescending(x => x.AssignedAt)
                .ToListAsync();

            var result = new List<SavedReportDto>();
            foreach (var assignment in assignments)
            {
                if (assignment.ReportType == "ihm")
                {
                    var job = await _db.SurveyJobs.AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.Id == assignment.ReportId &&
                            x.ModuleType == SurveyModuleType.IhmSurvey);
                    if (job == null) continue;

                    result.Add(new SavedReportDto
                    {
                        Id = job.Id,
                        ReportType = "ihm",
                        Title = string.IsNullOrWhiteSpace(job.ProjectName) ? "IHM Survey Report" : job.ProjectName,
                        VesselName = job.VesselName,
                        Reference = job.JobNumber,
                        Status = job.Status.ToString(),
                        CreatedByUserId = job.CreatedByUserId,
                        CreatedBy = await GetUserDisplayNameAsync(job.CreatedByUserId),
                        ProjectId = job.ProjectId,
                        ProjectName = job.ProjectName,
                        SavedAt = assignment.AssignedAt
                    });
                }
                else if (assignment.ReportType == "exsurvey")
                {
                    var job = await _db.SurveyJobs.AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.Id == assignment.ReportId &&
                            x.ModuleType == SurveyModuleType.ExSurvey);
                    if (job == null) continue;

                    result.Add(new SavedReportDto
                    {
                        Id = job.Id,
                        ReportType = "exsurvey",
                        Title = string.IsNullOrWhiteSpace(job.ProjectName) ? "EX-Survey Report" : job.ProjectName,
                        VesselName = string.IsNullOrWhiteSpace(job.VesselName) ? job.SiteName : job.VesselName,
                        Reference = job.JobNumber,
                        Status = job.Status.ToString(),
                        CreatedByUserId = job.CreatedByUserId,
                        CreatedBy = await GetUserDisplayNameAsync(job.CreatedByUserId),
                        ProjectId = job.ProjectId,
                        ProjectName = job.ProjectName,
                        SavedAt = assignment.AssignedAt
                    });
                }
            }

            return result
                .OrderByDescending(x => x.SortDate)
                .ThenByDescending(x => x.Id)
                .ToList();
        }

        private async Task<string?> GetUserDisplayNameAsync(int userId)
        {
            var name = await _db.users.AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => x.FullName ?? x.Email ?? $"User {x.Id}")
                .FirstOrDefaultAsync();

            return !string.IsNullOrWhiteSpace(name)
                ? name
                : userId > 0 ? $"User {userId}" : null;
        }

        public sealed class SavedReportDto
        {
            public int Id { get; set; }
            public string ReportType { get; set; } = string.Empty;
            public string? Title { get; set; }
            public string? VesselName { get; set; }
            public string? Reference { get; set; }
            public string? Status { get; set; }
            public string? CreatedBy { get; set; }
            public int? CreatedByUserId { get; set; }
            public int? ProjectId { get; set; }
            public string? ProjectName { get; set; }
            public DateOnly? SavedDate { get; set; }
            public DateTime? SavedAt { get; set; }
            [System.Text.Json.Serialization.JsonIgnore]
            public string? PdfFilePath { get; set; }
            public DateTime SortDate => SavedAt
                ?? (SavedDate.HasValue
                    ? SavedDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                    : DateTime.MinValue);
        }



        // ---------- CERTIFICATE LISTS BY TYPE ----------
        [HttpGet("GetCertificates")]
        public async Task<IActionResult> GetCertificates(string search = "", string type = "", int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrEmpty(type))
                return BadRequest("Type is required");

            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;

            IQueryable<object> query;

            if (type.ToLower() == "seafastening")
            {
                var data = _db.SeaFastenings.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    data = data.Where(c =>
                        c.CertificateNumber.Contains(search) ||
                        c.Client.Contains(search) ||
                        c.JobNumber.Contains(search));
                }

                var totalCount = await data.CountAsync();
                var result = await data
                    .Select(c => new cerificatedto
                    {
                        Id = c.Id,
                        CertificateNumber = c.CertificateNumber,
                        ProjectTitle = c.ProjectTitle,
                        ClientName = c.Client,
                        JobNumber = c.JobNumber
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                .ToListAsync();

                return Ok(new PagedResult<cerificatedto> { Data = result, TotalCount = totalCount });
            }
            else if (type.ToLower() == "towage")
            {
                var data = _db.certificates.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    data = data.Where(c =>
                        c.CertificateNumber.Contains(search) ||
                        c.ClientName.Contains(search) ||
                        c.JobNumber.Contains(search));
                }

                var totalCount = await data.CountAsync();
                var result = await data
                    .Select(c => new cerificatedto
                    {
                        Id = c.Id,
                        CertificateNumber = c.CertificateNumber,
                        ProjectTitle = c.ProjectTitle,
                        ClientName = c.ClientName,
                        JobNumber = c.JobNumber
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                .ToListAsync();

                return Ok(new PagedResult<cerificatedto> { Data = result, TotalCount = totalCount });
            }
            else if (type.ToLower() == "loadout")
            {
                var data = _db.loadoutCertificates.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    data = data.Where(c =>
                        c.CertificateNumber.Contains(search) ||
                        c.Client.Contains(search) ||
                        c.JobNumber.Contains(search));
                }

                var totalCount = await data.CountAsync();
                var result = await data
                    .Select(c => new cerificatedto
                    {
                        Id = c.Id,
                        CertificateNumber = c.CertificateNumber,
                        ProjectTitle = c.ProjectTitle,
                        ClientName = c.Client,
                        JobNumber = c.JobNumber
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                .ToListAsync();

                return Ok(new PagedResult<cerificatedto> { Data = result, TotalCount = totalCount });
            }

            return BadRequest("Invalid type selected.");
        }


        // ---------- GET BY ID ----------

        [HttpGet("GetSeaFasteningById/{id}")]
        public async Task<IActionResult> GetSeaFasteningById(int id)
        {
            try
            {
                var cert = await _db.SeaFastenings.FindAsync(id);
                if (cert == null)
                    return NotFound("Sea Fastening Certificate not found");

                return Ok(cert);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Exception: {ex.Message}");
            }
        }

        [HttpGet("GetTowageById/{id}")]
        public async Task<IActionResult> GetTowageById(int id)
        {
            try
            {
                var cert = await _db.certificates.FindAsync(id);
                if (cert == null)
                    return NotFound("Towage Certificate not found");
                return Ok(cert);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Exception:{ex.Message}");
            }
        }

        [HttpGet("GetLoadoutById/{id}")]
        public async Task<IActionResult> GetLoadoutById(int id)
        {
            try
            {
                var cert = await _db.loadoutCertificates.FindAsync(id);
                if (cert == null)
                    return NotFound("Loadout Certificate not found");
                return Ok(cert);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Exception:{ex.Message}");
            }
        }



        [HttpGet("generate-pdf")]
        public async Task<IActionResult> GeneratePdf(string type, int id)
        {
            var normalizedType = NormalizeReportType(type);
            if (normalizedType == null)
                return BadRequest("Invalid certificate type.");

            if (!await CanAccessReportAsync(normalizedType, id))
                return Forbid();

            object cert = null;

            switch (normalizedType)
            {
                case "seafastening":
                    cert = await _db.SeaFastenings.FindAsync(id);
                    break;
                case "towage":
                    cert = await _db.certificates.FindAsync(id);
                    break;
                case "loadout":
                    cert = await _db.loadoutCertificates.FindAsync(id);
                    break;
            }

            if (cert == null)
                return NotFound("Certificate not found.");

            // ✅ Call the helper function inside the same class
            byte[] pdfBytes = GenerateCertificatePdf(cert, normalizedType);

            Response.Headers.Add("Content-Disposition", "inline; filename=" + $"{normalizedType}_certificate_{id}.pdf");
            return File(pdfBytes, "application/pdf");

        }

        private static string? NormalizeReportType(string? type)
        {
            var value = (type ?? string.Empty).Trim().ToLowerInvariant();
            return value is "towage" or "seafastening" or "loadout" or "ihm" or "exsurvey" ? value : null;
        }

        private int? GetCurrentUserId()
        {
            var idValue =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("userId");

            return int.TryParse(idValue, out var id) ? id : null;
        }

        private bool IsCurrentUserAdmin()
        {
            var role =
                User.FindFirstValue(ClaimTypes.Role) ??
                User.FindFirstValue("role");

            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> CanAccessReportAsync(string type, int id)
        {
            return await _access.CanAccessReportAsync(type, id);
        }

        // ✅ PLACE THIS HELPER METHOD INSIDE THIS CLASS
        private byte[] GenerateCertificatePdf(object cert, string type)
        {
            using (var ms = new MemoryStream())
            {
                var writer = new iText.Kernel.Pdf.PdfWriter(ms);
                var pdf = new iText.Kernel.Pdf.PdfDocument(writer);
                var document = new iText.Layout.Document(pdf, iText.Kernel.Geom.PageSize.A4);
                document.SetMargins(40, 40, 40, 40);

                // Fonts and styles
                var bold = iText.Layout.Properties.TextAlignment.LEFT;
                var titleFont = iText.Kernel.Font.PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);
                var normalFont = iText.Kernel.Font.PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);

                // Title
                document.Add(new iText.Layout.Element.Paragraph("CERTIFICATE REPORT")
                    .SetFont(titleFont)
                    .SetFontSize(16)
                    .SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER)
                    .SetMarginBottom(20));

                // Helper: Add a labeled field
                void AddField(string label, string value)
                {
                    var p = new iText.Layout.Element.Paragraph()
                        .Add(new iText.Layout.Element.Text($"{label}: ").SetFont(titleFont))
                        .Add(new iText.Layout.Element.Text(value ?? "").SetFont(normalFont));
                    document.Add(p);
                }

                // Populate by type
                switch (type.ToLower())
                {
                    case "seafastening":
                        var sf = cert as SeaFastening;
                        AddField("CERTIFICATE NO", sf.CertificateNumber);
                        AddField("JOB NO", sf.JobNumber);
                        AddField("PROJECT", sf.ProjectTitle);
                        AddField("CLIENT", sf.Client);
                        AddField("TRANSPORT UNIT", sf.TransportUnit);
                        AddField("CARGO", sf.Cargo);
                        break;

                    case "towage":
                        var tow = cert as Certificate;
                        AddField("CERTIFICATE NO", tow.CertificateNumber);
                        AddField("JOB NO", tow.JobNumber);
                        AddField("PROJECT", tow.ProjectTitle);
                        AddField("CLIENT", tow.ClientName);
                        AddField("NAME OF TOW", tow.TowName);
                        AddField("FROM LOCATION", tow.ArrivalPort);
                        AddField("TO LOCATION", tow.DeparturePort);
                        AddField("TUG NAME", tow.TugName);
                        AddField("CARGO", tow.Cargo);
                        break;

                    case "loadout":
                        var load = cert as LoadoutCertificate;
                        AddField("CERTIFICATE NO", load.CertificateNumber);
                        AddField("JOB NO", load.JobNumber);
                        AddField("PROJECT", load.ProjectTitle);
                        AddField("CLIENT", load.Client);
                        AddField("CARGO", load.Cargo);
                        AddField("ONTO BARGE VESSEL", load.BargeVessel);
                        AddField("LOCATION", load.Location);
                        AddField("MODE OF OPERATION", load.OperationType);
                        break;
                }

                document.Add(new iText.Layout.Element.Paragraph("\n\nThis document is system-generated and valid without signature.")
                    .SetFont(normalFont)
                    .SetFontSize(9)
                    .SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT)
                    .SetItalic());

                document.Close();
                return ms.ToArray();
            }


        }

        private UserDto ToUserDto(User user, IEnumerable<UserProjectAccess> accessRows)
        {
            var projectAccesses = accessRows
                .Where(x => x.UserId == user.Id)
                .Select(x => new UserProjectAccessDto
                {
                    Id = x.Id,
                    UserId = x.UserId,
                    ProjectId = x.ProjectId,
                    ProjectName = x.Project?.Name,
                    ProjectCode = x.Project?.Code,
                    RoleId = x.RoleId,
                    RoleName = x.RoleName,
                    IsActive = x.IsActive,
                    IsApproved = x.IsApproved,
                    IsRejected = x.IsRejected
                })
                .ToList();

            if (!projectAccesses.Any() && user.ProjectId.HasValue)
            {
                projectAccesses.Add(new UserProjectAccessDto
                {
                    UserId = user.Id,
                    ProjectId = user.ProjectId.Value,
                    ProjectName = user.ProjectName,
                    RoleId = user.RoleId,
                    RoleName = user.RoleName ?? user.Role,
                    IsActive = true,
                    IsApproved = user.IsApproved,
                    IsRejected = user.IsRejected
                });
            }

            return new UserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                ProjectId = user.ProjectId,
                ProjectName = user.ProjectName,
                RoleId = user.RoleId,
                RoleName = user.RoleName,
                CreatedByUserId = user.CreatedByUserId,
                IsApproved = user.IsApproved,
                IsRejected = user.IsRejected,
                RequestedRole = user.RequestedRole,
                ProjectAccesses = projectAccesses
            };
        }

        private async Task<UserProjectAccess> UpsertProjectAccessAsync(
            User user,
            Project project,
            ProjectRole role)
        {
            var access = await _db.UserProjectAccesses.FirstOrDefaultAsync(x =>
                x.UserId == user.Id &&
                x.ProjectId == project.Id);

            if (access == null)
            {
                access = new UserProjectAccess
                {
                    UserId = user.Id,
                    ProjectId = project.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _db.UserProjectAccesses.Add(access);
            }

            access.RoleId = role.Id;
            access.RoleName = role.RoleName;
            access.IsActive = true;
            access.IsApproved = true;
            access.IsRejected = false;
            access.UpdatedAtUtc = DateTime.UtcNow;

            return access;
        }

        private static bool HasProjectAccessPayload(UserDto userDto)
        {
            return userDto.ProjectAccesses != null && userDto.ProjectAccesses.Any(x => x.ProjectId > 0);
        }

        private async Task<List<UserProjectAccess>> ReconcileProjectAccessesAsync(
            User user,
            IEnumerable<UserProjectAccessDto>? requestedAccesses)
        {
            var requested = (requestedAccesses ?? Enumerable.Empty<UserProjectAccessDto>())
                .Where(x => x.ProjectId > 0)
                .GroupBy(x => x.ProjectId)
                .Select(x => x.Last())
                .ToList();

            if (!requested.Any())
                throw new InvalidOperationException("Select at least one project access.");

            var requestedProjectIds = requested.Select(x => x.ProjectId).Distinct().ToList();

            List<int>? manageableProjectIds = null;
            if (!_access.IsAdmin)
            {
                manageableProjectIds = await _access.ManagedProjectIdQuery().ToListAsync();
                if (requestedProjectIds.Any(projectId => !manageableProjectIds.Contains(projectId)))
                    throw new UnauthorizedAccessException();
            }

            var projects = await _db.Projects
                .Where(x => requestedProjectIds.Contains(x.Id) && x.IsActive)
                .ToDictionaryAsync(x => x.Id);

            var projectRoles = await _db.ProjectRoles
                .Where(x => requestedProjectIds.Contains(x.ProjectId))
                .ToListAsync();

            var existing = await _db.UserProjectAccesses
                .Where(x => x.UserId == user.Id)
                .ToListAsync();

            var requestedProjectIdSet = requestedProjectIds.ToHashSet();
            var removeCandidates = _access.IsAdmin
                ? existing.Where(x => !requestedProjectIdSet.Contains(x.ProjectId)).ToList()
                : existing.Where(x =>
                    manageableProjectIds!.Contains(x.ProjectId) &&
                    !requestedProjectIdSet.Contains(x.ProjectId)).ToList();

            _db.UserProjectAccesses.RemoveRange(removeCandidates);

            var activeAccesses = new List<UserProjectAccess>();
            foreach (var requestedAccess in requested)
            {
                if (!projects.TryGetValue(requestedAccess.ProjectId, out var project))
                    throw new InvalidOperationException("Invalid project selected.");

                var role = requestedAccess.RoleId.HasValue && requestedAccess.RoleId.Value > 0
                    ? projectRoles.FirstOrDefault(x =>
                        x.Id == requestedAccess.RoleId.Value &&
                        x.ProjectId == project.Id)
                    : projectRoles.FirstOrDefault(x =>
                        x.ProjectId == project.Id &&
                        x.RoleName == requestedAccess.RoleName);

                if (role == null)
                    throw new InvalidOperationException($"Invalid role selected for {project.Name}.");

                if (!_access.IsAdmin && string.Equals(role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException();

                var access = existing.FirstOrDefault(x => x.ProjectId == project.Id);
                if (access == null)
                {
                    access = new UserProjectAccess
                    {
                        UserId = user.Id,
                        ProjectId = project.Id,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _db.UserProjectAccesses.Add(access);
                }

                access.RoleId = role.Id;
                access.RoleName = role.RoleName;
                access.IsActive = true;
                access.IsApproved = true;
                access.IsRejected = false;
                access.UpdatedAtUtc = DateTime.UtcNow;
                activeAccesses.Add(access);
            }

            return activeAccesses
                .OrderBy(x => x.ProjectId)
                .ToList();
        }

        private async Task ApplyPrimaryProjectAccessAsync(User user, IReadOnlyCollection<UserProjectAccess> activeAccesses)
        {
            var primaryAccess = activeAccesses
                .Where(x => x.IsActive && x.IsApproved && !x.IsRejected)
                .OrderBy(x => x.ProjectId)
                .FirstOrDefault();

            if (primaryAccess == null)
                throw new InvalidOperationException("Select at least one active project access.");

            var project = await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == primaryAccess.ProjectId);

            user.ProjectId = primaryAccess.ProjectId;
            user.ProjectName = project?.Name;
            user.RoleId = primaryAccess.RoleId;
            user.RoleName = primaryAccess.RoleName;
            user.Role = primaryAccess.RoleName;
            user.IsApproved = true;
            user.IsRejected = false;
            user.RequestedRole = null;
        }

        [HttpGet("fleet-summary")]
        public async Task<IActionResult> GetFleetSummary()
        {
            // ⚠️ ASSUMPTION: adjust field names below to match your actual Vessel entity
            var vessels = await _db.Vessels
                .AsNoTracking()
                .OrderByDescending(v => v.Id) // swap for v.UpdatedAt / v.CreatedAt if you have one
                .Take(4)
                .Select(v => new VesselSummaryDto
                {
                    Id = v.Id,
                    Name = v.VesselName,
                    Location = v.VesselType,      // or v.Location
                                                  // e.g. "On survey", "Certified", "Towage", "Issued"
                })
                .ToListAsync();

            return Ok(vessels);
        }
    }

    internal class VesselSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Survey_Certification_App.Data;
using Survey_Certification_App.DTOS;
using Survey_Certification_App.IemailserviceInterface;
using Survey_Certification_App.Models;
using Survey_Certification_App.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;

namespace Survey_Certification_App.Controllers
{
    [Route("api/User")] // Matches your API URL exactly
    [ApiController]
    public class UserController : ControllerBase // Change from Controller to ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailService _emailService;
        private readonly EmailSettings _emailSettings;
        private readonly SignInManager<User> _signInManager;
        private readonly IWebHostEnvironment _env;
        private readonly AccessScopeService _access;
        private readonly IConfiguration _config;

        public UserController(
            ApplicationDbContext db,
            IEmailService emailService,
            IConfiguration config,
            IWebHostEnvironment env,
            AccessScopeService access)
        {
            _db = db;
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _emailSettings = config.GetSection("EmailSettings").Get<EmailSettings>();
            _env = env;
            _access = access;
            _config = config;
        }

        private string GenerateJwtToken(User user)
        {
            var key = _config["Jwt:Key"] ?? "THIS_SHOULD_BE_A_LONG_RANDOM_SECRET_KEY";
            var issuer = _config["Jwt:Issuer"] ?? "https://auth.mycompany.com";
            var audience = _config["Jwt:Audience"] ?? "my-apps";
            var effectiveRole = !string.IsNullOrWhiteSpace(user.RoleName)
                ? user.RoleName.Trim()
                : user.Role?.Trim() ?? "";

            var claims = new[]
            {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email ?? ""),
        new Claim(ClaimTypes.Name, user.FullName ?? ""),
        new Claim(ClaimTypes.Role, effectiveRole),
        new Claim("role", effectiveRole),
        new Claim("userId", user.Id.ToString()),
        new Claim("projectId", user.ProjectId?.ToString() ?? ""),
        new Claim("projectName", user.ProjectName ?? ""),
        new Claim("roleId", user.RoleId?.ToString() ?? ""),
        new Claim("roleName", effectiveRole)
    };

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public class RefreshTokenRequestDto
        {
            public string? Token { get; set; }
        }

        // Mobile clients keep the user signed in across app restarts and offline
        // periods, but access tokens expire after 1 hour. This endpoint exchanges a
        // correctly signed token (even if expired, up to the grace window below)
        // for a fresh one, after re-checking that the user is still allowed in.
        private static readonly TimeSpan RefreshGraceWindow = TimeSpan.FromDays(30);

        [AllowAnonymous]
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequestDto? dto)
        {
            var rawToken = dto?.Token?.Trim();
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                var header = Request.Headers.Authorization.ToString();
                if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    rawToken = header.Substring("Bearer ".Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(rawToken))
                return Unauthorized(new { message = "Token is required." });

            var key = _config["Jwt:Key"] ?? "THIS_SHOULD_BE_A_LONG_RANDOM_SECRET_KEY";
            var issuer = _config["Jwt:Issuer"] ?? "https://auth.mycompany.com";
            var audience = _config["Jwt:Audience"] ?? "my-apps";

            ClaimsPrincipal principal;
            SecurityToken validatedToken;
            try
            {
                principal = new JwtSecurityTokenHandler().ValidateToken(
                    rawToken,
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = false,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
                    },
                    out validatedToken);
            }
            catch
            {
                return Unauthorized(new { message = "Session is invalid. Please login again." });
            }

            if (validatedToken.ValidTo != DateTime.MinValue &&
                validatedToken.ValidTo.Add(RefreshGraceWindow) < DateTime.UtcNow)
            {
                return Unauthorized(new { message = "Session expired. Please login again." });
            }

            var userIdText = principal.FindFirst("userId")?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (!int.TryParse(userIdText, out var userId) || userId <= 0)
                return Unauthorized(new { message = "Session is invalid. Please login again." });

            var user = await _db.users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || user.IsRejected || !user.IsApproved)
                return Unauthorized(new { message = "Your account is no longer active. Please login again." });

            var token = GenerateJwtToken(user);
            var effectiveRole = !string.IsNullOrWhiteSpace(user.RoleName)
                ? user.RoleName.Trim()
                : user.Role?.Trim() ?? "";

            return Ok(new
            {
                message = "Token refreshed",
                token,
                user = new
                {
                    id = user.Id,
                    userId = user.Id,
                    fullName = user.FullName,
                    email = user.Email,
                    role = effectiveRole,
                    projectId = user.ProjectId,
                    projectName = user.ProjectName,
                    roleName = effectiveRole
                }
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginUserDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Login data is required." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = dto.Email?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { message = "Email and password are required." });

            if (email == "admin@local.com" && dto.Password == "Admin@123")
            {
                var staticAdmin = await _db.users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
                if (staticAdmin == null)
                {
                    staticAdmin = new User
                    {
                        FullName = "Administrator",
                        Email = "admin@local.com",
                        Role = "Admin",
                        RoleName = "Admin",
                        ProjectId = null,
                        ProjectName = "All Projects",
                        IsApproved = true,
                        IsRejected = false,
                        PasswordHash = ""
                    };
                    _db.users.Add(staticAdmin);
                    await _db.SaveChangesAsync();
                }
                else if (staticAdmin.RoleName != "Admin" || staticAdmin.Role != "Admin" || !staticAdmin.IsApproved || staticAdmin.IsRejected)
                {
                    staticAdmin.Role = "Admin";
                    staticAdmin.RoleName = "Admin";
                    staticAdmin.ProjectId = null;
                    staticAdmin.ProjectName = "All Projects";
                    staticAdmin.IsApproved = true;
                    staticAdmin.IsRejected = false;
                    await _db.SaveChangesAsync();
                }

                var staticToken = GenerateJwtToken(staticAdmin);

                return Ok(new
                {
                    message = "Static admin login successful",
                    token = staticToken,
                    user = new
                    {
                        id = staticAdmin.Id,
                        fullName = staticAdmin.FullName,
                        email = staticAdmin.Email,
                        role = staticAdmin.Role,
                        projectId = staticAdmin.ProjectId,
                        projectName = staticAdmin.ProjectName,
                        roleName = staticAdmin.RoleName
                    }
                });
            }

            User? user;

            try
            {
                user = await _db.users.FirstOrDefaultAsync(u => u.Email == email);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Database error while checking login user.",
                    details = ex.Message
                });
            }

            if (user == null)
                return Unauthorized(new { message = "Invalid Email Id" });

            if (user.IsRejected)
                return Unauthorized(new { message = "Your account has been rejected. Please contact admin." });

            bool isPasswordValid;

            try
            {
                isPasswordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            }
            catch
            {
                return StatusCode(500, new
                {
                    message = "Invalid stored password hash. Please reset password."
                });
            }

            if (!isPasswordValid)
                return Unauthorized(new { message = "Invalid Password" });

            if (!user.IsApproved)
                return Unauthorized(new { message = "Your account is pending admin approval." });

            var effectiveRole = !string.IsNullOrWhiteSpace(user.RoleName)
                ? user.RoleName.Trim()
                : user.Role?.Trim() ?? "";
            var token = GenerateJwtToken(user);

            return Ok(new
            {
                message = "Login Successful",
                token,
                user = new
                {
                    id = user.Id,
                    fullName = user.FullName,
                    email = user.Email,
                    role = effectiveRole,
                    projectId = user.ProjectId,
                    projectName = user.ProjectName,
                    roleId = user.RoleId,
                    roleName = effectiveRole
                }
            });
        }


        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            return Ok(new { message = "Logged out successfully." });
        }



        [HttpPost("send-otp")]
        public async Task<IActionResult> SendOtp([FromBody] EmailDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Email))
                return BadRequest(new { message = "Email is required" });

            var email = model.Email.Trim().ToLowerInvariant();

            var registeredUserExists = await _db.users
            .AnyAsync(u => u.Email.ToLower() == email);

            if (registeredUserExists)
                return BadRequest(new { message = "Email already exists. Please login instead." });

            var otp = new Random().Next(100000, 999999).ToString();

            var existingOtp = await _db.otpVerifications.FirstOrDefaultAsync(o => o.Email == email);

            if (existingOtp != null)
            {
                existingOtp.OtpCode = otp;
                existingOtp.ExpiryTime = DateTime.UtcNow.AddMinutes(5);
            }
            else
            {
                var otpEntry = new OtpVerification
                {
                    Email = email,
                    OtpCode = otp,
                    ExpiryTime = DateTime.UtcNow.AddMinutes(5)
                };

                await _db.otpVerifications.AddAsync(otpEntry);
            }

            await _db.SaveChangesAsync();

            try
            {
                await _emailService.SendEmailAsync(
                    email,
                    "Your OTP Code",
                    $"<h2>Your OTP Code</h2><p><strong>{otp}</strong></p><p>Use this code to complete your registration.</p>"
                );

                return Ok(new { message = "OTP sent successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "OTP saved but email sending failed",
                    details = ex.Message
                });
            }
        }

        private async Task<bool> SendEmailAsync(string toEmail, string otp)
        {
            try
            {
                var fromAddress = new MailAddress(_emailSettings.FromEmail, _emailSettings.DisplayName);
                var toAddress = new MailAddress(toEmail);

                using var smtp = new SmtpClient
                {
                    Host = _emailSettings.SmtpHost,
                    Port = _emailSettings.SmtpPort,
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_emailSettings.SmtpUsername, _emailSettings.SmtpPassword)
                };

                var message = new MailMessage(fromAddress, toAddress)
                {
                    Subject = "Your OTP Code",
                    Body = $"<h2>Your OTP Code</h2><p><strong>{otp}</strong></p><p>Use this code to complete your verification.</p>",
                    IsBodyHtml = true
                };

                await smtp.SendMailAsync(message);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending OTP email: " + ex.Message);
                return false;
            }
        }


        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] OtpDto model)
        {
            var otpEntry = await _db.otpVerifications
                .Where(o => o.Email == model.Email && o.OtpCode == model.Otp)
                .OrderByDescending(o => o.ExpiryTime)
                .FirstOrDefaultAsync();

            if (otpEntry == null || otpEntry.ExpiryTime < DateTime.UtcNow)
                return BadRequest(new { message = "Invalid or expired OTP" });

            // ✅ Update user table instead of removing OTP directly
            var user = await _db.users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user != null)
            {
                user.IsOtpVerified = true;
                await _db.SaveChangesAsync();
            }

            _db.otpVerifications.Remove(otpEntry);
            await _db.SaveChangesAsync();

            return Ok(new { message = "OTP verified successfully" });
        }

        [HttpGet("projects")]
        public async Task<IActionResult> GetProjects()
        {
            var projects = await _db.Projects
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.Code })
                .ToListAsync();

            return Ok(projects);
        }

        [HttpGet("projects/{projectId:int}/roles")]
        public async Task<IActionResult> GetPublicRoles(int projectId)
        {
            var roles = await _db.ProjectRoles
                .Where(x =>
                    x.ProjectId == projectId &&
                    x.IsPublicRegistrationAllowed &&
                    x.RoleName != "Admin")
                .OrderBy(x => x.RoleName)
                .Select(x => new { x.Id, x.RoleName })
                .ToListAsync();

            return Ok(roles);
        }

        // 🔹 User Registration API
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] CreateUserDto dto)
        {
            if (!ModelState.IsValid)
            {
                var firstError = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault();

                return BadRequest(new
                {
                    message = string.IsNullOrWhiteSpace(firstError)
                        ? "Validation failed"
                        : firstError
                });
            }

            dto.Email = dto.Email.ToLower();

            var existingUser = await _db.users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (existingUser != null)
            {
                if (!existingUser.IsOtpVerified)
                    return BadRequest("Email not verified. Please verify OTP before registration.");

                return BadRequest("Email already registered");
            }

            var project = await _db.Projects.FirstOrDefaultAsync(x => x.Id == dto.ProjectId && x.IsActive);
            if (project == null)
                return BadRequest(new { message = "Invalid project selected." });

            var role = await _db.ProjectRoles.FirstOrDefaultAsync(x =>
                x.Id == dto.RoleId &&
                x.ProjectId == dto.ProjectId &&
                x.IsPublicRegistrationAllowed);

            if (role == null)
                return BadRequest(new { message = "Invalid role selected for this project." });

            if (string.Equals(role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Admin cannot be created from public registration." });

            var requestedRole = role.RoleName;

            var user = new User
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                IsOtpVerified = true,
                IsApproved = false,
                IsRejected = false,
                Role = "Pending",
                RequestedRole = requestedRole,
                ProjectId = project.Id,
                ProjectName = project.Name,
                RoleId = role.Id,
                RoleName = role.RoleName
            };

            await _db.users.AddAsync(user);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Registration successful. Your account is pending admin approval." });
        }


        [HttpGet("pending-users")]
        [Authorize]
        public async Task<IActionResult> GetPendingUsers()
        {
            try
            {
                var query = _db.users
                    .AsNoTracking()
                    .Where(u => u.IsApproved == false && u.RequestedRole != null && !u.IsRejected)
                    .AsQueryable();

                List<int>? managedProjectIds = null;
                if (!_access.IsAdmin)
                {
                    managedProjectIds = await _access.ManagedProjectIdQuery().ToListAsync();
                    if (!managedProjectIds.Any())
                        return Ok(Array.Empty<object>());

                    query = query.Where(u =>
                        (u.ProjectId.HasValue &&
                         managedProjectIds.Contains(u.ProjectId.Value)) ||
                        _db.UserProjectAccesses.Any(access =>
                            access.UserId == u.Id &&
                            managedProjectIds.Contains(access.ProjectId) &&
                            !access.IsApproved &&
                            !access.IsRejected));
                }

                var users = await query.ToListAsync();
                var userIds = users.Select(x => x.Id).ToList();
                var pendingAccessRows = await _db.UserProjectAccesses
                    .AsNoTracking()
                    .Include(x => x.Project)
                    .Where(x =>
                        userIds.Contains(x.UserId) &&
                        !x.IsApproved &&
                        !x.IsRejected)
                    .ToListAsync();

                if (managedProjectIds != null)
                {
                    pendingAccessRows = pendingAccessRows
                        .Where(x => managedProjectIds.Contains(x.ProjectId))
                        .ToList();
                }

                var pendingUsers = users
                    .Select(u =>
                    {
                        var scopedAccess = pendingAccessRows
                            .Where(x => x.UserId == u.Id)
                            .OrderBy(x => x.ProjectId)
                            .FirstOrDefault();

                        return new
                        {
                            u.Id,
                            u.FullName,
                            u.Email,
                            RequestedRole = scopedAccess?.RoleName ?? u.RequestedRole,
                            ProjectId = scopedAccess?.ProjectId ?? u.ProjectId,
                            ProjectName = scopedAccess?.Project?.Name ?? u.ProjectName,
                            RoleId = scopedAccess?.RoleId ?? u.RoleId,
                            RoleName = scopedAccess?.RoleName ?? u.RoleName,
                        };
                    })
                    .Where(u =>
                        _access.IsAdmin ||
                        (u.ProjectId.HasValue &&
                         managedProjectIds!.Contains(u.ProjectId.Value)))
                    .ToList();

                return Ok(pendingUsers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }

        }

        [HttpPost("approve-user/{userId}")]
        [Authorize]
        public async Task<IActionResult> ApproveUser(
            int userId,
            [FromBody] ApproveUserRequest? request = null)
        {
            try
            {
                var user = await _db.users.FindAsync(userId);
                if (user == null)
                    return NotFound("User not found");

                if (user.IsApproved)
                    return Ok(new { message = "User is already approved." });

                if (string.IsNullOrWhiteSpace(user.RequestedRole))
                    return BadRequest(new { message = "Requested role is missing." });

                if (!_access.IsAdmin &&
                    string.Equals(user.RequestedRole, "Admin", StringComparison.OrdinalIgnoreCase))
                    return Forbid();

                var approvedProjectId = request?.ProjectId ?? user.ProjectId;
                if (!approvedProjectId.HasValue || approvedProjectId.Value <= 0)
                    return BadRequest(new { message = "Please select project before approving user." });

                var project = await _db.Projects.FirstOrDefaultAsync(x =>
                    x.Id == approvedProjectId.Value &&
                    x.IsActive);
                if (project == null)
                    return BadRequest(new { message = "Selected project is invalid or inactive." });

                if (!await _access.CanManageProjectAsync(project.Id))
                    return Forbid();

                var approvedRole = await _db.ProjectRoles.FirstOrDefaultAsync(x =>
                    x.ProjectId == project.Id &&
                    x.RoleName == user.RequestedRole);
                if (approvedRole == null)
                    return BadRequest(new { message = "Requested role is not available for selected project." });

                user.Role = approvedRole.RoleName;
                user.RoleName = approvedRole.RoleName;
                user.RoleId = approvedRole.Id;
                user.ProjectId = project.Id;
                user.ProjectName = project.Name;
                user.RequestedRole = null;
                user.IsApproved = true;
                user.IsRejected = false;
                user.ApprovedAt = DateTime.UtcNow;

                var access = await _db.UserProjectAccesses.FirstOrDefaultAsync(x =>
                    x.UserId == user.Id &&
                    x.ProjectId == project.Id);
                if (access == null)
                {
                    access = new Models.Access.UserProjectAccess
                    {
                        UserId = user.Id,
                        ProjectId = project.Id,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _db.UserProjectAccesses.Add(access);
                }

                access.RoleId = approvedRole.Id;
                access.RoleName = approvedRole.RoleName;
                access.IsActive = true;
                access.IsApproved = true;
                access.IsRejected = false;
                access.UpdatedAtUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                // ✅ Send approval email asynchronously
                _ = SendApprovalEmailAsync(user.Email, user.FullName, user.Role);

                return Ok(new { message = "User approved successfully." });
            }
            catch (Exception ex)
            {

                return StatusCode(500, $"Internal Server Error: {ex.Message}");

            }
        }

        public class ApproveUserRequest
        {
            public int? ProjectId { get; set; }
        }

        private async Task SendApprovalEmailAsync(string toEmail, string fullName, string role)
        {
            try
            {
                var fromAddress = new MailAddress(_emailSettings.FromEmail, _emailSettings.DisplayName);
                var toAddress = new MailAddress(toEmail);

                using var smtp = new SmtpClient
                {
                    Host = _emailSettings.SmtpHost,
                    Port = _emailSettings.SmtpPort,
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_emailSettings.SmtpUsername, _emailSettings.SmtpPassword)
                };

                var mailMessage = new MailMessage(fromAddress, toAddress)
                {
                    Subject = "Account Approved",
                    Body = $@"
                <h2>Congratulations, {fullName}!</h2>
                <p>Your account has been approved with the role: <strong>{role}</strong>.</p>
                <p>You can now login and start using the system.</p>
                <br/>
                <p>Best Regards,<br/>Survey Admin Team</p>",
                    IsBodyHtml = true
                };

                await smtp.SendMailAsync(mailMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending approval email: " + ex.Message);
            }
        }


        [HttpPost("reject-user/{userId}")]
        [Authorize]
        public async Task<IActionResult> RejectUser(
            int userId,
            [FromBody] RejectUserRequest? request = null)
        {
            try
            {
                var user = await _db.users.FindAsync(userId);
                if (user == null)
                    return NotFound("User not found");

                var rejectedProjectId = request?.ProjectId ?? user.ProjectId;
                if (!_access.IsAdmin)
                {
                    if (!rejectedProjectId.HasValue ||
                        !await _access.CanManageProjectAsync(rejectedProjectId.Value))
                        return Forbid();
                }

                if (rejectedProjectId.HasValue)
                {
                    var access = await _db.UserProjectAccesses.FirstOrDefaultAsync(x =>
                        x.UserId == user.Id &&
                        x.ProjectId == rejectedProjectId.Value);

                    if (access != null)
                    {
                        access.IsRejected = true;
                        access.IsApproved = false;
                        access.IsActive = false;
                        access.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }

                if (!user.IsApproved ||
                    !rejectedProjectId.HasValue ||
                    user.ProjectId == rejectedProjectId.Value)
                {
                    user.IsRejected = true;
                    user.IsApproved = false;
                    user.RequestedRole = null;
                }

                await _db.SaveChangesAsync();

                return Ok("User rejected and removed successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }

        public class RejectUserRequest
        {
            public int? ProjectId { get; set; }
        }

        [HttpDelete("delete-user/{userId}")]
        [Authorize]
        public async Task<IActionResult> DeleteUser(int userId)
        {
            var user = await _db.users.FindAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
                return Forbid();

            if (!_access.IsAdmin)
            {
                var managedProjectIds = _access.ManagedProjectIdQuery();
                var canManageUser = (user.ProjectId.HasValue &&
                                     managedProjectIds.Contains(user.ProjectId.Value)) ||
                                    await _db.UserProjectAccesses.AnyAsync(access =>
                                        access.UserId == userId &&
                                        managedProjectIds.Contains(access.ProjectId));

                if (!canManageUser)
                    return Forbid();
            }

            var assignments = await _db.SurveyAssignments
                .Where(x => x.SurveyorUserId == userId || x.AssignedByUserId == userId)
                .ToListAsync();
            if (assignments.Count > 0)
                _db.SurveyAssignments.RemoveRange(assignments);

            try
            {
                _db.users.Remove(user);
                await _db.SaveChangesAsync();
                return Ok(new { message = "User deleted successfully." });
            }
            catch (DbUpdateException)
            {
                return Conflict(new
                {
                    message = "This user has historical survey or audit records and cannot be permanently deleted."
                });
            }
        }

        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgetPassDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Email))
                return BadRequest(new { message = "Email is required." });

            var email = model.Email.Trim().ToLowerInvariant();

            var user = await _db.users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
                return NotFound(new { message = "User not found" });

            var otp = new Random().Next(100000, 999999).ToString();

            user.OTP = otp;
            user.OTPExpiration = DateTime.UtcNow.AddMinutes(5);
            await _db.SaveChangesAsync();

            try
            {
                await _emailService.SendEmailAsync(
                    user.Email,
                    "Password Reset OTP",
                    $"<h2>Password Reset</h2><p>Your OTP code is <strong>{otp}</strong>.</p><p>This code is valid for 5 minutes.</p>"
                );

                return Ok(new { message = "OTP sent successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "OTP generated but email sending failed.",
                    details = ex.Message
                });
            }
        }


        [HttpPost("VerifyOtp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto model)
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null || user.OTP != model.OTP || user.OTPExpiration < DateTime.UtcNow)
                return BadRequest("Invalid or expired OTP");

            return Ok(new { message = "OTP verified successfully" });
        }


        [HttpPost("ResendOtp")]
        public async Task<IActionResult> ResendOtp([FromBody] ResendDto model)
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null)
                return NotFound("User not found");

            // Generate new OTP
            var newOtp = new Random().Next(100000, 999999).ToString();

            // Store OTP in the database
            user.OTP = newOtp;
            user.OTPExpiration = DateTime.UtcNow.AddMinutes(5); // OTP valid for 5 mins
            await _db.SaveChangesAsync();

            // Send new OTP via email
            await _emailService.SendEmailAsync(user.Email, "Resend OTP", $"Your new OTP is: {newOtp}");

            return Ok(new { message = "New OTP sent successfully" });
        }



        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null)
                return NotFound("User not found");

            if (user.OTP != model.OTP)
                return BadRequest("Invalid OTP");

            // Hash new password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            user.OTP = null; // Clear OTP safely

            await _db.SaveChangesAsync();
            return Ok(new { message = "Password reset successfully" });
        }


        [HttpPost("ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePass model)
        {
            // ✅ Validate Model
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid input data." });
            }

            // ✅ Debugging - Check received UserId
            Console.WriteLine($"Received UserId: {model.UserId}");

            if (model.UserId == null || model.UserId <= 0)
            {
                return BadRequest(new { message = "Invalid UserId." });
            }

            // ✅ Fetch user from database
            var user = await _db.users.FindAsync(model.UserId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            // ✅ Verify old password safely
            try
            {
                if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash))
                {
                    return BadRequest(new { message = "Old password is incorrect." });
                }
            }
            catch (BCrypt.Net.SaltParseException ex)
            {
                Console.WriteLine("BCrypt error: " + ex.Message);
                return StatusCode(500, new { message = "Invalid password format. Please contact support." });
            }

            // ✅ Update password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password changed successfully!" });
        }


        [HttpGet("GetProfile/{userId}")]
        public async Task<IActionResult> GetProfile(int userId)
        {
            var user = await _db.users.FindAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(user);
        }



        [HttpPost("UpdateProfile")]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto model)
        {

            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            Console.WriteLine("ModelState Errors: " + string.Join(", ", errors));

            if (!ModelState.IsValid)
                return BadRequest("Invalid input data.");

            var user = await _db.users.FindAsync(model.UserId);
            if (user == null)
                return NotFound("User not found");

            user.FullName = model.FullName;
            user.Email = model.Email;
            user.Role = model.Role;

            try
            {
                await _db.SaveChangesAsync();
                return Ok(new { success = true, message = "Profile updated successfully!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Error saving changes.", error = ex.Message });
            }

        }
        [HttpGet("surveyors")]
        public async Task<IActionResult> GetSurveyors()
        {
            var surveyors = await _db.users
                .Where(u => u.IsApproved == true && u.Role == "Surveyor")
                .OrderBy(u => u.FullName)
                .Select(u => new
                {
                    id = u.Id,
                    fullName = u.FullName,
                    email = u.Email,
                    role = u.Role
                })
                .ToListAsync();

            return Ok(surveyors);
        }

        [HttpGet("managers")]
        public async Task<IActionResult> GetManagers()
        {
            var ihmProjectId = await _access.ResolveProjectIdAsync("IHM");

            var managersQuery = _db.users
                .AsNoTracking()
                .Where(u => u.IsApproved == true && !u.IsRejected);

            if (ihmProjectId.HasValue)
            {
                var projectId = ihmProjectId.Value;
                managersQuery = managersQuery.Where(u =>
                    _db.UserProjectAccesses.Any(access =>
                        access.UserId == u.Id &&
                        access.ProjectId == projectId &&
                        access.IsActive &&
                        access.IsApproved &&
                        !access.IsRejected &&
                        access.RoleName.ToLower() == "manager") ||
                    (u.ProjectId == projectId &&
                     ((u.RoleName ?? "").ToLower() == "manager" ||
                      (u.Role ?? "").ToLower() == "manager")));
            }
            else
            {
                managersQuery = managersQuery.Where(u =>
                    ((u.RoleName ?? "").ToLower() == "manager" ||
                     (u.Role ?? "").ToLower() == "manager"));
            }

            var managers = await managersQuery
                .OrderBy(u => u.FullName)
                .Select(u => new
                {
                    id = u.Id,
                    fullName = u.FullName,
                    email = u.Email,
                    role = u.Role
                })
                .ToListAsync();

            return Ok(managers);
        }


    }
}

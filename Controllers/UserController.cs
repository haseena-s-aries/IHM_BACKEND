using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Survey_Certification_App.Data;
using Survey_Certification_App.DTOS;
using Survey_Certification_App.IemailserviceInterface;
using Survey_Certification_App.Models;
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

        public UserController(ApplicationDbContext db, IEmailService emailService, IConfiguration config, IWebHostEnvironment env)
        {
            _db = db;
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _emailSettings = config.GetSection("EmailSettings").Get<EmailSettings>();
            _env = env;
        }

        private string GenerateJwtToken(User user)
        {
            var key = "THIS_SHOULD_BE_A_LONG_RANDOM_SECRET_KEY";
            var issuer = "https://auth.mycompany.com";
            var audience = "my-apps";

            var claims = new[]
            {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email ?? ""),
        new Claim(ClaimTypes.Name, user.FullName ?? ""),
        new Claim(ClaimTypes.Role, user.Role ?? ""),
        new Claim("role", user.Role ?? "")
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
                var staticAdmin = new User
                {
                    Id = 999999,
                    FullName = "Static Admin",
                    Email = "admin@local.com",
                    Role = "Admin",
                    IsApproved = true,
                    PasswordHash = ""
                };

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
                        role = staticAdmin.Role
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
                    role = user.Role
                }
            });
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

            var requestedRole = string.IsNullOrWhiteSpace(dto.Role) ? "User" : dto.Role.Trim();

            var user = new User
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                IsOtpVerified = true,
                IsApproved = false,
                IsRejected = false,
                Role = "Pending",
                RequestedRole = requestedRole
            };

            await _db.users.AddAsync(user);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Registration successful. Your account is pending admin approval." });
        }


        [HttpGet("pending-users")]
        public async Task<IActionResult> GetPendingUsers()
        {
            try
            {
                var pendingUsers = await _db.users
                    .Where(u => u.IsApproved == false && u.RequestedRole != null)
                    .Select(u => new
                    {
                        u.Id,
                        u.FullName,
                        u.Email,
                        u.RequestedRole,

                    })
                    .ToListAsync();

                return Ok(pendingUsers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }

        }

        [HttpPost("approve-user/{userId}")]
        public async Task<IActionResult> ApproveUser(int userId)
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

                user.Role = user.RequestedRole;
                user.RequestedRole = null;
                user.IsApproved = true;
                user.IsRejected = false;
                user.ApprovedAt = DateTime.UtcNow;

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
        public async Task<IActionResult> RejectUser(int userId)
        {
            try
            {
                var user = await _db.users.FindAsync(userId);
                if (user == null)
                    return NotFound("User not found");

                user.IsRejected = true;
                user.IsApproved = false;
                user.RequestedRole = null;
                await _db.SaveChangesAsync();

                return Ok("User rejected and removed successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }

        [HttpDelete("delete-user/{userId}")]
        public async Task<IActionResult> DeleteUser(int userId)
        {
            var user = await _db.users.FindAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

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
            var managers = await _db.users
                .Where(u => u.IsApproved == true && u.Role == "Manager")
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

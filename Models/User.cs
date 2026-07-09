using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        // ******** Basic Info ******** 

        [Required, MaxLength(100)]
        public string FullName { get; set; }

        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; set; }

        [MaxLength(100)]
        public string? EmailNormalized { get; set; }

        [MaxLength(50)]
        public string? Username { get; set; }

        [MaxLength(50)]
        public string? UsernameNormalized { get; set; }

        // ******** Authentication ******** 

        [Required]
        public string PasswordHash { get; set; }   // hashed password

        [Required]
        public string Role { get; set; } = "User";   // effective role

        public string? RequestedRole { get; set; }    // "User", "Manager", "Admin"

        public int? ProjectId { get; set; }

        [MaxLength(50)]
        public string? ProjectName { get; set; }

        public int? RoleId { get; set; }

        [MaxLength(50)]
        public string? RoleName { get; set; }

        public int? CreatedByUserId { get; set; }

        public bool IsApproved { get; set; } = false; // default false (must be approved)
        public bool IsRejected { get; set; } = false;

        // ******** Employee Information ******** 

        [MaxLength(100)]
        public string? EmployeeId { get; set; }

        [MaxLength(200)]
        public string? FieldName { get; set; }   // department, unit, etc.

        // ******** OTP ******** 

        public bool IsOtpVerified { get; set; } = false;

        public string? OTP { get; set; }

        public DateTime? OTPExpiration { get; set; }

        // ******** Audit Info ******** 

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? ApprovedBy { get; set; }      // admin user id

        public DateTime? ApprovedAt { get; set; }

        // ******** Constructor ******** 

        public User()
        {
        }
    }
}

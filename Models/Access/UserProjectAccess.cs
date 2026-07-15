using System.ComponentModel.DataAnnotations;
using Survey_Certification_App.Models;

namespace Survey_Certification_App.Models.Access
{
    public class UserProjectAccess
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int ProjectId { get; set; }

        public int? RoleId { get; set; }

        [Required, MaxLength(50)]
        public string RoleName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public bool IsApproved { get; set; } = true;

        public bool IsRejected { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public User? User { get; set; }

        public Project? Project { get; set; }

        public ProjectRole? Role { get; set; }
    }
}

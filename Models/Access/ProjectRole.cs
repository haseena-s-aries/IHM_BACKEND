using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models.Access
{
    public class ProjectRole
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        [Required, MaxLength(50)]
        public string RoleName { get; set; } = string.Empty;

        public bool IsPublicRegistrationAllowed { get; set; } = true;

        public Project? Project { get; set; }
    }
}
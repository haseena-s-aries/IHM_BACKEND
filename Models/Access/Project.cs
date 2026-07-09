using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models.Access
{
    public class Project
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
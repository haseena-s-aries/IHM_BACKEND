namespace Survey_Certification_App.Models
{
    using System.ComponentModel.DataAnnotations;


    public class IhmCompany
    {
        public int Id { get; set; }

        [MaxLength(150)]
        public string? Country { get; set; }

        [MaxLength(200)]
        public string? CityLocation { get; set; }

        [Required, MaxLength(300)]
        public string CompanyName { get; set; } = "";
        [MaxLength(300)]
        public string? DisplayName { get; set; }
        public string? FullAddress { get; set; }

        [MaxLength(100)]
        public string? Tel { get; set; }

        [MaxLength(100)]
        public string? Fax { get; set; }

        [MaxLength(300)]
        public string? Website { get; set; }

        [MaxLength(300)]
        public string? Email { get; set; }

        public string? LogoPath { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
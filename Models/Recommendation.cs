using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models
{
    public class Recommendation
    {
        [Key]
        public int Id { get; set; }
        public string? RecommendationText { get; set; }
    }
}

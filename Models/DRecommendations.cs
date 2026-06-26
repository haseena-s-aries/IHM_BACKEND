using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models
{
    public class DRecommendations
    {
        [Key]
        public int Id { get; set; }
        public string? RecommendationText { get; set; }
    }
}

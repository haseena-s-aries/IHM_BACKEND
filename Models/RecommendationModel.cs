using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models
{
    public class RecommendationModel
    {
        [Key]
        public int SerialNo { get; set; }
        public string Description { get; set; }
    }
}

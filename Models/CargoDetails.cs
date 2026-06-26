using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Survey_Certification_App.Models
{
    public class CargoDetails
    {
        [Key]
        public int Id { get; set; }

        public int? SerialNo { get; set; }
        public string? Location { get; set; }
        public string? Cargos { get; set; }
        public string? IDMarkings { get; set; }
        public string? Weight { get; set; }
        public string? Dimensions { get; set; }
        public string? Remarks { get; set; }




    }
}

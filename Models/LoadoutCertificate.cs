using System.ComponentModel.DataAnnotations;

namespace Survey_Certification_App.Models
{
    public class LoadoutCertificate
    {

        [Key]
        public int Id { get; set; }

        public string HeaderImagePath { get; set; }  // 👈 Add this

        [Required(ErrorMessage = "CertificateTitle is required.")]
        [MaxLength(100)]
        public string CertificateTitle { get; set; }


        [Required(ErrorMessage = "OperationType is required.")]
        [MaxLength(100)]
        public string OperationType { get; set; }

        [Required(ErrorMessage = "JobNo Number is required.")]
        [MaxLength(10)]

        public string JobNumber { get; set; }

        [Required(ErrorMessage = "ProjectTitle is required.")]
        [MaxLength(100)]
        public string ProjectTitle { get; set; }

        [Required(ErrorMessage = "Certificate Number is required.")]
        [MaxLength(60)]
        public string CertificateNumber { get; set; }

        [Required(ErrorMessage = "ClientName is required.")]
        [MaxLength(60)]
        public string Client { get; set; }

        [Required(ErrorMessage = "Cargo is required.")]
        [MaxLength(60)]

        public string Cargo { get; set; }

        [Required(ErrorMessage = "mode of  is required.")]
        [MaxLength(100)]
        public string Modeofoperation { get; set; }

        [Required(ErrorMessage = "mode of  is required.")]
        [MaxLength(100)]
        public string BargeVessel { get; set; }


        [Required(ErrorMessage = "IssuedBy is required.")]
        [MaxLength(60)]
        public string IssuedBy { get; set; }


        [Required(ErrorMessage = "Location is required.")]
        [MaxLength(100)]
        public string Location { get; set; }

        public DateOnly? IssuedDate { get; set; }

        public string PdfFilePath { get; set; }

        public bool NoDateCheckbox { get; set; } // This must be bound from the view

        public string StampPath { get; set; } // Store uploaded stamp path

        public string SignaturePath { get; set; }

        public string SignatureClient { get; set; }

        public bool IncludeCargoDetails { get; set; }

        public bool IsCargoDetailsNA { get; set; }
        public List<CargoDetails> CargoDetails { get; set; } = new(); // ✅ Add this




    }
}
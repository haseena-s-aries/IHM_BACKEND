using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Survey_Certification_App.Models
{
    public class Certificate
    {
        [Key]
        public int Id { get; set; }

   
        public string HeaderImagePath { get; set; } // For image dropdown


        [Required(ErrorMessage = "CertificateTitle is required.")]
       
        public string CertificateType { get; set; }

        [Required(ErrorMessage = "JobNo Number is required.")]
       
        public string JobNumber { get; set; }

        [NotMapped]
        public List<DRecommendations> defaultRecommendations { get; set; }
        //public List<Recommendation> Recommendations { get; set; }


        public List<CargoDetails> CargoDetails { get; set; } = new(); // ✅ Add this

       [ Required(ErrorMessage = "ProjectTitle is required.")]
   

        public string ProjectTitle { get; set; }
        [Required(ErrorMessage = "Certificate Number is required.")]
       


        public string CertificateNumber { get; set; }

        [Required(ErrorMessage = "ClientName is required.")]
       
        public string ClientName { get; set; }

        [Required(ErrorMessage = "Cargo is required.")]
      
        public string? Cargo { get; set; }

        [Required(ErrorMessage = "FromPort is required.")]
       
        public string? DeparturePort { get; set; }

        [Required(ErrorMessage = "FromPort is required.")]
      
        public string? ArrivalPort { get; set; }

        public DateOnly? IssuedDate { get; set; }

        public bool NoDateCheckbox { get; set; } // This must be bound from the view

         public string PdfFilePath { get; set; } 


        [Required(ErrorMessage = "IssuedBy is required.")]
        
        public string SurveyedBy { get; set; }

        [Required(ErrorMessage = "reviewed is required.")]
       

        public string ReviewedBy { get; set; }

        [Required(ErrorMessage = "IssuedBy is required.")]
      
        public string? TowName { get; set; }

        [Required(ErrorMessage = "IssuedBy is required.")]
      
        public string? TugName { get; set; }
        public string? StampPath { get; set; }
        public string? SignaturePath { get; set; }
        public string? SignatureClient { get; set; }


        //public bool IsCreateMode { get; set; } // set true when in create mode, false when in view/edit
        public bool IsCargoDetailsNA { get; set; }
        public bool IncludeAnnexure2 { get; set; }

        public bool IncludeCargoDetails { get; set; }

   

    }

}

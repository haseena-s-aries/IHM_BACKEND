namespace Survey_Certification_App.Models
{
    public class EmailSettings
    {

        public string FromEmail { get; set; }
        public string DisplayName { get; set; }
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; }
        public string SmtpUsername { get; set; }
        public string SmtpPassword { get; set; }
    }
}

namespace Survey_Certification_App.IemailserviceInterface
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body);
    }
}


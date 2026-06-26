using MimeKit;



namespace Survey_Certification_App.IemailserviceInterface
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                var senderEmail = _configuration.GetValue<string>("EmailSettings:FromEmail");
                var smtpServer = _configuration.GetValue<string>("EmailSettings:SmtpHost");
                var port = _configuration.GetValue<int>("EmailSettings:SmtpPort");
                var enableSSL = true; // Always true for Brevo
                var smtpUsername = _configuration.GetValue<string>("EmailSettings:SmtpUsername");
                var smtpPassword = _configuration.GetValue<string>("EmailSettings:SmtpPassword");

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Survey Certification Team", senderEmail));
                message.To.Add(MailboxAddress.Parse(to));
                message.Subject = subject;
                message.Body = new TextPart("html") // ✅ HTML Body
                {
                    Text = body
                };

                using (var client = new MailKit.Net.Smtp.SmtpClient())
                {
                    var socketOption = port == 465
      ? MailKit.Security.SecureSocketOptions.SslOnConnect
      : MailKit.Security.SecureSocketOptions.StartTls;

                    await client.ConnectAsync(smtpServer, port, socketOption);


                    await client.AuthenticateAsync(smtpUsername, smtpPassword);
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Email Sending Failed: " + ex.Message); // ✅ Log exact error!
                throw; // Important so you can catch this in your controller if needed
            }
        }
    }
}
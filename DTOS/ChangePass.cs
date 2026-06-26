namespace Survey_Certification_App.DTOS
{
    public class ChangePass
    {
        public int? UserId { get; set; }

        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }
    }
}

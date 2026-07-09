namespace Survey_Certification_App.Models.Access
{
    public class ClientVesselMapping
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int VesselId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
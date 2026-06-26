using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;

namespace Survey_Certification_App.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public NotificationsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet("users/{userId:int}")]
        public async Task<IActionResult> GetUserNotifications(
            int userId,
            [FromQuery] bool unreadOnly = false)
        {
            var query = _db.UserNotifications
                .Where(x => x.UserId == userId);

            if (unreadOnly)
                query = query.Where(x => !x.IsRead);

            var notifications = await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => new
                {
                    x.Id,
                    x.UserId,
                    x.Type,
                    x.Title,
                    x.Message,
                    x.SurveyJobId,
                    x.IsRead,
                    x.CreatedAt,
                    x.ReadAt
                })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpPost("{notificationId:int}/read")]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            var notification = await _db.UserNotifications.FindAsync(notificationId);
            if (notification == null)
                return NotFound("Notification not found.");

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Notification marked as read." });
        }

        [HttpPost("users/{userId:int}/read-all")]
        public async Task<IActionResult> MarkAllAsRead(int userId)
        {
            var notifications = await _db.UserNotifications
                .Where(x => x.UserId == userId && !x.IsRead)
                .ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var notification in notifications)
            {
                notification.IsRead = true;
                notification.ReadAt = now;
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "All notifications marked as read." });
        }
    }
}

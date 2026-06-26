using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Survey_Certification_App.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]


    public class SecureTestController : ControllerBase
    {
        [HttpGet("hello")]
        public IActionResult Hello()
        {
            return Ok(new { message = "JWT is VALID! Welcome." });
        }
    }
}
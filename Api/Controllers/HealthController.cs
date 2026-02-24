// Simple health check for availability checks (e.g. load balancers, monitoring).
using Microsoft.AspNetCore.Mvc;

namespace SpeechInsight.Api.Controllers;

[Route("api")]
[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Get() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });
}

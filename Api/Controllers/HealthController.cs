// Health check for load balancers and deploy verification.
// Returns gitSha baked into the image (ENV GIT_SHA) so CI can prove the running app matches origin/main.
using Microsoft.AspNetCore.Mvc;

namespace SpeechInsight.Api.Controllers;

[Route("api")]
[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Get()
    {
        var gitSha = Environment.GetEnvironmentVariable("GIT_SHA") ?? "unknown";
        return Ok(new
        {
            status = "ok",
            timestamp = DateTime.UtcNow,
            gitSha,
            // Short stamp for humans / UI checks
            build = gitSha.Length >= 7 ? gitSha[..7] : gitSha
        });
    }
}

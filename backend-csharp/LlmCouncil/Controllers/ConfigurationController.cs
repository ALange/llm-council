using LlmCouncil.Models;
using LlmCouncil.Services;
using Microsoft.AspNetCore.Mvc;

namespace LlmCouncil.Controllers;

[ApiController]
[Route("api/configuration")]
public class ConfigurationController(CouncilConfigurationService configurationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CouncilConfigurationResponse>> GetConfiguration()
    {
        return await configurationService.GetConfigurationAsync();
    }

    [HttpPut]
    public async Task<ActionResult<CouncilConfigurationResponse>> SaveConfiguration([FromBody] CouncilPortalConfig config)
    {
        return await configurationService.SaveConfigurationAsync(config);
    }
}

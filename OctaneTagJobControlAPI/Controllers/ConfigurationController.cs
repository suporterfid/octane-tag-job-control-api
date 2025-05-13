using Microsoft.AspNetCore.Mvc;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services;

namespace OctaneTagJobControlAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigurationController : ControllerBase
    {
        private readonly ILogger<ConfigurationController> _logger;
        private readonly JobConfigurationService _configService;

        public ConfigurationController(
            ILogger<ConfigurationController> logger,
            JobConfigurationService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<JobConfiguration>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllConfigurations()
        {
            var configs = await _configService.GetAllConfigurationsAsync();
            return Ok(configs);
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(JobConfiguration), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetConfiguration(string id)
        {
            var config = await _configService.GetConfigurationAsync(id);
            if (config == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Configuration with ID {id} not found"
                });
            }

            return Ok(config);
        }

        [HttpPost]
        [ProducesResponseType(typeof(JobConfiguration), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateConfiguration([FromBody] JobConfiguration config)
        {
            try
            {
                var createdConfig = await _configService.CreateConfigurationAsync(config);

                return CreatedAtAction(
                    nameof(GetConfiguration),
                    new { id = createdConfig.Id },
                    createdConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating configuration");
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error creating configuration: {ex.Message}"
                });
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(JobConfiguration), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UpdateConfiguration(string id, [FromBody] JobConfiguration config)
        {
            try
            {
                if (_configService.GetConfigurationAsync(id) == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Configuration with ID {id} not found"
                    });
                }

                var updatedConfig = await _configService.UpdateConfigurationAsync(id, config);

                return Ok(updatedConfig);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Configuration with ID {id} not found"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating configuration {ConfigId}", id);
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error updating configuration: {ex.Message}"
                });
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> DeleteConfiguration(string id)
        {
            try
            {
                if (_configService.GetConfigurationAsync(id) == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Configuration with ID {id} not found"
                    });
                }

                var success = await _configService.DeleteConfigurationAsync(id);

                if (success)
                {
                    return NoContent();
                }
                else
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Failed to delete configuration with ID {id}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting configuration {ConfigId}", id);
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error deleting configuration: {ex.Message}"
                });
            }
        }
    }
}

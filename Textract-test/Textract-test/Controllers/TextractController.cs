using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using Textract_test.Models;
using Textract_test.Services;

namespace Textract_test.Controllers
{
    [ApiController]
    [Route("api/textract")]
    public class TextractController : ControllerBase
    {
        private readonly ITextractService _service;
        private readonly AwsSettingsOptions _awsSettings;

        public TextractController(ITextractService service, IOptions<AwsSettingsOptions> awsSettings)
        {
            _service = service;
            _awsSettings = awsSettings.Value;
        }

        [HttpGet]
        public async Task<ActionResult<TextractDocument>> GetTextractAnalysis([FromQuery]string filename)
        {
            try
            {
                return Ok(await _service.AnalyzeDocument(_awsSettings.S3BucketName, filename));
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }
    }
}

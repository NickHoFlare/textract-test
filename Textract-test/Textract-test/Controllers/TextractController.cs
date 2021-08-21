using Amazon.Textract.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Textract_test.Services;

namespace Textract_test.Controllers
{
    [ApiController]
    [Route("api/textract")]
    public class TextractController : ControllerBase
    {
        private readonly ITextractService _service;
        private readonly IConfiguration _config;

        public TextractController(ITextractService service, IConfiguration config)
        {
            _service = service;
            _config = config;
        }

        [HttpGet]
        public async Task<ActionResult<AnalyzeDocumentResponse>> GetTextractAnalysis([FromQuery]string filename)
        {
            return Ok(await _service.AnalyzeDocumentAsync(_config["s3BucketName"], filename));
        }
    }
}

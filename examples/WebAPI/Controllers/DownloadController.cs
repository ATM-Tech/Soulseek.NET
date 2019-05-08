﻿namespace WebAPI.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Soulseek;

    /// <summary>
    ///     Search
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1")]
    [ApiController]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class DownloadController : ControllerBase
    {
        private ISoulseekClient Client { get; }

        public DownloadController(ISoulseekClient client)
        {
            Client = client;
        }

        /// <summary>
        ///     Retrieves the files shared by the specified <paramref name="username"/>.
        /// </summary>
        /// <param name="username">The user from which to download.</param>
        /// <param name="filename">The file to download.</param>
        /// <param name="token">The optional download token.</param>
        /// <returns></returns>
        [HttpGet("{username}/{filename}")]
        public async Task<FileContentResult> Get([FromRoute, Required]string username, [FromRoute, Required]string filename, [FromQuery]int? token)
        {
            var fileBytes = await Client.DownloadAsync(username, filename, token);
            return File(fileBytes, "application/octet-stream", Path.GetFileName(filename));
        }
    }
}

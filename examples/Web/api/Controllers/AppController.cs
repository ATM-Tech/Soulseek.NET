﻿namespace WebAPI.Controllers
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.IdentityModel.Tokens;
    using System;
    using System.Collections.Generic;
    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;
    using WebAPI.DTO;

    /// <summary>
    ///     Application
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1")]
    [ApiController]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class AppController : ControllerBase
    {
        public AppController()
        {
        }

        /// <summary>
        ///     Logs in.
        /// </summary>
        /// <param name="login"></param>
        /// <returns></returns>
        [HttpPost]
        [AllowAnonymous]
        public IActionResult Login([FromBody]LoginRequest login)
        {
            if (login == default)
            {
                return BadRequest();
            }

            if (string.IsNullOrWhiteSpace(login.Username) || string.IsNullOrWhiteSpace(login.Password))
            {
                return BadRequest("Username and/or Password missing or invalid");
            }

            if (login.Username == Startup.Username && login.Password == Startup.Password)
            {
                return Ok(new TokenResponse(GetJwtSecurityToken()));
            }

            return Unauthorized();
        }

        private JwtSecurityToken GetJwtSecurityToken()
        {
            var issuedUtc = DateTime.UtcNow;
            var expiresUtc = DateTime.UtcNow.AddMilliseconds(Startup.JwtTTL);

            var claims = new List<Claim>()
            {
                new Claim(ClaimTypes.Name, Startup.Username),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "User"),
                new Claim("name", Startup.Username),
                new Claim("iat", ((DateTimeOffset)issuedUtc).ToUnixTimeSeconds().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Startup.JwtSigningKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

            var token = new JwtSecurityToken(
                issuer: "slsk-web-example",
                claims: claims,
                notBefore: issuedUtc,
                expires: expiresUtc,
                signingCredentials: credentials);

            return token;
        }
    }
}

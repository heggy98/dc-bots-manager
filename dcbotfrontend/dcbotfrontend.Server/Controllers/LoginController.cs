using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

public class LoginController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public LoginController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)] // Zmenené na 401
    [HttpPost("Login")]
    public IActionResult Login(string username, string password)
    {
        // 1. Overiť užívateľa z databázy a overiť heslo (použiť hashovanie!)
        if (username == "domcajekral" && password == "heslo_ktory_je_zahashovany") // Toto je len ilustrácia!
        {
            // 2. Vygenerovať JWT token
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, username)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(1), // Platnosť tokenu
                signingCredentials: creds
            );

            return Ok(new { Token = new JwtSecurityTokenHandler().WriteToken(token) });
        }

        return Unauthorized(); // Vrátiť 401 Unauthorized pri neúspešnom prihlásení
    }

    [Authorize] // Vyžaduje platný JWT token
    [HttpGet("ProtectedResource")]
    public IActionResult GetProtectedResource()
    {
        var username = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Ok($"Prístup povolený pre užívateľa: {username}");
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using BotManager.Backend.Entities;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BotManager.Backend.Services.Interfaces;

public class LoginController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IBruteforceProtectionService _bruteforceService;
    private readonly IPasswordHasherService _passwordHasher;

    public LoginController(IConfiguration configuration, IBruteforceProtectionService bruteforceService, IPasswordHasherService passwordHasher)
    {
        _configuration = configuration;
        _bruteforceService = bruteforceService;
        _passwordHasher = passwordHasher;
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)] // Zmenené na 401
    [HttpPost("Login")]
    public IActionResult Login([FromBody] LoginRequest request) // Doporučeno přijímat data z body
    {
        // Získání IP adresy pro ochranu proti Bruteforce
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (ipAddress != null && _bruteforceService.IsLocked(ipAddress))
        {
            return Unauthorized("Účet je dočasně uzamčen kvůli příliš mnoha neúspěšným pokusům.");
        }

        // 1. Ověření uživatele a hesla z konfigurace
        var adminUser = _configuration.GetSection("AdminUsers").Get<List<User>>()?
            .FirstOrDefault(u => u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase));

        if (adminUser == null || !_passwordHasher.VerifyPassword(request.Password, adminUser.HashedPassword))
        {
            if (ipAddress != null) _bruteforceService.RegisterFailure(ipAddress);
            return Unauthorized("Neplatné jméno nebo heslo.");
        }

        if (ipAddress != null) _bruteforceService.RegisterSuccess(ipAddress);

        // 2. Vygenerovat JWT token S CLAIMEM 'Admin'
        var claims = new[]
        {
        new Claim(ClaimTypes.NameIdentifier, request.Username),
        new Claim(ClaimTypes.Role, "Admin") // Klíčový claim pro autorizaci
    };

        // ... (Zde následuje tvoje stávající logika pro generování tokenu, která je OK)
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key")));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddHours(1),
            signingCredentials: creds
        );

        return Ok(new { Token = new JwtSecurityTokenHandler().WriteToken(token) });
    }

    [Authorize] // Vyžaduje platný JWT token
    [HttpGet("ProtectedResource")]
    public IActionResult GetProtectedResource()
    {
        var username = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Ok($"Prístup povolený pre užívateľa: {username}");
    }
}
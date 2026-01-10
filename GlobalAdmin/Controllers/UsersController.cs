using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GlobalAdmin.Services;

namespace GlobalAdmin.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(UserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAdminUsers()
    {
        var users = await _userService.GetAdminUsersAsync();
        return Ok(users.Select(u => new
        {
            email = u
        }));
    }

    [HttpPost]
    public async Task<IActionResult> AddAdmin([FromBody] AddAdminRequest request)
    {
        try
        {
            var success = await _userService.AddAdminUserAsync(request.Email);
            if (success)
                return Ok(new { message = $"Admin user {request.Email} added" });
            return BadRequest(new { error = "Failed to add admin user" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{email}")]
    public async Task<IActionResult> RemoveAdmin(string email)
    {
        try
        {
            var decodedEmail = Uri.UnescapeDataString(email);
            var success = await _userService.RemoveAdminUserAsync(decodedEmail);
            if (success)
                return Ok(new { message = $"Admin user {decodedEmail} removed" });
            return BadRequest(new { error = "Failed to remove admin user" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record AddAdminRequest(string Email);

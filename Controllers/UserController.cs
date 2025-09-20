using DIP.Backend.Interfaces;
using DIP.Backend.Models;
using Microsoft.AspNetCore.Mvc;

namespace DIP.Backend.Controllers;


[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserRepository _users;

    public UserController(IUserRepository users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<User>>> list()
    {
        try
        {
            var users = await _users.GetAllAsync();
            return Ok(users);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<User>> GetUser(int id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<User>> CreateUser([FromBody] User input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Email))
        {
            return BadRequest(new { message = "Name and Email are required." });
        }

        var user = new User
        {
            Name = input.Name.Trim(), Email = input.Email.Trim()
        };

        await _users.AddAsync(user);
        
        return CreatedAtAction(nameof(GetUser), new {id = user.Id}, user);
    }
}
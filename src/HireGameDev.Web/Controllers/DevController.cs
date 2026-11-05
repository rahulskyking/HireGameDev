using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
[Route("Dev")]
public sealed class DevController : Controller
{
    private readonly IWebHostEnvironment _environment;

    public DevController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet("Styleguide")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Styleguide()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return View();
    }
}

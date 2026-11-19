using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.ViewModels.Shared;

namespace OscCommittee.Web.Controllers;

[Route("error")]
public sealed class ErrorController : Controller
{
    [Route("{statusCode:int?}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index(int? statusCode)
    {
        var code = statusCode ?? StatusCodes.Status500InternalServerError;
        Response.StatusCode = code;

        var model = code switch
        {
            404 => new ErrorViewModel
            {
                StatusCode = 404,
                Title = "Page not found",
                Message = "That page does not exist. Check the address, or head back to your dashboard."
            },
            403 => new ErrorViewModel
            {
                StatusCode = 403,
                Title = "Access denied",
                Message = "Your account does not have access to this page."
            },
            429 => new ErrorViewModel
            {
                StatusCode = 429,
                Title = "Too many attempts",
                Message = "You tried that too many times. Wait a few minutes, then try again."
            },
            _ => new ErrorViewModel
            {
                StatusCode = code,
                Title = "Something went wrong",
                Message = "The request could not be completed. Try again, and tell a board member if it keeps happening.",
                RequestId = HttpContext.TraceIdentifier
            }
        };

        return View(model);
    }
}

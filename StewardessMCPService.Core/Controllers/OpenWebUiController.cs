// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPService.Services;
using System.Text;

namespace StewardessMCPService.Controllers;

/// <summary>
///     Generates a callable Python <c>Tools</c> wrapper for Open WebUI from the
///     live OpenAPI specification of this service.
///
///     GET /api/openwebui-tools          — returns the Python source as text/plain
///     GET /api/openwebui-tools?format=download — same content with Content-Disposition: attachment
/// </summary>
[Route("api/openwebui-tools")]
public sealed class OpenWebUiController : BaseController
{
    private OpenWebUiToolsGenerator Generator => GetService<OpenWebUiToolsGenerator>();

    /// <summary>
    ///     Returns a Python <c>Tools</c> class for Open WebUI that wraps every
    ///     REST endpoint exposed by this service.  The file is generated on-the-fly
    ///     from the live OpenAPI document so it always reflects the current API.
    ///
    ///     Paste the output directly into the Open WebUI "Tools" editor, or save
    ///     it as a <c>.py</c> file and import it.
    /// </summary>
    /// <param name="format">
    ///     Optional.  Pass <c>download</c> to receive the file as a downloadable
    ///     attachment (<c>stewardess_tools.py</c>) instead of inline text.
    /// </param>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetPythonTools([FromQuery] string? format = null)
    {
        try
        {
            var python = Generator.Generate();
            var bytes = Encoding.UTF8.GetBytes(python);

            if (string.Equals(format, "download", StringComparison.OrdinalIgnoreCase))
            {
                return File(bytes, "text/x-python; charset=utf-8", "stewardess_tools.py");
            }

            return Content(python, "text/plain; charset=utf-8");
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }
}

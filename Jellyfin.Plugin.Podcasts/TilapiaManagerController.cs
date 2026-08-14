using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Podcasts;

[ApiController]
[AllowAnonymous]
[Route("Tilapia")]
public sealed class TilapiaManagerController : ControllerBase
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ApplyHeaders(true);
        using var stream = OpenResource("index.html");
        if (stream is null) return NotFound();
        using var reader = new StreamReader(stream);
        var managerBase = $"{Request.PathBase}/Tilapia/";
        return Content(reader.ReadToEnd().Replace("__TILAPIA_BASE__", managerBase, StringComparison.Ordinal), "text/html; charset=utf-8");
    }

    [HttpGet("manager.css")]
    public IActionResult Css() => Resource("manager.css", "text/css; charset=utf-8", true);

    [HttpGet("manager.js")]
    public IActionResult JavaScript() => Resource("manager.js", "text/javascript; charset=utf-8", true);

    [HttpGet("icon.png")]
    public IActionResult Icon() => Resource("tilapia-icon.png", "image/png", false);

    private IActionResult Resource(string name, string contentType, bool noStore)
    {
        ApplyHeaders(noStore);
        var stream = OpenResource(name);
        return stream is null ? NotFound() : File(stream, contentType);
    }

    private void ApplyHeaders(bool noStore)
    {
        Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' https: http: data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'self'";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.CacheControl = noStore ? "no-store" : "public, max-age=86400";
    }

    private static Stream? OpenResource(string name)
        => Assembly.GetExecutingAssembly().GetManifestResourceStream($"{typeof(TilapiaManagerController).Namespace}.Manager.{name}");
}

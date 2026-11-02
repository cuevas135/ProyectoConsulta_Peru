using System.Net;
using System.Security.Claims;
using ConsultaPeru.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace ConsultaPeru.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccesoActividadModel(AccesoActividad acceso, IWebHostEnvironment environment) : PageModel
{
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; private set; }
    private bool Local => environment.IsDevelopment() && HttpContext.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
    public IActionResult OnGet() => !Local ? NotFound() : Page();
    public async Task<IActionResult> OnPostAsync()
    {
        if (!Local) return NotFound();
        var resultado = acceso.Verificar(Password);
        Password = "";
        ModelState.Clear();
        if (resultado == ResultadoAcceso.Permitido)
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Administrador local") }, "ActividadLocal");
            await HttpContext.SignInAsync("ActividadLocal", new ClaimsPrincipal(identidad),
                new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
            return RedirectToPage("/Actividad");
        }
        Error = resultado switch {
            ResultadoAcceso.Bloqueado => "Demasiados intentos. Espera un minuto y vuelve a intentarlo.",
            ResultadoAcceso.SinConfigurar => "La contraseña administrativa no está configurada.",
            _ => "Contraseña incorrecta."
        };
        return Page();
    }
}

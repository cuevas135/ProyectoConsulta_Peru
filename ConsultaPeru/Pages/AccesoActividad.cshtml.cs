using System.Net;
using System.Security.Claims;
using ConsultaPeru.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace ConsultaPeru.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccesoActividadModel(AccesoActividad acceso, IWebHostEnvironment environment, IConfiguration config) : PageModel
{
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Usuario { get; set; } = "";
    [BindProperty] public string Codigo { get; set; } = "";
    public bool RequiereSegundoFactor => !Local;
    public string? Error { get; private set; }
    private bool Local => environment.IsDevelopment() && HttpContext.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
    private bool Disponible => Local || (acceso.ProduccionHabilitada && Request.IsHttps);
    public IActionResult OnGet() => !Disponible ? NotFound() : Page();
    public async Task<IActionResult> OnPostAsync()
    {
        if (!Disponible) return NotFound();
        var resultado = Local ? acceso.Verificar(Password) : acceso.VerificarProduccion(Usuario, Password, Codigo,
            config["Actividad:Directorio"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "Actividad"));
        Password = "";
        Codigo = "";
        ModelState.Clear();
        if (resultado == ResultadoAcceso.Permitido)
        {
            var identidad = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.Name, "Administrador"),
                new Claim("ActividadSello", acceso.Sello),
                new Claim("ActividadSegundoFactor", Local ? "local" : "totp") }, "ActividadLocal");
            await HttpContext.SignInAsync("ActividadLocal", new ClaimsPrincipal(identidad),
                new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
            return RedirectToPage("/Actividad");
        }
        Error = resultado switch {
            ResultadoAcceso.Bloqueado => Local ? "Demasiados intentos. Espera un minuto." : "Acceso temporalmente bloqueado. Espera 15 minutos y vuelve a intentarlo.",
            ResultadoAcceso.SinConfigurar => "El acceso administrativo no está disponible. Revisa la configuración del servidor.",
            _ => "Credenciales incorrectas o código vencido."
        };
        return Page();
    }
}

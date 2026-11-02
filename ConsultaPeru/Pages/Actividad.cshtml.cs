using System.Net;
using ConsultaPeru.Actividad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace ConsultaPeru.Pages;
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = "ActividadLocal")]
public class ActividadModel(IRegistroActividad registro, IWebHostEnvironment environment) : PageModel
{
    public IReadOnlyList<EventoActividad> Eventos { get; private set; } = [];
    public EstadoRegistro Salud => registro.Estado;
    public string? ErrorLectura { get; private set; }
    public long Visitas => Eventos.LongCount(e => e.Tipo == TipoEvento.Visita);
    public long Busquedas => Eventos.LongCount(e => e.Tipo == TipoEvento.Busqueda);
    public long ConResultados => Eventos.LongCount(e => e.Tipo == TipoEvento.Busqueda && e.Estado == EstadoConsulta.ConResultados.ToString());
    public long SinResultados => Eventos.LongCount(e => e.Tipo == TipoEvento.Busqueda && e.Estado == EstadoConsulta.SinResultados.ToString());
    public long Incidencias => Eventos.LongCount(e => e.Tipo == TipoEvento.Incidencia);
    public async Task<IActionResult> OnGetAsync()
    {
        // No publicar este panel hasta implementar autenticación de administrador.
        if (!environment.IsDevelopment() || HttpContext.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)) return NotFound();
        try { Eventos = await registro.LeerAsync(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException or Microsoft.VisualBasic.FileIO.MalformedLineException)
        { ErrorLectura = "No se pudo leer el registro completo. Revisa los archivos de actividad y sus permisos; no se han borrado."; }
        return Page();
    }
    public static string HoraPeru(DateTimeOffset fecha) => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(fecha, "SA Pacific Standard Time").ToString("dd/MM/yyyy HH:mm:ss");
    public async Task<IActionResult> OnPostSalirAsync()
    {
        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(HttpContext, "ActividadLocal");
        return RedirectToPage("/Index");
    }
}

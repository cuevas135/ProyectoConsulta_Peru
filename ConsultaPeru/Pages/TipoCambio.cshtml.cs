using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace ConsultaPeru.Pages;
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TipoCambioModel(Services.TipoCambioService service) : PageModel
{
    public static DateOnly HoyPeru => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "SA Pacific Standard Time").DateTime);
    [BindProperty] public DateOnly? Fecha { get; set; } = HoyPeru;
    public Services.TipoCambio? Resultado { get; private set; }
    public string? Error { get; private set; }
    public string OrigenConsulta { get; private set; } = "";
    public Actividad.EstadoConsulta EstadoBusqueda { get; private set; } = Actividad.EstadoConsulta.Validacion;
    public void OnGet() { }
    public async Task OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid || Fecha is null) { Error = "Selecciona una fecha válida."; return; }
        if (Fecha > HoyPeru) { Error = "Selecciona una fecha de hoy o anterior."; return; }
        var respuesta = await service.Consultar(Fecha.Value, ct);
        Resultado = respuesta.Datos; Error = respuesta.Error;
        OrigenConsulta = respuesta.Datos is not null ? respuesta.Origen : "";
        EstadoBusqueda = Resultado is not null ? Actividad.EstadoConsulta.ConResultados : respuesta.Estado;
    }
}

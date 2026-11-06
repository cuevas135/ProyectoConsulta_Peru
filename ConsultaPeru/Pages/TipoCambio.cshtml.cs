using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace ConsultaPeru.Pages;
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TipoCambioModel(Services.TipoCambioService service, Services.ResultadosTemporales resultados) : PageModel
{
    public static DateOnly HoyPeru => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "SA Pacific Standard Time").DateTime);
    [BindProperty] public DateOnly? Fecha { get; set; } = HoyPeru;
    public Services.TipoCambio? Resultado { get; private set; }
    public string? Error { get; private set; }
    public string OrigenConsulta { get; private set; } = "";
    public Actividad.EstadoConsulta EstadoBusqueda { get; private set; } = Actividad.EstadoConsulta.Validacion;
    public void OnGet(string? consulta)
    {
        if (consulta is null) return;
        var guardado = resultados.Leer<ResultadoGuardado>(HttpContext, consulta);
        if (guardado is null) { Error = "El resultado temporal venció. Realiza una nueva consulta."; return; }
        Fecha = guardado.Fecha;
        Resultado = guardado.Cotizacion;
        Error = guardado.Error;
    }
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid || Fecha is null) { Error = "Selecciona una fecha válida."; return GuardarResultado(); }
        if (Fecha > HoyPeru) { Error = "Selecciona una fecha de hoy o anterior."; return GuardarResultado(); }
        var respuesta = await service.Consultar(Fecha.Value, ct);
        Resultado = respuesta.Datos; Error = respuesta.Error;
        OrigenConsulta = respuesta.Datos is not null ? respuesta.Origen : "";
        EstadoBusqueda = Resultado is not null ? Actividad.EstadoConsulta.ConResultados : respuesta.Estado;
        return GuardarResultado();
    }
    private IActionResult GuardarResultado() => RedirectToPage("/TipoCambio", new {
        consulta = resultados.Guardar(HttpContext, new ResultadoGuardado(Fecha, Resultado, Error))
    });
    private sealed record ResultadoGuardado(DateOnly? Fecha, Services.TipoCambio? Cotizacion, string? Error);
}

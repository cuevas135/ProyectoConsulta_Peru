using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConsultaPeru.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(Services.ConsultaService service, Services.ResultadosTemporales resultados) : PageModel
{
    [BindProperty] public string Tipo { get; set; } = "DNI";
    [BindProperty] public string Numero { get; set; } = "";
    public Services.Documento? Resultado { get; private set; }
    public string? Error { get; private set; }
    public Actividad.EstadoConsulta EstadoBusqueda { get; private set; } = Actividad.EstadoConsulta.Validacion;
    public void OnGet(string? consulta)
    {
        if (consulta is null) return;
        var guardado = resultados.Leer<ResultadoGuardado>(HttpContext, consulta);
        if (guardado is null) { Error = "El resultado temporal venció. Realiza una nueva consulta."; return; }
        Tipo = guardado.Tipo;
        Numero = guardado.Numero;
        Resultado = guardado.Documento;
        Error = guardado.Error;
    }
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Numero = (Numero ?? "").Trim();
        if (Tipo is not ("DNI" or "RUC")) { Tipo = "DNI"; Error = "Selecciona un tipo de documento válido."; return GuardarResultado(); }
        var length = Tipo == "DNI" ? 8 : 11;
        if (Numero.Length != length || Numero.Any(c => c is < '0' or > '9'))
        { Error = $"Ingresa un {Tipo} de {length} dígitos numéricos."; return GuardarResultado(); }
        var respuesta = await service.Consultar(Tipo, Numero, cancellationToken);
        Resultado = respuesta.Datos;
        Error = respuesta.Error;
        EstadoBusqueda = Resultado is not null ? Actividad.EstadoConsulta.ConResultados : respuesta.Estado;
        return GuardarResultado();
    }
    private IActionResult GuardarResultado() => RedirectToPage("/Index", new {
        consulta = resultados.Guardar(HttpContext, new ResultadoGuardado(Tipo, Numero, Resultado, Error))
    });
    private sealed record ResultadoGuardado(string Tipo, string Numero, Services.Documento? Documento, string? Error);
}

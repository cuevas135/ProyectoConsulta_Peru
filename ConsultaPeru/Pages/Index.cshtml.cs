using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConsultaPeru.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(Services.ConsultaService service) : PageModel
{
    [BindProperty] public string Tipo { get; set; } = "DNI";
    [BindProperty] public string Numero { get; set; } = "";
    public Services.Documento? Resultado { get; private set; }
    public string? Error { get; private set; }
    public Actividad.EstadoConsulta EstadoBusqueda { get; private set; } = Actividad.EstadoConsulta.Validacion;
    public void OnGet()
    {

    }
    public async Task OnPostAsync(CancellationToken cancellationToken)
    {
        Numero = (Numero ?? "").Trim();
        if (Tipo is not ("DNI" or "RUC")) { Tipo = "DNI"; Error = "Selecciona un tipo de documento válido."; return; }
        var length = Tipo == "DNI" ? 8 : 11;
        if (Numero.Length != length || Numero.Any(c => c is < '0' or > '9'))
        { Error = $"Ingresa un {Tipo} de {length} dígitos numéricos."; return; }
        var respuesta = await service.Consultar(Tipo, Numero, cancellationToken);
        Resultado = respuesta.Datos;
        Error = respuesta.Error;
        EstadoBusqueda = Resultado is not null ? Actividad.EstadoConsulta.ConResultados : respuesta.Estado;
    }
}

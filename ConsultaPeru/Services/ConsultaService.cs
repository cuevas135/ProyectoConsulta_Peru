using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
namespace ConsultaPeru.Services;

public sealed record CampoResultado(string Etiqueta, string Valor);
public sealed record Documento(string Numero, string Nombre, string Nombres, string Paterno, string Materno, string Direccion, string Estado, string Condicion)
{
    public IReadOnlyList<CampoResultado> Adicionales { get; init; } = [];
}
public sealed record Respuesta(Documento? Datos, string? Error, Actividad.EstadoConsulta Estado = Actividad.EstadoConsulta.Error);
public sealed class ConsultaService(HttpClient client, IConfiguration config, TimeProvider clock, CredencialProveedor? credencial = null)
{
    public async Task<Respuesta> Consultar(string tipo, string numero, CancellationToken ct)
    {
        var longitud = tipo == "DNI" ? 8 : 11;
        if (tipo is not ("DNI" or "RUC") || numero.Length != longitud || numero.Any(c => c is < '0' or > '9'))
            return new(null, "El documento no tiene un formato válido.");
        var token = credencial?.Token ?? config["ConsultaApi:Token"];
        if (string.IsNullOrWhiteSpace(token)) return new(null, "Falta configurar la credencial del servicio de consultas.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{tipo.ToLowerInvariant()}/{numero}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new(null, response.StatusCode switch {
                HttpStatusCode.NotFound => "No hay información disponible para esta consulta.",
                HttpStatusCode.TooManyRequests => "El proveedor alcanzó su límite de consultas. Intenta más tarde.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "El proveedor rechazó la autorización. Revisa la configuración del servicio.",
                _ => "El proveedor no está disponible en este momento." },
                response.StatusCode == HttpStatusCode.NotFound ? Actividad.EstadoConsulta.SinResultados : Actividad.EstadoConsulta.Error);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(null, "El proveedor devolvió una respuesta inesperada.");
            // No se expone el mensaje remoto ni se confunde un fallo del proveedor con un documento inexistente.
            if (!success.GetBoolean()) return new(null, "No hay información disponible para esta consulta.", Actividad.EstadoConsulta.SinResultados);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return new(null, "El proveedor devolvió una respuesta sin datos válidos.");
            string Read(params string[] keys)
            {
                foreach (var key in keys)
                    if (data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                        return WebUtility.HtmlDecode(v.GetString()!).Trim();
                return "";
            }
            if (tipo == "DNI")
            {
                var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.GetUtcNow(), "SA Pacific Standard Time").DateTime);
                if (!DateOnly.TryParseExact(Read("fecha_nacimiento"), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var nacimiento)
                    || nacimiento > hoy || nacimiento.AddYears(18) > hoy)
                    return new(null, "Esta consulta no está disponible para visualización pública.", Actividad.EstadoConsulta.Restringida);
            }
            var recibido = Read(tipo == "DNI" ? "numero" : "ruc");
            if (recibido != numero) return new(null, "El proveedor devolvió un documento distinto al solicitado.");
            var nombre = Read(tipo == "DNI" ? "nombre_completo" : "nombre_o_razon_social", "name");
            if (string.IsNullOrWhiteSpace(nombre)) return new(null, "El proveedor devolvió una respuesta sin datos válidos.");
            var campos = new (string Key, string Label)[] {
                ("direccion_completa", "Dirección completa"), ("trade_name", "Nombre comercial"),
                ("departamento", "Departamento"), ("provincia", "Provincia"), ("distrito", "Distrito"),
                ("ubigeo_sunat", "Ubigeo SUNAT"), ("es_agente_de_retencion", "Agente de retención"),
                ("es_buen_contribuyente", "Buen contribuyente") };
            // Lista explícita: fecha de nacimiento, sexo, estado civil y otros datos no se envían a la vista.
            return new(new Documento(recibido, nombre, tipo == "DNI" ? Read("nombres") : "",
                tipo == "DNI" ? Read("apellido_paterno") : "", tipo == "DNI" ? Read("apellido_materno") : "",
                tipo == "RUC" ? Read("direccion") : "", tipo == "RUC" ? Read("estado", "state") : "", tipo == "RUC" ? Read("condicion") : "")
                { Adicionales = tipo == "RUC" ? campos.Select(c => new CampoResultado(c.Label, Read(c.Key))).ToArray() : [] }, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(null, "El proveedor tardó demasiado en responder. Intenta nuevamente."); }
        catch (HttpRequestException) { return new(null, "No se pudo conectar con el proveedor. Intenta más tarde."); }
        catch (JsonException) { return new(null, "El proveedor devolvió un formato de respuesta no válido."); }
        catch (InvalidOperationException) { return new(null, "El proveedor devolvió una respuesta inesperada."); }
    }
}

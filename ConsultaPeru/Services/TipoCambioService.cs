using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
namespace ConsultaPeru.Services;

public sealed record TipoCambio(DateOnly Fecha, decimal Compra, decimal Venta, string Moneda);
public sealed record RespuestaCambio(TipoCambio? Datos, string? Error, Actividad.EstadoConsulta Estado = Actividad.EstadoConsulta.Error, string Origen = "API");
public sealed class TipoCambioService(HttpClient client, IConfiguration config, CredencialProveedor? credencial = null,
    ICacheTipoCambio? cache = null, ILogger<TipoCambioService>? logger = null)
{
    public async Task<RespuestaCambio> Consultar(DateOnly fecha, CancellationToken ct)
    {
        try
        {
            return cache is null ? await ConsultarApi(fecha, ct)
                : await cache.ObtenerAsync(fecha, token => ConsultarApi(fecha, token), ct);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.VisualBasic.FileIO.MalformedLineException)
        {
            logger?.LogError("No se pudo acceder a la caché de tipo de cambio. Tipo de fallo: {Tipo}", ex.GetType().Name);
            return new(null, "No se pudo leer o guardar el CSV de tipo de cambio. Revisa sus permisos y formato.");
        }
    }
    private async Task<RespuestaCambio> ConsultarApi(DateOnly fecha, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"tipo-cambio-dia/{fecha:yyyy-MM-dd}");
        var token = credencial?.Token ?? config["ConsultaApi:Token"];
        if (string.IsNullOrWhiteSpace(token)) return new(null, "Falta configurar la credencial del servicio de consultas.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new(null, response.StatusCode switch {
                HttpStatusCode.TooManyRequests => "El proveedor alcanzó su límite de solicitudes. Intenta más tarde.",
                HttpStatusCode.NotFound => "No hay un tipo de cambio disponible para esa fecha.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "El proveedor requiere autorización. Revisa la configuración del servicio.",
                _ => "No se pudo obtener el tipo de cambio. Intenta más tarde." },
                response.StatusCode == HttpStatusCode.NotFound ? Actividad.EstadoConsulta.SinResultados : Actividad.EstadoConsulta.Error);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var envelope = json.RootElement;
            if (envelope.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("success", out var success) || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(null, "El proveedor devolvió una respuesta inesperada.");
            if (!success.GetBoolean()) return new(null, "No hay cotizaciones disponibles para esa fecha.", Actividad.EstadoConsulta.SinResultados);
            if (!envelope.TryGetProperty("data", out var root) || root.ValueKind != JsonValueKind.Object)
                return new(null, "El proveedor devolvió una respuesta sin cotizaciones válidas.");
            decimal? Read(string key) => root.TryGetProperty(key, out var v) && decimal.TryParse(v.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
            if (root.ValueKind != JsonValueKind.Object || Read("compra") is not { } compra || Read("venta") is not { } venta || compra <= 0 || venta <= 0)
                return new(null, "El proveedor no devolvió cotizaciones válidas para esa fecha.");
            if (!(root.TryGetProperty("fecha_sunat", out var f) || root.TryGetProperty("fecha", out f)) || !DateOnly.TryParseExact(f.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var publicada))
                return new(null, "El proveedor no indicó una fecha válida para la cotización.");
            return new(new TipoCambio(publicada, compra, venta, root.TryGetProperty("moneda", out var m) ? m.GetString() ?? "USD" : "USD"), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(null, "El proveedor tardó demasiado en responder."); }
        catch (HttpRequestException) { return new(null, "No se pudo conectar con el proveedor."); }
        catch (JsonException) { return new(null, "El proveedor devolvió un formato no válido."); }
        catch (InvalidOperationException) { return new(null, "El proveedor devolvió una respuesta inesperada."); }
    }
}


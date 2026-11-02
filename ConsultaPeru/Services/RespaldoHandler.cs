using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConsultaPeru.Services;

// Convierte únicamente el esquema de api.apis.net.pe al contrato normalizado de AQPFACT.
public sealed class RespaldoHandler(IHttpClientFactory factory, IConfiguration config,
    Actividad.IRegistroActividad? registro = null, IHttpContextAccessor? accessor = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage? principal = null;
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(7));
            var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
            principal = await base.SendAsync(request, limite.Token);
            var (respaldo, estado) = await NecesitaRespaldo(principal, ct);
            await Registrar(request, "AQPFACT", estado,
                (int)principal.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
            if (!respaldo) return principal;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { await Registrar(request, "AQPFACT", "Timeout"); }
        catch (HttpRequestException) { await Registrar(request, "AQPFACT", "ErrorConexion"); }

        ct.ThrowIfCancellationRequested();
        if (!config.GetValue("ConsultaApi:Respaldo:Habilitado", true))
            return principal ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var ruta = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (ruta.Length < 2) return principal ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var tipo = ruta[^2];
        var valor = ruta[^1];
        var endpoint = tipo switch {
            "dni" or "ruc" => $"{tipo}?numero={Uri.EscapeDataString(valor)}",
            "tipo-cambio-dia" => $"tipo-cambio-sunat?fecha={Uri.EscapeDataString(valor)}",
            _ => null
        };
        if (endpoint is null) return principal ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        try
        {
            using var secundario = new HttpRequestMessage(HttpMethod.Get, endpoint);
            // Nunca se reenvía la clave de AQPFACT al otro proveedor.
            if (config["ConsultaApi:Respaldo:Token"] is { Length: > 0 } token)
                secundario.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
            using var legacy = await factory.CreateClient("ApiAnterior").SendAsync(secundario, ct);
            await Registrar(request, "ApiAnterior", legacy.IsSuccessStatusCode ? "RespuestaRecibida" : "RespuestaNoDisponible",
                (int)legacy.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
            if (!legacy.IsSuccessStatusCode) return principal ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            using var json = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return principal ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var data = new JsonObject();
            void Map(string destino, string origen)
            {
                if (root.TryGetProperty(origen, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    data[destino] = JsonNode.Parse(v.GetRawText());
            }
            if (tipo == "tipo-cambio-dia")
            {
                Map("compra", "compra"); Map("venta", "venta"); Map("fecha_sunat", "fecha"); Map("moneda", "moneda");
            }
            else
            {
                if (!root.TryGetProperty("nombre", out var nombre) || nombre.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nombre.GetString()))
                    return principal ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                Map(tipo == "dni" ? "numero" : "ruc", "numeroDocumento");
                Map(tipo == "dni" ? "nombre_completo" : "nombre_o_razon_social", "nombre");
                Map("nombres", "nombres"); Map("apellido_paterno", "apellidoPaterno"); Map("apellido_materno", "apellidoMaterno");
                // Si el respaldo no informa nacimiento, ConsultaService restringe el DNI en el servidor.
                Map("fecha_nacimiento", "fecha_nacimiento");
                foreach (var key in new[] {"direccion", "estado", "condicion", "departamento", "provincia", "distrito"}) Map(key, key);
                Map("ubigeo_sunat", "ubigeo");
            }
            principal?.Dispose();
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(new JsonObject { ["success"] = true, ["data"] = data }.ToJsonString(), Encoding.UTF8, "application/json")
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { await Registrar(request, "ApiAnterior", "Timeout"); }
        catch (HttpRequestException) { await Registrar(request, "ApiAnterior", "ErrorConexion"); }
        catch (JsonException) { await Registrar(request, "ApiAnterior", "JsonInvalido"); }
        return principal ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    private async Task Registrar(HttpRequestMessage request, string proveedor, string estado, int? codigo = null, long duracion = 0)
    {
        if (registro is null) return;
        var partes = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var operacion = partes.Length > 1 ? partes[^2] switch {
            "dni" => "DNI", "ruc" => "RUC", "tipo-cambio-dia" => "TipoCambioDia", _ => "Otra"
        } : "Otra";
        var id = accessor?.HttpContext?.Items["ActividadSolicitudId"] as string ?? Guid.NewGuid().ToString("N");
        await registro.GuardarAsync(new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, id,
            Actividad.TipoEvento.IntentoApi, operacion, estado, proveedor, duracion, codigo));
        if (estado is "Timeout" or "ErrorConexion" or "JsonInvalido" or "RespuestaInvalida" || codigo is >= 500 or 401 or 403 or 408 or 429)
            await registro.GuardarAsync(new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, id,
                Actividad.TipoEvento.Incidencia, operacion, estado, proveedor, duracion, codigo));
    }

    private static async Task<(bool Respaldo, string Estado)> NecesitaRespaldo(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            return (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || (int)response.StatusCode >= 500, "RespuestaNoDisponible");
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success)
                || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return (true, "RespuestaInvalida");
            if (success.ValueKind == JsonValueKind.False) return (true, "SinInformacion");
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.EnumerateObject().Any()) return (true, "RespuestaInvalida");
            return (false, "RespuestaRecibida");
        }
        catch (JsonException) { return (true, "JsonInvalido"); }
    }
}

using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;
namespace ConsultaPeru.Services;

public interface ICacheTipoCambio
{
    Task<RespuestaCambio> ObtenerAsync(DateOnly fecha, Func<CancellationToken, Task<RespuestaCambio>> consultarApi, CancellationToken ct,
        Func<DateOnly, CancellationToken, Task<RespuestaCambio>>? consultarAnterior = null);
}

public sealed class CacheTipoCambioCsv(string directorio, TimeProvider reloj) : ICacheTipoCambio
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private const string Cabecera = "fecha_consultada,fecha_publicada,moneda,compra,venta,guardado_utc";
    private sealed record Fila(DateOnly Consulta, TipoCambio Datos, DateTimeOffset Guardado);
    public async Task<RespuestaCambio> ObtenerAsync(DateOnly fecha, Func<CancellationToken, Task<RespuestaCambio>> consultarApi, CancellationToken ct,
        Func<DateOnly, CancellationToken, Task<RespuestaCambio>>? consultarAnterior = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directorio);
            // El lock de archivo evita consultas duplicadas incluso entre dos procesos locales.
            FileStream? bloqueo = null;
            var inicioBloqueo = System.Diagnostics.Stopwatch.GetTimestamp();
            while (bloqueo is null)
            {
                ct.ThrowIfCancellationRequested();
                try { bloqueo = new FileStream(Path.Combine(directorio, "tipo_cambio.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (System.Diagnostics.Stopwatch.GetElapsedTime(inicioBloqueo) < TimeSpan.FromSeconds(20))
                { await Task.Delay(100, ct); }
            }
            using (bloqueo)
            {
                var archivo = Path.Combine(directorio, "tipo_cambio.csv");
                var filas = Leer(archivo);
                var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(reloj.GetUtcNow(), "SA Pacific Standard Time").DateTime);
                if (filas.TryGetValue(fecha, out var fila))
                {
                    // Una cotización anterior devuelta para hoy es provisional: revisar cada 15 minutos.
                    var provisionalVencida = fecha == hoy && fila.Datos.Fecha != fecha
                        && reloj.GetUtcNow() - fila.Guardado >= TimeSpan.FromMinutes(15);
                    if (!provisionalVencida) return new(fila.Datos, null, Actividad.EstadoConsulta.ConResultados, "CSV");
                }
                var resultado = await consultarApi(ct);
                // Incluye el 502 histórico del proveedor. Autorización, límites y errores de red detienen el retroceso.
                for (var dias = 1; resultado.BuscarAnterior
                    && consultarAnterior is not null && dias <= TipoCambioService.MaxDiasRetroceso && fecha.DayNumber >= dias; dias++)
                {
                    ct.ThrowIfCancellationRequested();
                    var anterior = fecha.AddDays(-dias);
                    resultado = filas.TryGetValue(anterior, out var previa)
                        ? new(previa.Datos, null, Actividad.EstadoConsulta.ConResultados, "CSV")
                        : await consultarAnterior(anterior, ct);
                    if (resultado.Datos is { } encontrada && encontrada.Fecha > anterior)
                        return new(null, "El proveedor devolvió una cotización posterior al día consultado.");
                }
                if (resultado.Datos is not { } datos) return resultado.Estado == Actividad.EstadoConsulta.SinResultados && consultarAnterior is not null
                    ? resultado with { Error = $"No se encontró tipo de cambio para esa fecha ni en los {TipoCambioService.MaxDiasRetroceso} días anteriores." }
                    : resultado; // No cachear errores ni respuestas vacías.
                if (datos.Compra <= 0 || datos.Venta <= 0 || datos.Moneda != "USD" || datos.Fecha > fecha)
                    return new(null, "El proveedor devolvió una cotización no válida para la fecha solicitada.");
                filas[fecha] = new(fecha, datos, reloj.GetUtcNow());
                var temporal = archivo + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    var lineas = new List<string> { Cabecera };
                    lineas.AddRange(filas.Values.OrderBy(f => f.Consulta).Select(f => string.Join(',',
                        f.Consulta.ToString("yyyy-MM-dd"), f.Datos.Fecha.ToString("yyyy-MM-dd"), "USD",
                        f.Datos.Compra.ToString(CultureInfo.InvariantCulture), f.Datos.Venta.ToString(CultureInfo.InvariantCulture),
                        f.Guardado.ToUniversalTime().ToString("O"))));
                    await File.WriteAllLinesAsync(temporal, lineas, new UTF8Encoding(false), ct);
                    File.Move(temporal, archivo, overwrite: true);
                }
                finally { if (File.Exists(temporal)) File.Delete(temporal); }
                return resultado;
            }
        }
        finally { gate.Release(); }
    }
    private static Dictionary<DateOnly, Fila> Leer(string archivo)
    {
        var filas = new Dictionary<DateOnly, Fila>();
        if (!File.Exists(archivo)) return filas;
        using var parser = new TextFieldParser(archivo, Encoding.UTF8);
        parser.SetDelimiters(",");
        var cabecera = parser.ReadFields();
        if (cabecera is null || string.Join(',', cabecera) != Cabecera) throw new InvalidDataException("Cabecera de caché no válida.");
        while (!parser.EndOfData)
        {
            var v = parser.ReadFields();
            if (v is null || v.Length != 6
                || !DateOnly.TryParseExact(v[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var consulta)
                || !DateOnly.TryParseExact(v[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var publicada)
                || v[2] != "USD" || publicada > consulta
                || !decimal.TryParse(v[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var compra) || compra <= 0
                || !decimal.TryParse(v[4], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var venta) || venta <= 0
                || !DateTimeOffset.TryParse(v[5], CultureInfo.InvariantCulture, DateTimeStyles.None, out var guardado))
                throw new InvalidDataException("Fila de caché no válida.");
            filas[consulta] = new(consulta, new(publicada, compra, venta, "USD"), guardado);
        }
        return filas;
    }
}

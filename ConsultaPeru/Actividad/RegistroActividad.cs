using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace ConsultaPeru.Actividad;

public enum EstadoConsulta { ConResultados, SinResultados, Restringida, Validacion, Error, Cancelada }
public enum TipoEvento { Visita, Busqueda, Incidencia, IntentoApi }
public sealed record EventoActividad(
    string Id, DateTimeOffset FechaUtc, string SolicitudId, TipoEvento Tipo,
    string Operacion, string Estado, string Proveedor = "", long DuracionMs = 0,
    int? CodigoHttp = null, string Detalle = "");
public sealed record EstadoRegistro(bool Disponible, long FallosEscritura);
public interface IRegistroActividad
{
    Task GuardarAsync(EventoActividad evento);
    Task<IReadOnlyList<EventoActividad>> LeerAsync();
    EstadoRegistro Estado { get; }
}

// Sustituible por un repositorio PostgreSQL sin cambiar las páginas ni los eventos.
public sealed class RegistroCsv(string directorio, ILogger<RegistroCsv> logger) : IRegistroActividad
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long fallos;
    public EstadoRegistro Estado => new(Interlocked.Read(ref fallos) == 0, Interlocked.Read(ref fallos));
    private const string Cabecera = "version,id,fecha_utc,solicitud_id,tipo,operacion,estado,proveedor,duracion_ms,codigo_http,detalle\r\n";
    public static string Archivo(TipoEvento tipo) => tipo switch {
        TipoEvento.Visita => "visitas.csv", TipoEvento.Busqueda => "busquedas.csv",
        TipoEvento.IntentoApi => "intentos_api.csv", _ => "incidencias.csv"
    };
    public async Task GuardarAsync(EventoActividad e)
    {
        await gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(directorio);
            // Bloqueo de archivo: también protege frente a dos procesos locales.
            FileStream? archivo = null;
            for (var i = 0; i < 10; i++) {
                try { archivo = new FileStream(Path.Combine(directorio, Archivo(e.Tipo)), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 4096, true); break; }
                catch (IOException) when (i < 9) { await Task.Delay(50); }
            }
            await using var stream = archivo!;
            var nuevo = stream.Length == 0;
            stream.Seek(0, SeekOrigin.End);
            var valores = new[] { "1", e.Id, e.FechaUtc.ToUniversalTime().ToString("O"), e.SolicitudId,
                e.Tipo.ToString(), e.Operacion, e.Estado, e.Proveedor,
                e.DuracionMs.ToString(CultureInfo.InvariantCulture), e.CodigoHttp?.ToString(CultureInfo.InvariantCulture) ?? "", e.Detalle };
            var linea = string.Join(',', valores.Select(Escapar)) + "\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes((nuevo ? Cabecera : "") + linea));
            await stream.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Interlocked.Increment(ref fallos);
            // No incluye entradas de usuario, claves ni mensajes de excepciones con rutas.
            logger.LogError("No se pudo persistir actividad. Tipo de fallo: {Tipo}", ex.GetType().Name);
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<EventoActividad>> LeerAsync()
    {
        await gate.WaitAsync();
        try
        {
            var registros = new List<EventoActividad>();
            foreach (var tipo in Enum.GetValues<TipoEvento>())
            {
                var path = Path.Combine(directorio, Archivo(tipo));
                if (!File.Exists(path)) continue;
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var parser = new TextFieldParser(file, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true };
                parser.SetDelimiters(",");
                parser.ReadFields();
                while (!parser.EndOfData)
                {
                    var v = parser.ReadFields();
                    if (v is null || v.Length != 11 || v[0] != "1") throw new InvalidDataException("Formato de actividad no válido.");
                    registros.Add(new(v[1], DateTimeOffset.Parse(v[2], CultureInfo.InvariantCulture), v[3],
                        Enum.Parse<TipoEvento>(v[4]), v[5], v[6], v[7], long.Parse(v[8], CultureInfo.InvariantCulture),
                        int.TryParse(v[9], out var codigo) ? codigo : null, v[10]));
                }
            }
            return registros.OrderByDescending(e => e.FechaUtc).ToArray();
        }
        finally { gate.Release(); }
    }
    private static string Escapar(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
}

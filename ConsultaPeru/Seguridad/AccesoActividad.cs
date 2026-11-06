using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OtpNet;
namespace ConsultaPeru.Seguridad;

public enum ResultadoAcceso { Permitido, Incorrecto, Bloqueado, SinConfigurar }
public sealed class AccesoActividad(IConfiguration config, TimeProvider reloj)
{
    public bool ProduccionHabilitada => config.GetValue<bool>("Actividad:ProduccionHabilitada");
    public string Sello => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('|', config["Actividad:Usuario"], config["Actividad:PasswordHash"], config["Actividad:TotpSecret"]))));

    // Se guarda fuera de wwwroot. El bloqueo de archivo protege también durante un reciclado de IIS.
    public ResultadoAcceso VerificarProduccion(string? usuario, string? password, string? codigo, string directorio)
    {
        if (!ProduccionHabilitada) return ResultadoAcceso.SinConfigurar;
        var nombre = config["Actividad:Usuario"];
        var hash = config["Actividad:PasswordHash"];
        var secreto = config["Actividad:TotpSecret"];
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(secreto))
            return ResultadoAcceso.SinConfigurar;
        lock (gate)
        {
            try
            {
                var clave = Base32Encoding.ToBytes(secreto);
                if (clave.Length < 20) return ResultadoAcceso.SinConfigurar;
                Directory.CreateDirectory(directorio);
                using var archivo = new FileStream(Path.Combine(directorio, "acceso-admin.json"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var estado = archivo.Length == 0 ? new EstadoAcceso() : JsonSerializer.Deserialize<EstadoAcceso>(archivo)
                    ?? throw new InvalidDataException();
                var ahora = reloj.GetUtcNow();
                if (estado.BloqueoHasta > ahora) return ResultadoAcceso.Bloqueado;
                if (ahora - estado.UltimoFallo > TimeSpan.FromMinutes(15)) estado.Fallos = 0;
                var passwordValido = password?.Length <= 256 && hasher.VerifyHashedPassword(this, hash, password ?? "") != PasswordVerificationResult.Failed;
                var totp = new Totp(clave);
                long ventana = -1;
                var codigoValido = codigo?.Length == 6 && codigo.All(char.IsAsciiDigit)
                    && totp.VerifyTotp(ahora.UtcDateTime, codigo, out ventana, new VerificationWindow(1, 1))
                    && ventana > estado.UltimaVentana;
                var permitido = usuario == nombre && passwordValido && codigoValido;
                if (permitido)
                {
                    estado.Fallos = 0;
                    estado.UltimaVentana = ventana;
                }
                else
                {
                    estado.UltimoFallo = ahora;
                    if (++estado.Fallos >= 5)
                    {
                        estado.BloqueoHasta = ahora.AddMinutes(15);
                        estado.Fallos = 0;
                    }
                }
                archivo.Position = 0;
                JsonSerializer.Serialize(archivo, estado);
                archivo.SetLength(archivo.Position);
                archivo.Flush(true);
                return permitido ? ResultadoAcceso.Permitido : estado.BloqueoHasta > ahora
                    ? ResultadoAcceso.Bloqueado : ResultadoAcceso.Incorrecto;
            }
            catch (IOException) { return ResultadoAcceso.Bloqueado; }
            catch (UnauthorizedAccessException) { return ResultadoAcceso.SinConfigurar; }
            catch (Exception ex) when (ex is FormatException or ArgumentException or JsonException)
            { return ResultadoAcceso.SinConfigurar; }
        }
    }

    public sealed class EstadoAcceso
    {
        public int Fallos { get; set; }
        public DateTimeOffset UltimoFallo { get; set; }
        public DateTimeOffset BloqueoHasta { get; set; }
        public long UltimaVentana { get; set; } = -1;
    }
    private readonly object gate = new();
    private readonly PasswordHasher<object> hasher = new();
    private int fallos;
    private DateTimeOffset bloqueoHasta;
    public ResultadoAcceso Verificar(string? password)
    {
        lock (gate)
        {
            if (bloqueoHasta > reloj.GetUtcNow()) return ResultadoAcceso.Bloqueado;
            var hash = config["Actividad:PasswordHash"];
            if (string.IsNullOrWhiteSpace(hash)) return ResultadoAcceso.SinConfigurar;
            var correcto = false;
            try { correcto = hasher.VerifyHashedPassword(this, hash, password ?? "") != PasswordVerificationResult.Failed; }
            catch (FormatException) { return ResultadoAcceso.SinConfigurar; }
            if (correcto) { fallos = 0; return ResultadoAcceso.Permitido; }
            if (++fallos >= 5) { fallos = 0; bloqueoHasta = reloj.GetUtcNow().AddMinutes(1); return ResultadoAcceso.Bloqueado; }
            return ResultadoAcceso.Incorrecto;
        }
    }
}

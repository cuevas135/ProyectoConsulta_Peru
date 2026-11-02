using Microsoft.AspNetCore.Identity;
namespace ConsultaPeru.Seguridad;

public enum ResultadoAcceso { Permitido, Incorrecto, Bloqueado, SinConfigurar }
public sealed class AccesoActividad(IConfiguration config, TimeProvider reloj)
{
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

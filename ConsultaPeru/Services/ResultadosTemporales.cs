using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace ConsultaPeru.Services;

// Resultados efímeros por navegador. El enlace contiene solo un identificador aleatorio.
public sealed class ResultadosTemporales : IDisposable
{
    private const string Cookie = "DatoAlToque.Resultados";
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 500 });
    public string Guardar<T>(HttpContext contexto, T resultado) where T : class
    {
        var propietario = contexto.Request.Cookies[Cookie];
        if (string.IsNullOrEmpty(propietario))
        {
            propietario = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            contexto.Response.Cookies.Append(Cookie, propietario, new CookieOptions {
                HttpOnly = true, Secure = contexto.Request.IsHttps, SameSite = SameSiteMode.Lax,
                IsEssential = true, Path = "/"
            });
        }
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set(id, new Entrada(propietario, resultado), new MemoryCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15), Size = 1
        });
        return id;
    }

    public T? Leer<T>(HttpContext contexto, string id) where T : class
    {
        if (id.Length != 64 || !cache.TryGetValue<Entrada>(id, out var entrada)
            || entrada is null || entrada.Propietario != contexto.Request.Cookies[Cookie]) return null;
        return entrada.Resultado as T;
    }
    public void Dispose() => cache.Dispose();
    private sealed record Entrada(string Propietario, object Resultado);
}

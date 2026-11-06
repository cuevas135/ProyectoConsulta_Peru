using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using OtpNet;

namespace ConsultaPeru.Seguridad;

public static class PrepararAcceso
{
    public static void Guardar(string raiz)
    {
        var carpeta = Path.Combine(raiz, "App_Data", "Administracion");
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, "configuracion-inicial.txt");
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var hash = new PasswordHasher<object>().HashPassword(new object(), password);
        var secreto = Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));
        using var archivo = new StreamWriter(new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.None));
        archivo.WriteLine($"""
            DATO AL TOQUE — CONFIGURACIÓN PRIVADA
            Guarda esta información en tu gestor de contraseñas. No la compartas ni la subas al hosting.

            Usuario de acceso: administrador
            Contraseña de acceso: {password}

            En Microsoft Authenticator o Google Authenticator agrega una cuenta mediante clave manual.
            Nombre de la cuenta: Dato al Toque — administrador
            Tipo: basada en tiempo (TOTP), seis dígitos, 30 segundos
            Clave manual: {secreto}

            Variables de entorno en SmarterASP.NET (sin espacios adicionales):
            Actividad__Usuario=administrador
            Actividad__PasswordHash={hash}
            Actividad__TotpSecret={secreto}
            Actividad__ProduccionHabilitada=true

            Configura las tres primeras variables y habilita ProduccionHabilitada al final.
            Recicla el sitio después de cambiar las variables. Accede por HTTPS a /Actividad.
            La contraseña y la clave del autenticador son exclusivas de este panel.
            Para revocar accesos desactiva ProduccionHabilitada y recicla el sitio.
            Para reemplazar credenciales, guarda primero las actuales de forma segura y genera un nuevo archivo.
            """);
        Console.WriteLine("Configuración privada guardada en App_Data/Administracion/configuracion-inicial.txt.");
    }
}

namespace ConsultaPeru.Services;

// No es un record: ToString() nunca debe imprimir accidentalmente la credencial.
public sealed class CredencialProveedor(string token)
{
    public string Token { get; } = token;
}

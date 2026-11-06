var builder = WebApplication.CreateBuilder(args);

// Preparación local de una cuenta única. Nunca se publica el archivo generado.
if (args.Contains("--preparar-acceso-actividad"))
{
    ConsultaPeru.Seguridad.PrepararAcceso.Guardar(builder.Environment.ContentRootPath);
    return;
}

// Configuración privada de esta máquina. Se copia al ejecutar, pero nunca al publicar.
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"),
    optional: true, reloadOnChange: false);

#if DEBUG
// Carga local explícita: no depende de cómo Visual Studio detecte el assembly de entrada.
// Solo completa una credencial ausente, sin reemplazar una configurada en el alojamiento.
if (string.IsNullOrWhiteSpace(builder.Configuration["ConsultaApi:Token"]))
{
    var carpetaPerfil = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    var archivoSecretos = builder.Configuration["ConsultaApi:SecretsFile"]
        ?? Path.Combine(carpetaPerfil, "Microsoft", "UserSecrets",
            "consulta-peru-aqpfact-local", "secrets.json");
    var secretosLocales = new ConfigurationBuilder()
        .AddJsonFile(archivoSecretos, optional: true, reloadOnChange: false)
        .Build();
    var credencialLocal = secretosLocales["ConsultaApi:Token"];
    if (!string.IsNullOrWhiteSpace(credencialLocal))
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConsultaApi:Token"] = credencialLocal
        });
}
#endif

// Captura estable: los servicios no dependen de recargas de configuración durante una consulta.
var credencialProveedor = new ConsultaPeru.Services.CredencialProveedor(
    builder.Configuration["ConsultaApi:Token"] ?? "");
builder.Services.AddSingleton(credencialProveedor);

// Diagnóstico local sin publicar endpoints, mostrar claves ni consultar al proveedor.
if (args.Contains("--verificar-credencial"))
{
    var configurada = !string.IsNullOrWhiteSpace(credencialProveedor.Token);
    Console.WriteLine($"Credencial configurada: {configurada}");
    Environment.ExitCode = configurada ? 0 : 1;
    return;
}

// Add services to the container.
builder.Services.AddSingleton<ConsultaPeru.Actividad.IRegistroActividad>(sp =>
    new ConsultaPeru.Actividad.RegistroCsv(
        builder.Configuration["Actividad:Directorio"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Actividad"),
        sp.GetRequiredService<ILogger<ConsultaPeru.Actividad.RegistroCsv>>()));
builder.Services.AddScoped<ConsultaPeru.Actividad.RegistroPaginasFilter>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages(options => options.Conventions.ConfigureFilter(
    new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(ConsultaPeru.Actividad.RegistroPaginasFilter))));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ConsultaPeru.Services.ResultadosTemporales>();
builder.Services.AddSingleton<ConsultaPeru.Services.ICacheTipoCambio>(sp =>
    new ConsultaPeru.Services.CacheTipoCambioCsv(
        builder.Configuration["TipoCambio:DirectorioCache"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "TipoCambio"),
        sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<ConsultaPeru.Seguridad.AccesoActividad>();
builder.Services.AddAuthentication("ActividadLocal").AddCookie("ActividadLocal", options => {
    options.LoginPath = "/AccesoActividad";
    options.Cookie.Name = "DatoAlToque.Actividad.v2";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = context =>
    {
        var acceso = context.HttpContext.RequestServices.GetRequiredService<ConsultaPeru.Seguridad.AccesoActividad>();
        var local = builder.Environment.IsDevelopment()
            && context.HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip);
        var valido = context.Principal?.FindFirst("ActividadSello")?.Value == acceso.Sello
            && (local || (context.HttpContext.Request.IsHttps && acceso.ProduccionHabilitada
                && context.Principal?.FindFirst("ActividadSegundoFactor")?.Value == "totp"));
        if (!valido) context.RejectPrincipal();
        return Task.CompletedTask;
    };
});
builder.Services.AddTransient<ConsultaPeru.Services.RespaldoHandler>();
builder.Services.AddHttpClient("ApiAnterior", client => {
    client.BaseAddress = new Uri(builder.Configuration["ConsultaApi:Respaldo:BaseUrl"] ?? "https://api.apis.net.pe/v1/");
    client.Timeout = TimeSpan.FromSeconds(7);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ConsultaPeru/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).RemoveAllLoggers();
builder.Services.AddHttpClient<ConsultaPeru.Services.TipoCambioService>(client => {
    client.BaseAddress = new Uri(builder.Configuration["ConsultaApi:BaseUrl"] ?? "https://apis.aqpfact.pe/api/");
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ConsultaPeru/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).AddHttpMessageHandler<ConsultaPeru.Services.RespaldoHandler>().RemoveAllLoggers();
builder.Services.AddHttpClient<ConsultaPeru.Services.ConsultaService>(client => {
    client.BaseAddress = new Uri(builder.Configuration["ConsultaApi:BaseUrl"] ?? "https://apis.aqpfact.pe/api/");
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ConsultaPeru/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).AddHttpMessageHandler<ConsultaPeru.Services.RespaldoHandler>().RemoveAllLoggers();

var app = builder.Build();
#if DEBUG
app.Logger.LogInformation("Credencial del proveedor configurada: {Configurada}",
    !string.IsNullOrWhiteSpace(app.Configuration["ConsultaApi:Token"]));
#endif

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.UseRouting();

// Captura errores fuera del handler (por ejemplo, al renderizar una página), sin mensajes sensibles.
app.Use(async (context, next) =>
{
    var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        if (!context.Items.ContainsKey("ActividadErrorRegistrado"))
        {
            var registro = context.RequestServices.GetRequiredService<ConsultaPeru.Actividad.IRegistroActividad>();
            await registro.GuardarAsync(new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
                context.Items["ActividadSolicitudId"] as string ?? Guid.NewGuid().ToString("N"),
                ConsultaPeru.Actividad.TipoEvento.Incidencia, "Aplicacion", "Excepcion",
                DuracionMs: (long)System.Diagnostics.Stopwatch.GetElapsedTime(inicio).TotalMilliseconds,
                Detalle: ex.GetType().Name));
        }
        throw;
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();




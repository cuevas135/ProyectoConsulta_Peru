using ConsultaPeru.Services;
using Microsoft.Extensions.Configuration;
using System.Net;

if (args.Contains("--crear-clave-local")) {
 var path=Path.GetFullPath(args[^1]);
 var json=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
 var password="CP-"+Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)).Replace('+','-').Replace('/','_');
 var hash=new Microsoft.AspNetCore.Identity.PasswordHasher<object>().HashPassword(new object(),password);
 if(json["Actividad"] is not System.Text.Json.Nodes.JsonObject) json["Actividad"]=new System.Text.Json.Nodes.JsonObject();
 json["Actividad"]!["PasswordHash"]=hash;
 await File.WriteAllTextAsync(path,json.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
 Console.WriteLine("Contraseña inicial: "+password);
 return;
}
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConsultaApi:Token"]="test-token" }).Build();
var clock = new FixedClock();
var count = 0;
async Task<Respuesta> Dni(string birth, string? number = "12345678") {
 var handler = new FakeHandler($$$"""{"success":true,"data":{"numero":"{{{number}}}","nombre_completo":"PERSONA DE PRUEBA","nombres":"PRUEBA","apellido_paterno":"PERSONA","apellido_materno":"FICTICIA","fecha_nacimiento":"{{{birth}}}","sexo":"NO EXPONER","estado_civil":"NO EXPONER"}}""");
 var result = await new ConsultaService(Client(handler), config, clock).Consultar("DNI","12345678",default);
 Check(handler.Path == "/api/dni/12345678" && handler.Auth == "Bearer test-token", "Ruta y autorización DNI"); return result;
}
void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; }
HttpClient Client(FakeHandler h) => new(h) {BaseAddress=new Uri("https://example.test/api/")};
Check((await Dni("01/11/2008")).Datos is not null,"Mayoría de edad el día exacto");
Check((await Dni("02/11/2008")).Datos is null,"Menor: un día antes de cumplir 18");
Check((await Dni("01/01/2015")).Datos is null,"Menor bloqueado");
Check((await Dni("")).Datos is null,"Edad desconocida bloqueada");
Check((await Dni("01/01/2099")).Datos is null,"Nacimiento futuro bloqueado");
Check((await Dni("01/01/1980","87654321")).Datos is null,"Documento incorrecto bloqueado");
var adult=(await Dni("07/11/1952")).Datos!;
Check(!System.Text.Json.JsonSerializer.Serialize(adult).Contains("1952"),"Nacimiento no sale del servicio");
foreach(var payload in new[]{"{\"success\":false,\"data\":{},\"message\":\"error remoto\"}","{\"success\":false,\"data\":[]}","{\"success\":true,\"data\":[]}","[]","not-json"}) {
 var r=await new ConsultaService(Client(new FakeHandler(payload)),config,clock).Consultar("RUC","20123456789",default);
 Check(r.Datos is null && r.Error is not null,"Respuesta sin datos o inválida");
}
var ruc=await new ConsultaService(Client(new FakeHandler("""{"success":true,"data":{"ruc":"20123456789","nombre_o_razon_social":"EMPRESA FICTICIA","estado":"ACTIVO","condicion":"HABIDO","ubigeo_sunat":"150140","es_agente_de_retencion":"","es_buen_contribuyente":null}}""")),config,clock).Consultar("RUC","20123456789",default);
Check(ruc.Datos?.Adicionales.Any(c=>c.Valor=="150140")==true,"Mapeo RUC y campos nulos");
var cambioHandler=new FakeHandler("""{"success":true,"data":{"fecha_sunat":"2026-05-21","fecha_busqueda":"2026-05-21","compra":3.408,"venta":3.414,"moneda":"USD"}}""");
var cambio=await new TipoCambioService(Client(cambioHandler),config).Consultar(new(2026,5,21),default);
Check(cambio.Datos?.Compra==3.408m && cambio.Datos.Venta==3.414m && cambioHandler.Path=="/api/tipo-cambio-dia/2026-05-21","Mapeo diario");
var limited=await new ConsultaService(Client(new FakeHandler("",HttpStatusCode.TooManyRequests)),config,clock).Consultar("DNI","12345678",default);
Check(limited.Error?.Contains("límite")==true,"Límite del proveedor");
var missing=new FakeHandler("{}");
var emptyConfig=new ConfigurationBuilder().Build();
Check((await new ConsultaService(Client(missing),emptyConfig,clock).Consultar("DNI","12345678",default)).Datos is null && missing.Path is null,"Sin token no envía solicitudes");
async Task<(Respuesta Result, int Calls, FakeHandler Legacy)> WithFallback(string primary, HttpStatusCode status = HttpStatusCode.OK, string type = "RUC", string legacyBody = "", bool enabled = true) {
 if (legacyBody == "") legacyBody = """{"numeroDocumento":"20123456789","nombre":"EMPRESA RESPALDO","ubigeo":"150140","estado":"ACTIVO"}""";
 var legacy = new FakeHandler(legacyBody);
 var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
 ["ConsultaApi:Token"]="primary-secret", ["ConsultaApi:Respaldo:Habilitado"]=enabled.ToString() }).Build();
 var primaryHandler = new FakeHandler(primary,status);
 var fallback = new RespaldoHandler(new FixedFactory(Client(legacy)),settings) {InnerHandler=primaryHandler};
 using var http = new HttpClient(fallback) {BaseAddress=new Uri("https://primary.test/api/")};
 var result=await new ConsultaService(http,settings,clock).Consultar(type,type=="DNI"?"12345678":"20123456789",default);
 return (result,primaryHandler.Calls,legacy);
}
var backed=await WithFallback("",HttpStatusCode.ServiceUnavailable);
Check(backed.Result.Datos?.Nombre=="EMPRESA RESPALDO" && backed.Legacy.Calls==1,"Respaldo RUC por 503");
Check(backed.Legacy.Auth is null && backed.Legacy.Path=="/api/ruc","No filtra token principal al respaldo");
var noResult=await WithFallback("""{"success":false,"data":[]}""");
Check(noResult.Result.Datos is not null,"Respaldo por success:false");
var malformed=await WithFallback("not-json");
Check(malformed.Result.Datos is not null,"Respaldo por JSON inválido");
var primaryOk=await WithFallback("""{"success":true,"data":{"ruc":"20123456789","nombre_o_razon_social":"PRINCIPAL"}}""");
Check(primaryOk.Result.Datos?.Nombre=="PRINCIPAL" && primaryOk.Legacy.Calls==0,"Principal exitoso no llama respaldo");
var privacy=await WithFallback("""{"success":true,"data":{"numero":"12345678","nombre_completo":"MENOR FICTICIO","fecha_nacimiento":"01/01/2015"}}""",type:"DNI");
Check(privacy.Result.Datos is null && privacy.Legacy.Calls==0,"Restricción de menor no activa respaldo");
var oldDni=await WithFallback("",HttpStatusCode.ServiceUnavailable,"DNI","""{"numeroDocumento":"12345678","nombre":"PRUEBA","nombres":"PRUEBA"}""");
Check(oldDni.Result.Datos is null && oldDni.Legacy.Calls==1,"DNI respaldo sin nacimiento no expone datos");
var disabled=await WithFallback("",HttpStatusCode.TooManyRequests,enabled:false);
Check(disabled.Result.Datos is null && disabled.Legacy.Calls==0,"Respaldo deshabilitado");
var badRequest=await WithFallback("",HttpStatusCode.BadRequest);
Check(badRequest.Legacy.Calls==0,"Error de entrada 400 no activa respaldo");
var tcOld=new FakeHandler("""{"fecha":"2026-05-21","compra":3.408,"venta":3.414,"moneda":"USD"}""");
using(var http=new HttpClient(new RespaldoHandler(new FixedFactory(Client(tcOld)),config){InnerHandler=new FakeHandler("",HttpStatusCode.TooManyRequests)}){BaseAddress=new Uri("https://primary.test/api/")}) {
 var result=await new TipoCambioService(http,config).Consultar(new(2026,5,21),default);
 Check(result.Datos?.Venta==3.414m && tcOld.Path=="/api/tipo-cambio-sunat","Respaldo tipo de cambio");
}
var mutableConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConsultaApi:Token"]="snapshot-token"}).Build();
var stableCredential=new CredencialProveedor(mutableConfig["ConsultaApi:Token"]!);
mutableConfig["ConsultaApi:Token"]="";
var stableHandler=new FakeHandler("""{"success":true,"data":{"ruc":"20123456789","nombre_o_razon_social":"EMPRESA FICTICIA"}}""");
var stableResult=await new ConsultaService(Client(stableHandler),mutableConfig,clock,stableCredential).Consultar("RUC","20123456789",default);
Check(stableResult.Datos is not null && stableHandler.Auth=="Bearer snapshot-token","Credencial estable en el servicio de documentos");
var stableChangeHandler=new FakeHandler("""{"success":true,"data":{"fecha_sunat":"2026-05-21","compra":3.408,"venta":3.414,"moneda":"USD"}}""");
var stableChange=await new TipoCambioService(Client(stableChangeHandler),mutableConfig,stableCredential).Consultar(new(2026,5,21),default);
Check(stableChange.Datos is not null && stableChangeHandler.Auth=="Bearer snapshot-token","Credencial estable en tipo de cambio");
var eventsDir=Path.Combine(Path.GetTempPath(),"ConsultaPeruChecks",Guid.NewGuid().ToString("N"));
var store=new ConsultaPeru.Actividad.RegistroCsv(eventsDir,Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsultaPeru.Actividad.RegistroCsv>.Instance);
var store2=new ConsultaPeru.Actividad.RegistroCsv(eventsDir,Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsultaPeru.Actividad.RegistroCsv>.Instance);
await Task.WhenAll(Enumerable.Range(0,40).Select(i=>(i%2==0?store:store2).GuardarAsync(new(
 Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,"solicitud-ficticia",ConsultaPeru.Actividad.TipoEvento.Busqueda,
 "RUC",i%2==0?"ConResultados":"SinResultados",Detalle:"Prueba, con \"comillas\"\ny otra línea"))));
var rows=await store.LeerAsync();
Check(rows.Count==40 && rows.Count(e=>e.Estado=="ConResultados")==20,"CSV: concurrencia y conteos exactos entre instancias");
Check(rows.All(e=>e.Detalle=="Prueba, con \"comillas\"\ny otra línea"),"CSV: escape y lectura de comas, comillas y saltos");
var reopened=new ConsultaPeru.Actividad.RegistroCsv(eventsDir,Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsultaPeru.Actividad.RegistroCsv>.Instance);
Check((await reopened.LeerAsync()).Count==40,"CSV: persistencia al reiniciar repositorio");
Check(store.Estado.Disponible && store2.Estado.Disponible,"CSV: escritura disponible");
var unavailablePath=Path.Combine(eventsDir,"archivo-no-directorio");File.WriteAllText(unavailablePath,"test");
var unavailable=new ConsultaPeru.Actividad.RegistroCsv(unavailablePath,Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsultaPeru.Actividad.RegistroCsv>.Instance);
await unavailable.GuardarAsync(rows[0]);
Check(!unavailable.Estado.Disponible && unavailable.Estado.FallosEscritura==1,"Fallo de persistencia visible sin romper consulta");
var logDir=Path.Combine(eventsDir,"proveedores");
var apiStore=new ConsultaPeru.Actividad.RegistroCsv(logDir,Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsultaPeru.Actividad.RegistroCsv>.Instance);
var requestContext=new Microsoft.AspNetCore.Http.DefaultHttpContext();requestContext.Items["ActividadSolicitudId"]="correlacion-ficticia";
var requestAccessor=new Microsoft.AspNetCore.Http.HttpContextAccessor{HttpContext=requestContext};
using(var http=new HttpClient(new RespaldoHandler(new FixedFactory(Client(new FakeHandler("""{"numeroDocumento":"20123456789","nombre":"EMPRESA FICTICIA"}"""))),config,apiStore,requestAccessor){InnerHandler=new FakeHandler("",HttpStatusCode.ServiceUnavailable)}){BaseAddress=new Uri("https://primary.test/api/")}) {
 Check((await new ConsultaService(http,config,clock).Consultar("RUC","20123456789",default)).Datos is not null,"Respaldo con registro habilitado");
}
var apiEvents=await apiStore.LeerAsync();
Check(apiEvents.Count(e=>e.Tipo==ConsultaPeru.Actividad.TipoEvento.IntentoApi)==2 && apiEvents.Count(e=>e.Tipo==ConsultaPeru.Actividad.TipoEvento.Incidencia)==1,"Incidencia del principal aunque el respaldo responda");
Check(apiEvents.All(e=>e.SolicitudId=="correlacion-ficticia"),"Correlación de intentos y fallos");
var csvContents=string.Join("",Directory.GetFiles(logDir).Select(File.ReadAllText));
Check(!csvContents.Contains("20123456789") && !csvContents.Contains("test-token") && !csvContents.Contains("EMPRESA FICTICIA"),"No persiste documento, clave ni razón social");
var noInfo=await new ConsultaService(Client(new FakeHandler("""{"success":false,"data":[]}""")),config,clock).Consultar("RUC","20123456789",default);
Check(noInfo.Estado==ConsultaPeru.Actividad.EstadoConsulta.SinResultados,"Sin información no se confunde con error técnico");
var localContext=new Microsoft.AspNetCore.Http.DefaultHttpContext();localContext.Connection.RemoteIpAddress=IPAddress.Loopback;
var productionPanel=new ConsultaPeru.Pages.ActividadModel(store,new FakeEnvironment("Production")) {PageContext=new Microsoft.AspNetCore.Mvc.RazorPages.PageContext{HttpContext=localContext}};
Check(await productionPanel.OnGetAsync() is Microsoft.AspNetCore.Mvc.NotFoundResult,"Panel privado: producción devuelve 404");
var remoteContext=new Microsoft.AspNetCore.Http.DefaultHttpContext();remoteContext.Connection.RemoteIpAddress=IPAddress.Parse("192.0.2.1");
var remotePanel=new ConsultaPeru.Pages.ActividadModel(store,new FakeEnvironment("Development")) {PageContext=new Microsoft.AspNetCore.Mvc.RazorPages.PageContext{HttpContext=remoteContext}};
Check(await remotePanel.OnGetAsync() is Microsoft.AspNetCore.Mvc.NotFoundResult,"Panel privado: conexión remota devuelve 404");
var adminHash=new Microsoft.AspNetCore.Identity.PasswordHasher<object>().HashPassword(new object(),"clave-ficticia-para-tests");
var adminConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Actividad:PasswordHash"]=adminHash}).Build();
var access=new ConsultaPeru.Seguridad.AccesoActividad(adminConfig,clock);
Check(access.Verificar("incorrecta")==ConsultaPeru.Seguridad.ResultadoAcceso.Incorrecto,"Contraseña incorrecta denegada");
Check(access.Verificar("clave-ficticia-para-tests")==ConsultaPeru.Seguridad.ResultadoAcceso.Permitido,"Hash de contraseña válido");
for(var i=0;i<4;i++) access.Verificar("incorrecta");
Check(access.Verificar("incorrecta")==ConsultaPeru.Seguridad.ResultadoAcceso.Bloqueado,"Cinco intentos activan bloqueo");
Check(access.Verificar("clave-ficticia-para-tests")==ConsultaPeru.Seguridad.ResultadoAcceso.Bloqueado,"No elude bloqueo con otra petición");
Check(new ConsultaPeru.Seguridad.AccesoActividad(emptyConfig,clock).Verificar("cualquiera")==ConsultaPeru.Seguridad.ResultadoAcceso.SinConfigurar,"Sin contraseña no concede acceso");
var cacheDir=Path.Combine(Path.GetTempPath(),"ConsultaPeruCacheChecks",Guid.NewGuid().ToString("N"));
var exchangeCache=new CacheTipoCambioCsv(cacheDir,clock);
var exchangeCache2=new CacheTipoCambioCsv(cacheDir,clock);
var requests=0;
async Task<RespuestaCambio> Quote(CancellationToken token) {
 Interlocked.Increment(ref requests);await Task.Delay(40,token);
 return new(new TipoCambio(new(2026,5,21),3.408m,3.414m,"USD"),null);
}
var cachedQuotes=await Task.WhenAll(Enumerable.Range(0,8).Select(i=>(i%2==0?exchangeCache:exchangeCache2).ObtenerAsync(new(2026,5,21),Quote,default)));
Check(requests==1 && cachedQuotes.Count(q=>q.Origen=="CSV")==7,"Caché: consultas simultáneas usan una sola llamada API");
var loadedCache=new CacheTipoCambioCsv(cacheDir,clock);
var noApiHandler=new FakeHandler("unexpected");
var offlineService=new TipoCambioService(Client(noApiHandler),emptyConfig,cache:loadedCache);
var offlineQuote=await offlineService.Consultar(new(2026,5,21),default);
Check(offlineQuote.Origen=="CSV" && offlineQuote.Datos?.Venta==3.414m && noApiHandler.Calls==0,"Caché persistente funciona sin token ni red");
var errors=0;
Task<RespuestaCambio> FailedQuote(CancellationToken token){errors++;return Task.FromResult(new RespuestaCambio(null,"Error ficticio"));}
await exchangeCache.ObtenerAsync(new(2026,5,22),FailedQuote,default);
await exchangeCache.ObtenerAsync(new(2026,5,22),FailedQuote,default);
Check(errors==2 && File.ReadAllLines(Path.Combine(cacheDir,"tipo_cambio.csv")).Length==2,"Errores no se guardan como cotizaciones");
var invalidQuote=await exchangeCache.ObtenerAsync(new(2026,5,20),_=>Task.FromResult(new RespuestaCambio(new(new(2026,5,21),3m,4m,"USD"),null)),default);
Check(invalidQuote.Datos is null,"Cotización futura no contamina la fecha solicitada");
var badCacheDir=Path.Combine(cacheDir,"invalido");Directory.CreateDirectory(badCacheDir);File.WriteAllText(Path.Combine(badCacheDir,"tipo_cambio.csv"),"archivo incorrecto");
var badCacheService=new TipoCambioService(Client(noApiHandler),config,cache:new CacheTipoCambioCsv(badCacheDir,clock));
Check((await badCacheService.Consultar(new(2026,5,21),default)).Error is not null && noApiHandler.Calls==0,"CSV inválido no se sobrescribe ni dispara consumo automático");
var mismatched=await exchangeCache.ObtenerAsync(new(2026,5,23),_=>Task.FromResult(new RespuestaCambio(new(new(2026,5,22),3.4m,3.5m,"USD"),null)),default);
var mapped=await loadedCache.ObtenerAsync(new(2026,5,23),FailedQuote,default);
Check(mapped.Origen=="CSV" && mapped.Datos?.Fecha==new DateOnly(2026,5,22),"CSV conserva fecha consultada y fecha publicada sin confundirlas");
Console.WriteLine($"Correcto: {count} comprobaciones sin conexiones externas ni datos personales reales.");
sealed class FixedClock:TimeProvider {public override DateTimeOffset GetUtcNow()=>new(2026,11,1,12,0,0,TimeSpan.Zero);}
sealed class FakeHandler(string body,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler {
 public string? Path;public string? Auth;public int Calls;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;Path=request.RequestUri!.AbsolutePath;Auth=request.Headers.Authorization?.ToString();return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body)});}
}


sealed class FixedFactory(HttpClient client):IHttpClientFactory {public HttpClient CreateClient(string name)=>client;}



sealed class FakeEnvironment(string name):Microsoft.AspNetCore.Hosting.IWebHostEnvironment {
 public string EnvironmentName {get;set;}=name;
 public string ApplicationName {get;set;}="Checks";
 public string WebRootPath {get;set;}="";
 public string ContentRootPath {get;set;}="";
 public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider {get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
 public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider {get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
}

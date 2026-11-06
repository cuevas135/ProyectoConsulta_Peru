using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
namespace ConsultaPeru.Actividad;

public sealed class RegistroPaginasFilter(IRegistroActividad registro, TimeProvider reloj) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        // No se almacenan URL completa, query string, documento, IP, cookies ni user-agent.
        var pagina = context.HandlerInstance;
        var operacion = pagina is Pages.IndexModel ? "Documentos" : pagina is Pages.TipoCambioModel ? "TipoCambioDia" : null;
        if (operacion is null) { await next(); return; }
        var inicio = Stopwatch.GetTimestamp();
        var id = Guid.NewGuid().ToString("N");
        http.Items["ActividadSolicitudId"] = id;
        var esBusqueda = HttpMethods.IsPost(http.Request.Method);
        try
        {
            var ejecutado = await next();
            if (ejecutado.Exception is not null && !ejecutado.ExceptionHandled) {
                http.Items["ActividadErrorRegistrado"] = true;
                await Guardar(TipoEvento.Incidencia, "Excepcion", ejecutado.Exception.GetType().Name);
                if (esBusqueda) await Guardar(TipoEvento.Busqueda, EstadoConsulta.Error.ToString(), "Excepcion");
                return;
            }
            if (!esBusqueda) {
                if (!http.Request.Query.ContainsKey("consulta")) await Guardar(TipoEvento.Visita, "CargaPagina");
                return;
            }
            var estado = pagina is Pages.IndexModel doc ? doc.EstadoBusqueda : ((Pages.TipoCambioModel)pagina).EstadoBusqueda;
            operacion = pagina is Pages.IndexModel d && d.Tipo is "DNI" or "RUC" ? d.Tipo : operacion;
            var error = pagina is Pages.IndexModel dm ? dm.Error : ((Pages.TipoCambioModel)pagina).Error;
            await Guardar(TipoEvento.Busqueda, estado.ToString(), error ?? "");
            if (estado == EstadoConsulta.Error) await Guardar(TipoEvento.Incidencia, "ErrorConsulta", error ?? "Error de consulta");
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            if (esBusqueda) await Guardar(TipoEvento.Busqueda, EstadoConsulta.Cancelada.ToString());
            throw;
        }
        catch (Exception ex)
        {
            http.Items["ActividadErrorRegistrado"] = true;
            await Guardar(TipoEvento.Incidencia, "Excepcion", ex.GetType().Name);
            if (esBusqueda) await Guardar(TipoEvento.Busqueda, EstadoConsulta.Error.ToString(), "Excepcion");
            throw;
        }
        Task Guardar(TipoEvento tipo, string estado, string detalle = "") => registro.GuardarAsync(new(
            Guid.NewGuid().ToString("N"), reloj.GetUtcNow(), id, tipo, operacion!, estado,
            Proveedor: pagina is Pages.TipoCambioModel cambio && tipo == TipoEvento.Busqueda ? cambio.OrigenConsulta : "",
            DuracionMs: (long)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds, Detalle: detalle));
    }
}

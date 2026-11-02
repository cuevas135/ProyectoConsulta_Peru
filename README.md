# Consulta Perú

ASP.NET Core Razor Pages, .NET 10. Abrir ConsultaPeru.sln en Visual Studio 2026, elegir perfil http y ejecutar Ctrl+F5. Dirección: http://localhost:5293. Si hay una versión anterior ejecutándose, detenerla antes de recompilar.

## Proveedor AQPFACT

Base predeterminada: https://apis.aqpfact.pe/api/. Solicitudes GET a dni/{numero}, ruc/{numero} y tipo-cambio-dia/{yyyy-MM-dd}, con Bearer Token y Accept: application/json. Se valida el sobre success/data, el documento devuelto y las cotizaciones. Las respuestas success:false se muestran como información no disponible; no prueban que el documento no exista.

La clave se obtiene de ConsultaApi:Token. En desarrollo usa secretos de usuario de .NET (UserSecretsId: consulta-peru-aqpfact-local). En producción configurar ConsultaApi__Token en el alojamiento. Nunca guardar claves en el código, appsettings, launchSettings ni el repositorio. ConsultaApi:BaseUrl permite otro servidor compatible con el mismo esquema, no adaptar automáticamente otros proveedores.

## Privacidad y resultados

DNI público: solo nombres, apellidos y número. Se usa fecha_nacimiento en el servidor para verificar mayoría de edad a la fecha actual de Perú. Menores, nacimiento ausente, inválido o futuro: respuesta restringida, sin datos personales en el resultado. El filtro depende de la exactitud del proveedor y no sustituye comprobar la base legal del tratamiento. No se exponen nacimiento, sexo, estado civil, dirección ni código verificador del DNI. Se desactivaron los logs automáticos de HttpClient para no registrar URLs con documentos.

RUC: razón social, estado, condición, dirección, dirección completa, nombre comercial, ubicación, ubigeo SUNAT, agente de retención y buen contribuyente cuando el proveedor los suministre. Los campos ausentes se muestran como No informado; no se inventan valores. No se conserva el desglose de vía del proveedor anterior porque no aparece en este esquema.

Cada dato disponible tiene botón Copiar; requiere acceso al portapapeles. No se persisten resultados en base de datos. PostgreSQL no está conectado.

## Tipo de cambio

/TipoCambio consulta compra/venta por fecha y muestra fecha_sunat (o fecha si no existe). Fecha inicial según Perú; no se consultan fechas futuras. La consulta mensual queda pendiente de conocer su respuesta JSON. SOAT pospuesto. El respaldo automático se describe abajo; no hay reintentos adicionales al intento único por proveedor.

## Comprobaciones

`dotnet run --project ConsultaPeru.Checks` ejecuta pruebas sin red ni créditos: rutas, autorización ficticia, success:false con data objeto/lista, campos nulos, documento incorrecto, errores, mapeo y límites de mayoría de edad. Los datos de prueba son ficticios.

Antes de publicación: verificar el uso permitido del proveedor y la base legal, añadir límites y protección contra consultas automatizadas, definir privacidad y configurar HTTPS.

## Respaldo automático

AQPFACT es principal. Ante HTTP 401/403/404/408/429/5xx, error de conexión, timeout, JSON inválido, success:false o data vacío se intenta una sola consulta al proveedor anterior https://api.apis.net.pe/v1/. No se consulta a ambos si el principal responde correctamente. No se reintentan errores HTTP 400 ni restricciones de privacidad aplicadas por ConsultaService. Una respuesta semánticamente inválida que pasa la validación básica del sobre se rechaza por el servicio, sin otra consulta.

Respaldo: dni?numero=, ruc?numero= y tipo-cambio-sunat?fecha=. Se normaliza la respuesta a los modelos actuales. No se inventan campos ausentes; la clave de AQPFACT nunca se envía al respaldo. Credencial separada opcional: ConsultaApi:Respaldo:Token. Configuraciones: ConsultaApi:Respaldo:Habilitado (true por defecto) y ConsultaApi:Respaldo:BaseUrl (barra final). Cada intento tiene límite de 7 segundos, con presupuesto total de 20 segundos. La cancelación del usuario no activa respaldo.

La API anterior normalmente no informa nacimiento: sus resultados de DNI se restringen cuando no se puede verificar mayoría de edad. Esto impide que el respaldo eluda la protección de menores. RUC y cambio diario sí pueden utilizarse si su respuesta es válida. La disponibilidad actual del servicio anterior no está garantizada. La integración se verifica con respuestas simuladas sin consumir consultas reales.


## Configuración local para pruebas

En esta máquina, ConsultaPeru/appsettings.Local.json contiene la configuración privada local. Está excluido de Git, se copia junto al ejecutable durante la compilación y tiene CopyToPublishDirectory=Never. La aplicación carga esa copia desde AppContext.BaseDirectory. Para editarlo: abrir el archivo en Visual Studio y detener/reiniciar la ejecución después del cambio. No compartir este archivo ni copiar manualmente la carpeta bin a un hosting: para desplegar, usar Publicar y configurar ConsultaApi__Token en el servidor.

Esta configuración local tiene prioridad sobre los secretos de usuario. En otro equipo, crear el archivo privado siguiendo la estructura de appsettings y configurar ConsultaApi:Token. No añadirlo al repositorio. La variable ConsultaApi__SecretsFile de launchSettings es una alternativa anterior de esta máquina; el archivo local evita depender de ella para las pruebas.

## Registro tabulado sin PostgreSQL

Se utiliza IRegistroActividad con implementación RegistroCsv, sin paquetes ni motor de base de datos. Carpeta predeterminada: ConsultaPeru/App_Data/Actividad (o Actividad:Directorio para un directorio persistente del servidor). No está en wwwroot, Git ni los archivos de publicación. Los CSV aparecen al registrar el primer evento correspondiente, son UTF-8 y usan comas, cabecera y comillas compatibles con importación. No abrirlos en modo de edición mientras la web escribe; trabajar con una copia.

Archivos: visitas.csv, busquedas.csv, incidencias.csv, intentos_api.csv. Columnas comunes: version, id, fecha_utc, solicitud_id, tipo, operacion, estado, proveedor, duracion_ms, codigo_http, detalle. Los identificadores son aleatorios; solicitud_id permite relacionar los intentos y el resultado de una búsqueda sin almacenar su número de documento.

/Actividad muestra totales, agrupaciones y los últimos 100 eventos, exclusivamente en Development y desde loopback. En producción responde 404 hasta implementar autenticación administrativa. El enlace solo aparece localmente. No expone archivos CSV por HTTP.

Visitas: cargas GET de las páginas DNI/RUC y tipo de cambio que llegan al handler. Recargar suma otra visita; no representa usuarios únicos, sesiones ni personas reales, y puede incluir bots. No cuenta recursos estáticos, /Actividad ni recargas POST. Búsquedas: envíos POST que llegan al handler, incluso entradas inválidas. Los rechazos previos de ASP.NET (p. ej., antifalsificación) no cuentan como búsquedas. Una búsqueda puede tener dos intentos de proveedor, pero solo una fila final.

Estados finales: ConResultados, SinResultados (el proveedor no entrega información; no demuestra inexistencia del documento), Restringida (privacidad), Validacion (entrada inválida), Error (fallo técnico) y Cancelada. Los intentos informan proveedor, disponibilidad y código HTTP cuando existe; una respuesta recibida no implica necesariamente que se haya mostrado al usuario. Incidencias también recoge fallos del proveedor principal recuperados por el respaldo y excepciones de aplicación. Por eso incidencias no equivale a búsquedas fallidas.

No se almacenan DNI, RUC, nombres, fecha de nacimiento, IP, cookies, user-agent, claves, cuerpos de API ni URL de consulta. Los detalles de errores conocidos son mensajes internos; de excepciones solo se almacena el nombre del tipo. Si la escritura falla, se informa en el log de la aplicación y el panel muestra un aviso y el número de fallos de la ejecución actual; no se oculta como un registro exitoso. Ese aviso en memoria se reinicia al reiniciar la app. Mantener copias de seguridad del directorio; no borrarlo al desplegar. El CSV está pensado para una instancia pequeña; para volumen alto o varias instancias conviene pasar a base de datos.

Migración: docs/actividad-postgresql.sql define una tabla destino con los mismos campos. Puede importarse cada CSV preservando IDs y marcas UTC, después reemplazar RegistroCsv por otro IRegistroActividad. El panel muestra horas de Perú. Los conteos no reconstruyen actividad anterior a la activación.

## Acceso privado a Actividad

Se retiró el enlace de la cabecera. Atajo: Ctrl+Alt+A; también se puede abrir /Actividad directamente. Ocultar el enlace no es la protección: el servidor exige autenticación mediante cookie HttpOnly, SameSite Strict, no persistente, con vencimiento absoluto de 20 minutos. La pantalla de contraseña aparece como ventana superpuesta antes de cargar las estadísticas. Cerrar acceso privado elimina la cookie mediante POST protegido contra falsificación.

La contraseña se verifica con PasswordHasher de ASP.NET Core (PBKDF2). Solo se guarda Actividad:PasswordHash en appsettings.Local.json, nunca la contraseña en texto claro. No usar un número personal más día/mes: el patrón es predecible. Tras cinco fallos se bloquean los intentos por un minuto en esta instancia. El bloqueo se reinicia al reiniciar el proceso.

El panel y el acceso siguen restringidos a Development y conexiones locales; no se ha habilitado administración remota en producción. Para el hosting se necesitará habilitar acceso administrativo expresamente con HTTPS y configuración de secretos. La cookie se envía por HTTPS cuando el acceso es HTTPS y admite HTTP solo para las pruebas locales actuales.

Para generar/reemplazar una contraseña aleatoria local: dotnet run --project ConsultaPeru.Checks -- --crear-clave-local RUTA_ABSOLUTA_A_appsettings.Local.json. La herramienta muestra la nueva contraseña una vez y guarda solo su hash, conservando las otras configuraciones. Detener y reiniciar la web después. Anotar la contraseña en un gestor de contraseñas; no incluirla en documentación ni Git.

## Caché CSV de tipo de cambio

La consulta diaria primero busca en App_Data/TipoCambio/tipo_cambio.csv; si existe una cotización válida para esa fecha, responde sin pedir token ni llamar a ninguna API. Si no existe, consulta el proveedor y guarda solo resultados válidos. Usa el mismo servicio para respuestas obtenidas del respaldo. Configuración opcional de ubicación: TipoCambio:DirectorioCache. Mantener esta carpeta en almacenamiento persistente al desplegar y hacer copia de seguridad; está fuera de wwwroot, Git y publicación.

Columnas: fecha_consultada,fecha_publicada,moneda,compra,venta,guardado_utc. Importes con punto decimal y fechas ISO. No se necesita PostgreSQL. El archivo tipo_cambio.lock coordina procesos; no borrarlo mientras la aplicación está en ejecución. La escritura del CSV se hace con archivo temporal y reemplazo atómico, y las consultas concurrentes esperan el mismo bloqueo antes de volver a leer. Los errores del proveedor nunca se cachean.

Las cotizaciones históricas se reutilizan sin vencimiento. Si para hoy el proveedor entrega una fecha publicada anterior, esa respuesta se reutiliza por 15 minutos; luego se vuelve a verificar para no congelar un valor provisional. Se conserva y muestra la fecha publicada real. Las cotizaciones devueltas con fecha posterior a la solicitada, moneda distinta de USD o importes no positivos se rechazan. Si el CSV es inválido o no puede leerse/escribirse, se muestra un error y se registra la incidencia, sin sobrescribir datos dañados ni ocultar el fallo. No hay actualización manual de cotizaciones históricas por ahora.

En busquedas.csv, proveedor indica CSV o API para tipo de cambio; los intentos_api.csv solo se generan cuando hubo consultas reales al proveedor. Una búsqueda satisfecha con CSV sigue contando como búsqueda con resultados, pero no añade un intento de API.

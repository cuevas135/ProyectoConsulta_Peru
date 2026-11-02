-- Esquema de destino opcional. No se ejecuta ni requiere PostgreSQL ahora.
-- Los cuatro CSV comparten estas columnas y se importan en esta tabla.
CREATE TABLE actividad (
    version integer NOT NULL,
    id uuid PRIMARY KEY,
    fecha_utc timestamptz NOT NULL,
    solicitud_id text NOT NULL,
    tipo text NOT NULL CHECK (tipo IN ('Visita','Busqueda','Incidencia','IntentoApi')),
    operacion text NOT NULL,
    estado text NOT NULL,
    proveedor text NOT NULL DEFAULT '',
    duracion_ms bigint NOT NULL DEFAULT 0,
    codigo_http integer,
    detalle text NOT NULL DEFAULT ''
);
CREATE INDEX actividad_fecha ON actividad (fecha_utc);
CREATE INDEX actividad_solicitud ON actividad (solicitud_id);
CREATE INDEX actividad_tipo_estado ON actividad (tipo,estado);
-- En psql: \copy actividad FROM 'ruta/visitas.csv' WITH (FORMAT csv, HEADER true, ENCODING 'UTF8');
-- Repetir para busquedas.csv, incidencias.csv e intentos_api.csv, una sola vez por archivo.
-- Para importaciones repetidas, usar una tabla staging y ON CONFLICT (id) DO NOTHING.

-- Caché de cotizaciones diarias (importación de tipo_cambio.csv).
CREATE TABLE tipo_cambio_cache (
    fecha_consultada date PRIMARY KEY,
    fecha_publicada date NOT NULL,
    moneda text NOT NULL CHECK (moneda = 'USD'),
    compra numeric NOT NULL CHECK (compra > 0),
    venta numeric NOT NULL CHECK (venta > 0),
    guardado_utc timestamptz NOT NULL,
    CHECK (fecha_publicada <= fecha_consultada)
);

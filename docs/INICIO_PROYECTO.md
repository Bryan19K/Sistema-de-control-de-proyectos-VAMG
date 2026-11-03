# Sistema de Gestión de Proyectos TEK-COM — Guía de inicio en Code

> Documento de arranque para trabajar el proyecto en **Claude Code**.
> Parte 1 es para Bryan (pasos manuales). Parte 2 en adelante es el contexto que Code debe leer y seguir.
> Elaborado por: Bryan Calderón · Versión 1.0 · Noviembre 2026

---

## Parte 1 · Pasos antes de abrir Code (Bryan)

1. **Crear el repositorio en GitHub**
   - Ir a `github.com/new`
   - Nombre: `tekcom-gestion`
   - Visibilidad: **Private**
   - Marcar **Add a README file**
   - Crear.
2. **Abrir Code** en la app y seleccionar el repositorio `Bryan19K/tekcom-gestion`.
3. **Subir al repositorio** (o tenerlos a mano para pasarlos en la primera sesión):
   - Este archivo → guardarlo como `docs/INICIO_PROYECTO.md`
   - `PrePlan_Sistema_Gestion_Proyectos_v0.2.pdf` → `docs/`
   - Logo TEK-COM (PNG fondo transparente o SVG) → `docs/assets/`
   - Excel actuales de control (avance, entregas, inventario, catálogo, seriales, proyecciones de diseño) → `docs/excel-origen/` *(no se suben datos sensibles a un repo público; este es privado)*
4. **Primer mensaje para Code** (copiar y pegar):

```
Lee docs/INICIO_PROYECTO.md completo. Es el plan del sistema que vamos a construir.
Empieza por el Sprint 0 (estructura de la solución). Trabaja en una rama
feature/sprint-0, haz commits pequeños y abre un pull request cuando termines
para que yo lo revise. No avances al siguiente sprint sin mi aprobación.
```

5. **En tu PC** (para probar): instalar .NET 10 SDK, SQL Server Express (o Docker) y Visual Studio 2022/2026 o VS Code.

---

## Parte 2 · Contexto del proyecto (para Code)

### 2.1 Qué es
Sistema web que reemplaza el control en múltiples Excel de los proyectos de TEK-COM. Junta en un solo lugar:
proyectos, plan de trabajo, sprints, tareas, avance por nodo/sitio, materiales, bodega, proyección de diseño,
liquidación, equipos/seriales y documentación. **Los Excel no desaparecen: se generan desde el sistema.**

### 2.2 Usuarios
- 2 usuarios iniciales, ambos administradores (Bryan y su pareja).
- Diseñar con roles desde el inicio (`Admin`, `Bodega`, `Supervisor`, `Gerencia` solo lectura, `Contratista`) aunque al principio solo se use `Admin`.

### 2.3 Principios no negociables
1. **Un dato, un lugar.** Nada se captura dos veces.
2. **El stock no se guarda, se calcula** a partir de movimientos (ingresos − entregas + devoluciones ± ajustes).
3. **Nada se borra.** Los movimientos se anulan con motivo (soft delete + auditoría).
4. **Auditoría:** cada registro guarda quién y cuándo lo creó/modificó.
5. **Transacciones:** una entrega y su efecto en stock se guardan juntos o no se guarda nada.
6. **No permitir stock negativo** sin una justificación explícita.
7. **Todo exporte (Excel/PDF) lleva encabezado fijo:** logo TEK-COM, nombre del proyecto, fecha de corte y "Elaborado por".
8. **Módulos según tipo de proyecto:** FTTH/telecom usa todo; General usa solo plan, sprints, tareas y documentos.
9. Interfaz y textos en **español**. Código (clases, métodos, variables) en **inglés** o español, pero **consistente**; se elige en Sprint 0 y se documenta.

---

## Parte 3 · Tecnología

| Capa | Elección |
|---|---|
| Framework | **.NET 10 (LTS)** |
| UI | **Blazor Web App** (render interactivo en servidor) + **MudBlazor** |
| Datos | **EF Core 10** + **SQL Server** (local: Express o Docker; producción: Azure SQL) |
| Autenticación | **ASP.NET Core Identity** con roles |
| Excel | **ClosedXML** (exportar e importar) |
| PDF | **QuestPDF** (verificar licencia Community vigente) |
| Tareas programadas | **Hangfire** |
| Notificaciones | **WhatsApp Business Cloud API** (o Twilio) + correo (Brevo/SendGrid) como respaldo |
| Gantt | Componente compatible con Blazor (evaluar en Sprint 5) |
| Pruebas | **xUnit** + **FluentAssertions**; base en memoria o SQLite para pruebas de servicios |
| Publicación | **Azure App Service F1** + **Azure SQL (oferta gratuita)**, CI/CD con **GitHub Actions** |

### 3.1 Estructura de la solución

```
tekcom-gestion/
├── src/
│   ├── Tekcom.Gestion.Domain/          Entidades, enums, reglas de negocio puras
│   ├── Tekcom.Gestion.Application/     Servicios, DTOs, validaciones, interfaces
│   ├── Tekcom.Gestion.Infrastructure/  EF Core, DbContext, migraciones, Excel, PDF, WhatsApp, correo
│   └── Tekcom.Gestion.Web/             Blazor + MudBlazor, Identity, páginas y componentes
├── tests/
│   ├── Tekcom.Gestion.Domain.Tests/
│   └── Tekcom.Gestion.Application.Tests/
├── docs/
│   ├── INICIO_PROYECTO.md
│   ├── decisiones/                    Registro de decisiones técnicas (ADR)
│   ├── assets/                        Logo TEK-COM
│   └── excel-origen/                  Excel actuales para migración
├── .github/workflows/                  CI (build + tests) y despliegue
├── .editorconfig
├── .gitignore
├── Tekcom.Gestion.sln
└── README.md
```

### 3.2 Configuración y secretos
- Nunca subir cadenas de conexión, contraseñas ni tokens al repo.
- Local: `dotnet user-secrets`. Producción: configuración de Azure App Service.
- Incluir `appsettings.Example.json` con las claves vacías para documentar qué se necesita.

---

## Parte 4 · Modelo de datos inicial

> Es una propuesta. Se ajusta en Sprint 1 después de revisar los Excel reales en `docs/excel-origen/`.

**Gestión**
- `Project` — nombre, cliente, tipo (`FTTH`, `General`, `Custom`), estado, fechas inicio/fin plan y real, responsable
- `ProjectType` — módulos activos
- `WorkPlanPhase` / `WorkPlanActivity` — fechas plan/real, dependencias (`ActivityDependency`), hitos
- `Sprint` — proyecto, nombre, objetivo, fecha inicio, fecha fin (duración libre)
- `TaskItem` — título, descripción, responsable, prioridad, estado (`PorHacer`, `EnProceso`, `EnRevision`, `Hecho`), fecha límite, sprint (nullable = backlog), vínculo opcional a nodo/material/documento
- `TaskChecklistItem`, `TaskComment`, `Attachment`

**Técnico (FTTH)**
- `Site` (nodo/sitio) — proyecto, código (SON19…), contratista, fechas, ubicación/link mapa
- `Contractor` — nombre, brigada, responsable, teléfono
- `WorkItem` (partida) — sitio, descripción, cantidad proyectada, cantidad ejecutada
- `Material` — código ERP, nombre, unidad, categoría
- `MaterialAlias` — nombre alterno → material (ej. `SUB-BOX` → `NAP DE PASO`)
- `DesignProjection` — sitio, versión, fecha, origen (archivo) · `DesignProjectionLine` — material, cantidad
- `InventoryMovement` — tipo (`Ingreso`, `EntregaContratista`, `Devolucion`, `Ajuste`, `Instalado`), material, cantidad, sitio, contratista, fecha, lote/entrega, referencia, anulado + motivo
- `Liquidation` — sitio, fecha, estado, observaciones (cálculos: proyectado, entregado, instalado, sobrante, devuelto, pendiente, variación)
- `Equipment` / `EquipmentSlot` / `Transceiver` (OLT, tarjetas, puertos, SFP con serial y estado)

**Documentos y sistema**
- `Document` (ATP, minuta, otro) con fotos/anexos
- `ExportTemplate` — reporte, columnas, colores, agrupación
- `NotificationRule` / `NotificationLog`
- Campos de auditoría en todas las entidades: `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IsDeleted`

### 4.1 Cálculos clave
- **Stock por material** = Σ ingresos − Σ entregas + Σ devoluciones ± Σ ajustes (excluye anulados).
- **Indicador de ritmo** = `% avance − % tiempo transcurrido`
  - mayor a +margen → **Adelantado** · dentro de ±margen → **En tiempo** · menor a −margen → **Atrasado**
  - margen configurable, 10% por defecto. Aplica a proyecto, plan, sprint y sitio.
  - Calcular también días de adelanto/atraso y fecha de cierre proyectada.
- **Liquidación por material:** sobrante = entregado − instalado; pendiente = sobrante − devuelto; variación = instalado − proyectado (cantidad y %).

---

## Parte 5 · Plan de sprints

Cada sprint: rama `feature/sprint-N-nombre` → commits pequeños → pull request → revisión de Bryan → merge a `main`.

### Sprint 0 · Estructura (≈ 2–3 días)
- [ ] Crear solución y los 4 proyectos + 2 de pruebas con .NET 10
- [ ] Agregar MudBlazor con tema de colores TEK-COM (azul marino `#12304f`, acento naranja `#e8742c`)
- [ ] Layout base: menú lateral con las secciones del pre-plan, barra superior, página de inicio vacía
- [ ] DbContext, conexión SQL Server, primera migración
- [ ] ASP.NET Core Identity con roles y usuario administrador semilla (contraseña por user-secrets)
- [ ] Interceptor de auditoría (CreatedAt/By, UpdatedAt/By) y filtro global de soft delete
- [ ] `.gitignore`, `.editorconfig`, `appsettings.Example.json`
- [ ] GitHub Actions: build + tests en cada PR
- [ ] README con cómo correr el proyecto en local
- [ ] ADR-001: convención de idioma en el código

**Aceptación:** `dotnet build` y `dotnet test` pasan; la app corre, pide login y muestra el menú; la base se crea con la migración.

### Sprint 1 · Base y migración (1–2 semanas)
- [ ] Revisar los Excel de `docs/excel-origen/` y ajustar el modelo de datos
- [ ] CRUD de proyectos con tipo y módulos activos
- [ ] CRUD de contratistas y sitios/nodos
- [ ] Catálogo de materiales con alias
- [ ] Importador de Excel: limpieza, mapeo, importación y **reporte de pendientes** (filas con error no se importan a la fuerza)
- [ ] Validación de totales importados contra el Excel origen

**Aceptación:** importar "SUB-BOX" lo asocia a "NAP DE PASO" sin duplicar; los totales del sistema coinciden con los del Excel; las filas con error quedan listadas.

### Sprint 2 · Gestión: sprints y tareas (2 semanas)
- [ ] Sprints de duración libre por proyecto
- [ ] Backlog y tablero Kanban (arrastrar entre columnas)
- [ ] Tareas con responsable, prioridad, fecha, checklist, comentarios y adjuntos
- [ ] Vínculo de tarea a sitio / material / documento
- [ ] Indicador de ritmo del sprint y cierre de sprint (pendientes pasan al siguiente)
- [ ] Vista "Mis tareas"

**Aceptación:** un sprint con 15% hecho y 60% del tiempo aparece **Atrasado** en rojo; al cerrar un sprint, las tareas no terminadas se mueven al siguiente o al backlog.

### Sprint 3 · Bodega, proyección y liquidación (2–3 semanas)
- [ ] Registro de ingresos (por lote/entrega)
- [ ] Entregas a contratista con **bloqueo si no hay stock**
- [ ] Devoluciones, ajustes con motivo y anulación de movimientos
- [ ] Stock en tiempo real con alerta de stock bajo
- [ ] Importar proyección de diseño por sitio, con versiones
- [ ] Comparación proyectado vs. entregado vs. instalado durante la obra, con alertas
- [ ] Liquidación por sitio; **no permite cerrar** con pendientes sin justificar
- [ ] Bitácora de movimientos

**Aceptación:** entrega mayor al stock se rechaza mostrando el disponible; con 120 entregados, 102 instalados y 10 devueltos, la liquidación muestra 18 sobrante y 8 pendientes y bloquea el cierre.

### Sprint 4 · Exportes (1–2 semanas)
- [ ] Servicio de exportación con encabezado TEK-COM fijo (logo, proyecto, fecha de corte, "Elaborado por")
- [ ] Reportes: consolidado de materiales, entregas por sitio, inventario, liquidación (acta), estado de sprints
- [ ] Excel con filtros, formato y área de impresión configurada (carta)
- [ ] PDF con QuestPDF
- [ ] Plantillas de diseño de exporte guardables

**Aceptación:** todo Excel generado abre sin errores, trae logo, fecha de corte y "Elaborado por", y está listo para imprimir.

### Sprint 5 · Plan de trabajo, avance y dashboard (2–3 semanas)
- [ ] Fases y actividades con dependencias e hitos
- [ ] Vista Gantt (planificado vs. real, línea de hoy) y vista tabla
- [ ] Personalización del plan y plantillas de proyecto
- [ ] Avance por partida y por sitio
- [ ] Dashboard: indicadores, proyectos con ritmo, tareas por vencer, sprint actual, stock bajo
- [ ] Exporte del plan de trabajo con logo TEK-COM

### Sprint 6 · Notificaciones (1 semana)
- [ ] Hangfire con trabajos programados
- [ ] Correo (respaldo) y WhatsApp Business API
- [ ] Avisos: tarea asignada, por vencer (1 día), vencida, sprint/proyecto atrasado, stock bajo, resumen diario
- [ ] Bitácora de notificaciones enviadas

### Sprint 7 · Técnico: equipos, seriales y documentos (2 semanas)
- [ ] OLT, tarjetas, puertos y SFP con serial y estado
- [ ] ATP con registro fotográfico y exporte
- [ ] Minutas y pendientes por sitio

### Publicación
- Desde el Sprint 0 o 1: despliegue automático a Azure App Service desde `main` con GitHub Actions.
- Configurar alerta de presupuesto en Azure desde el primer día.

---

## Parte 6 · Reglas de trabajo para Code

1. **Leer este documento al inicio de cada sesión** y el README.
2. **Un sprint a la vez.** No empezar el siguiente sin aprobación de Bryan.
3. **Ramas:** `feature/sprint-N-nombre`; nunca commit directo a `main`.
4. **Commits pequeños** y descriptivos en español (ej. `Agrega bloqueo de entrega sin stock`).
5. **Pull request por sprint** con: qué se hizo, cómo probarlo, capturas si aplica, y qué quedó pendiente.
6. **Pruebas** para toda regla de negocio (stock, ritmo, liquidación, alias, importación).
7. **Antes de abrir el PR:** `dotnet build` y `dotnet test` en verde.
8. **No inventar datos de negocio.** Si un Excel o regla no está claro, preguntar a Bryan.
9. **Decisiones técnicas importantes** se registran en `docs/decisiones/` (ADR corto).
10. **Secretos nunca en el repo.**

### Definición de "Hecho"
- Cumple los criterios de aceptación del sprint
- Compila, pruebas pasan, sin advertencias nuevas importantes
- Funciona en local siguiendo el README
- Textos de interfaz en español, sin errores de ortografía
- PR revisado y aprobado por Bryan

---

## Parte 7 · Pendientes por definir (de la hoja de comentarios)

| # | Pendiente | Responsable |
|---|---|---|
| 1 | Reportes de Excel que se piden siempre y su formato | Bryan |
| 2 | Número para notificaciones de WhatsApp | Bryan |
| 3 | Avisos indispensables y cuáles sobran | Bryan y pareja |
| 4 | Otros tipos de proyecto además de FTTH | Bryan y pareja |
| 5 | Uso desde celular en campo | Bryan y pareja |
| 6 | Formato en que llega la proyección de diseño | Bryan |
| 7 | Logo TEK-COM en buena calidad | Bryan |
| 8 | Comentarios de la revisión del pre-plan | Pareja |

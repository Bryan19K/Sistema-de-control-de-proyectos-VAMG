# Guía de diseño · Sistema de Gestión TEK-COM

Los mockups de esta carpeta (01 a 06) son la **referencia oficial** de la interfaz. Toda pantalla
nueva debe verse como ellos. Esta guía resume los valores que se usan en el código
(`src/Tekcom.Gestion.Web/Theme/TekcomTheme.cs` y `wwwroot/app.css`).

## Colores

| Uso | Valor |
|---|---|
| Azul marino (barra lateral, títulos, botón oscuro) | `#12304f` |
| Naranja (acción principal, indicador del menú activo, línea "hoy" del Gantt) | `#e8742c` |
| Fondo de página | `#f3f5f8` |
| Tarjetas | blanco, borde `#e3e8ef`, radio 12 px |
| Texto principal | `#1f2a37` · secundario `#5b6b7c` |
| Estado rojo (Atrasado, Vencida, Hoy, Faltan) | fondo `#fde8e8`, texto `#c62828` |
| Estado verde (Adelantado, OK, Real a tiempo) | fondo `#e3f5ea`, texto `#1e8e4a` |
| Estado azul (En tiempo, Mié) | fondo `#e3edfb`, texto `#1d5fcf` |
| Estado amarillo (Mañana, Lun, Bajo) | fondo `#fdf1cf`, texto `#b7791f` |
| Planificado (Gantt) | `#d5dfeb` |
| Avatares | B azul marino `#12304f`, N morado `#7e3fbf` |

## Tipografía

Inter (Google Fonts), 400/500/600/700. Títulos de página 28 px semibold en azul marino,
subtítulo 15 px gris. Números grandes de indicadores 32 px bold.

## Componentes reutilizables (carpeta `Components/Shared`)

| Componente | Para qué |
|---|---|
| `PageHeader` | Título, subtítulo y botones de acción a la derecha |
| `KpiCard` | Tarjeta de indicador: etiqueta, número grande y nota de color |
| `StatusChip` | Chip de estado: `Atrasado`, `Adelantado`, `EnTiempo`, `Vencida`, `Hoy`, `Manana`, `Dia`, `Ok`, `Bajo`, `Falta` |
| `PaceBar` | Barra "avance vs. tiempo": % avance, % tiempo y color según ritmo |
| `SectionCard` | Tarjeta blanca con título y, opcionalmente, un número naranja de referencia |
| `UserAvatar` | Círculo con inicial del responsable |
| `EmptyState` | Mensaje cuando una sección aún no tiene datos |

## Reglas

1. El menú lateral tiene exactamente estas entradas: Inicio, Proyectos, Plan de trabajo,
   Sprints y tareas, Nodos y avance, Materiales, Seriales, Documentos, Exportar.
2. Los botones de acción van arriba a la derecha de cada página; el principal en naranja,
   el de exportar en azul marino, el resto con borde.
3. Las alertas de bloqueo (stock insuficiente, cierre de liquidación) se muestran en rojo
   dentro de la tarjeta, nunca como ventana emergente.
4. Todo exporte lleva: logo TEK-COM, nombre del reporte, proyecto, fecha de corte,
   "Elaborado por" y paginación.
5. Nunca se inventan datos: la página **Diseño** (`/diseno`) muestra los mockups con
   datos de ejemplo claramente marcados; las páginas reales muestran estados vacíos hasta
   que su sprint las conecte a la base.

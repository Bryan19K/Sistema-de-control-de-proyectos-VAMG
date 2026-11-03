# ADR-001 · Convención de idioma en el código

- **Estado:** aceptada
- **Fecha:** 2026-11-03
- **Sprint:** 0

## Contexto

El plan (principio 9) exige interfaz y textos en español, y permite el código en inglés o
español siempre que sea consistente. Hay que decidirlo antes de escribir la primera entidad.

## Decisión

| Elemento | Idioma | Ejemplo |
|---|---|---|
| Clases, métodos, propiedades, variables, enums | **Inglés** | `InventoryMovement`, `CalculateStock()` |
| Nombres de tablas y columnas (los genera EF Core) | **Inglés** | `AspNetUsers`, `CreatedAt` |
| Rutas de la aplicación (URL) | **Español** | `/proyectos`, `/bodega` |
| Textos de interfaz, mensajes de error y validación | **Español** | "Entrega mayor al stock disponible" |
| Comentarios y documentación XML | **Español** | |
| Nombres de pruebas | **Español**, con guiones bajos | `Al_crear_asigna_CreatedAt_y_CreatedBy` |
| Mensajes de commit, PR, ADR, README | **Español** | `Agrega bloqueo de entrega sin stock` |
| Registros de log | **Español** | |

Valores de enums que el usuario ve (por ejemplo estados de tarea) se nombran en inglés en el
código y se traducen al mostrarlos; nunca se guarda el texto traducido en la base.

## Glosario de términos del negocio

Para que el nombre inglés sea siempre el mismo en todo el código:

| Término TEK-COM | Nombre en código |
|---|---|
| Proyecto | `Project` |
| Plan de trabajo / fase / actividad | `WorkPlan` / `WorkPlanPhase` / `WorkPlanActivity` |
| Sprint | `Sprint` |
| Tarea | `TaskItem` (no `Task`, que choca con `System.Threading.Tasks.Task`) |
| Sitio / nodo | `Site` |
| Contratista / brigada | `Contractor` / `Crew` |
| Partida | `WorkItem` |
| Material | `Material` |
| Alias de material | `MaterialAlias` |
| Bodega | `Warehouse` |
| Movimiento de inventario | `InventoryMovement` |
| Ingreso / entrega / devolución / ajuste / instalado | `Receipt` / `Delivery` / `Return` / `Adjustment` / `Installed` |
| Proyección de diseño | `DesignProjection` |
| Liquidación | `Liquidation` |
| Equipo / tarjeta / puerto / SFP | `Equipment` / `Card` / `Port` / `Transceiver` |
| ATP / minuta | `AcceptanceTest` / `MeetingMinutes` |
| Indicador de ritmo | `PaceIndicator` |
| Anular (un movimiento) | `Void` |

## Consecuencias

- El código se lee con naturalidad junto a las librerías de .NET (todas en inglés).
- Quien revise la interfaz no verá ningún texto en inglés.
- Cualquier término nuevo del negocio se agrega a este glosario antes de usarlo.

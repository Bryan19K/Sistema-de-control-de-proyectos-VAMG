# Flujo operativo por nodo (proyectos FTTH)

> Fuente: indicación de Bryan Calderón del 2026-11-04. Lo que está en **"Dudas por confirmar"** es
> interpretación mía y no se implementa hasta que Bryan la confirme (regla 8 del plan: no inventar
> reglas de negocio).

## Secuencia 1 · Construcción de la ODN por nodo

| Paso | Etapa | Detalle |
|---|---|---|
| Previo | Aprobación de CAES | Aprobación de los planos, **por nodo**. |
| 1 | Replanteo del nodo | Se hace junto a un supervisor, **por nodo**. |
| 2 | Entrega de materiales | Por nodo, a la brigada contratista. |
| 3 | Instalación de cable FC | Largos: 5, 50, 100, 120, 150, 180, 200, 250, 300, 350, 400, 450 y 500 m. |
| 4 | Instalación de cable MPO | Largos: 100, 150, 200, 250, 300, 350, 400, 450 y 500 m. |
| 5 | Instalación de FAT | Sub-box y end-box. |
| 6 | Instalación de Hub-box y X-box | |
| 7 | Fusiones en X-box | |
| Cierre | **ODN finalizada** | Cuando se completan las etapas anteriores. |

## Secuencia 2 · Feeder (troncal)

Regla indicada: si el nodo **no tiene cierres de distribución ni de continuidad que fusionar**, el
sistema debe **notificar e indicar que el troncal o feeder ya pasó por ese nodo**.

## Cómo lo modela el sistema

1. **Cada nodo tiene su lista de etapas** (las de arriba), cada una con estado
   (`Pendiente`, `EnProceso`, `Completada`, `NoAplica`), fecha plan y real, quién la registró y
   observaciones. Es la base del avance por nodo y del indicador de ritmo.
2. **El avance del nodo y del proyecto se calcula** a partir de las etapas completadas
   (principio 1: un dato, un lugar). La plantilla "Plan FTTH estándar" del Gantt se genera con
   estas mismas etapas.
3. **El catálogo de materiales distingue cada largo**: un cable FC de 50 m y uno de 100 m son
   materiales distintos (igual que en el diseño: "Cable FC SC/APC 50 m", "Cable MPO 400 m").
   Hay 13 largos de FC y 9 de MPO.
4. **Lo instalado en cada etapa alimenta la liquidación**: cables FC (etapa 3), MPO (etapa 4),
   FAT (etapa 5), Hub-box y X-box (etapa 6). Así "Instalado" no se captura dos veces.
5. **Alias**: sub-box = NAP de paso y end-box = NAP final (ya definidos en el catálogo de alias).
6. **Feeder**: al abrir la etapa de fusiones o el feeder de un nodo, si la proyección de diseño del
   nodo no trae cierres de distribución ni de continuidad, el sistema marca la etapa como
   `NoAplica`, muestra el aviso "el troncal/feeder ya pasó por este nodo" y crea una notificación
   (Sprint 6 la envía por WhatsApp/correo).
7. **Bitácora**: cada cambio de estado de una etapa queda en auditoría (quién, cuándo, motivo).

## Dónde entra en el plan de sprints

- **Sprint 1:** plantilla de etapas por tipo de proyecto, aprobación CAES y replanteo por nodo,
  catálogo con los largos de FC y MPO.
- **Sprint 3:** entrega de materiales por nodo, instalado por etapa y liquidación.
- **Sprint 5:** Gantt y avance por nodo calculados desde las etapas.
- **Sprint 6:** notificación de feeder ya pasó por el nodo y avisos de etapas atrasadas.

## Dudas por confirmar

1. **Orden obligatorio o solo recomendado.** ¿El sistema debe *bloquear* una etapa si la anterior no
   está completa (por ejemplo, no entregar materiales sin aprobación de CAES y replanteo), o solo
   avisar?
2. **"ODN FINALIZADA".** ¿Es el cierre al terminar las fusiones (paso 7), o un estado aparte que se
   marca a mano? ¿La Secuencia 2 (feeder) empieza después de que la ODN está finalizada?
3. **Feeder: ¿por nodo o por tramo?** ¿El feeder se registra nodo por nodo, o por tramo de ruta que
   pasa por varios nodos?
4. **Cierres de distribución y de continuidad.** ¿Vienen con cantidad en la proyección de diseño
   (para que el sistema detecte "cero")? ¿O se cuentan en campo?
5. **Peso de cada etapa en el avance.** ¿Todas valen lo mismo, o cada etapa pesa distinto
   (por ejemplo, el cable pesa más que el replanteo)?
6. **"Cable SFC".** Asumí que es el cable **FC** (SC/APC) del catálogo. ¿Es correcto?
7. **Aprobación de CAES.** ¿Necesita número de oficio, fecha y adjunto del documento aprobado?

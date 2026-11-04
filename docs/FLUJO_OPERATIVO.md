# Flujo operativo por nodo (proyectos FTTH)

> Fuente: indicaciones de Bryan Calderón del 2026-11-04. Las reglas de la sección
> **"Decisiones confirmadas"** son definitivas. Lo de **"Pendiente por confirmar"** es
> interpretación y no se implementa hasta que Bryan lo confirme (regla 8 del plan).

## Secuencia 1 · Construcción de la ODN por nodo

| Paso | Etapa | Detalle |
|---|---|---|
| Previo | Supervisión de CAES | Solo se supervisa en campo. **No afecta el flujo.** |
| 1 | Replanteo del nodo | Se hace junto a un supervisor, por nodo. |
| 2 | Entrega de materiales | Por nodo, a la brigada contratista. |
| 3 | Instalación de cable FC | Largos: 5, 50, 100, 120, 150, 180, 200, 250, 300, 350, 400, 450 y 500 m. |
| 4 | Instalación de cable MPO | Largos: 100, 150, 200, 250, 300, 350, 400, 450 y 500 m. |
| 5 | Instalación de FAT | Sub-box y end-box. |
| 6 | Instalación de Hub-box y X-box | |
| 7 | Fusiones en X-box | |
| Cierre | **ODN finalizada** | Se marca **a mano** (ver decisión 2). |

## Secuencia 2 · Feeder (troncal)

Se maneja con estados manuales (ver decisión 3).

## Decisiones confirmadas

1. **CAES no condiciona nada.** Es una supervisión de campo. Aparece como una casilla informativa
   en el nodo y no bloquea ni retrasa ninguna otra etapa.
2. **"ODN finalizada" se marca manualmente.** La marca Bryan cuando están puestos los cruces
   americanos. El sistema no la calcula ni la marca solo.
3. **Feeder = estado manual.** Es un estado que se pone a mano para indicar que la troncal ya está
   lista. El sistema solo permite verificar y registrar que el feeder pasó por esa troncal; no
   lo deduce automáticamente.
4. **Los cierres de distribución y de continuidad vienen de la proyección de diseño.** Con esa
   cantidad el sistema sabe cuántos cierres hay que fusionar en cada nodo.
5. **Las etapas pesan por tiempo, no por valor.** Algunas tardan más que otras. El avance del nodo
   se calcula con los días planificados de cada etapa: avance = días de las etapas completadas ÷
   días totales de las etapas. Los días de cada etapa son configurables en la plantilla del
   proyecto.
6. **"Cable SFC" es el cable FC** (SC/APC) del catálogo.
7. **Documentos firmados.** El nodo tiene una sección para adjuntar los documentos firmados
   (por ejemplo, el de supervisión de CAES). Se guardan con fecha y usuario que los subió.

## Cómo lo modela el sistema

1. **Cada nodo tiene su lista de etapas**, con estado (`Pendiente`, `EnProceso`, `Completada`,
   `NoAplica`), fecha plan y real, quién la registró y observaciones. Es la base del avance por nodo
   y del indicador de ritmo.
2. **Avance ponderado por días planificados** (decisión 5). El Gantt "Plan FTTH estándar" usa esas
   mismas etapas y duraciones.
3. **El catálogo distingue cada largo de cable**: 13 de FC y 9 de MPO son materiales distintos
   (igual que en el diseño: "Cable FC SC/APC 50 m", "Cable MPO 400 m").
4. **Lo instalado en cada etapa alimenta la liquidación**: FC (etapa 3), MPO (etapa 4), FAT
   (etapa 5), Hub-box y X-box (etapa 6). Así "Instalado" no se captura dos veces.
5. **Alias:** sub-box = NAP de paso y end-box = NAP final.
6. **Cierres por nodo:** salen de la proyección de diseño importada. Si la proyección de un nodo
   no trae cierres de distribución ni de continuidad, el sistema muestra el aviso "el troncal/feeder
   ya pasó por este nodo" como ayuda; el estado de la troncal lo sigue poniendo Bryan a mano.
7. **Documentos:** adjuntos por nodo con fecha y usuario (principio 4: auditoría).
8. **Bitácora:** cada cambio de estado queda en auditoría (quién, cuándo, motivo).

## Dónde entra en el plan de sprints

- **Sprint 1:** plantilla de etapas con días por etapa, casilla de CAES, marca manual de ODN
  finalizada, estado manual de troncal, catálogo con los largos de FC y MPO.
- **Sprint 3:** entrega de materiales por nodo, instalado por etapa, cierres desde la proyección y
  liquidación.
- **Sprint 5:** Gantt y avance por nodo ponderado por días.
- **Sprint 6:** aviso de "feeder ya pasó por el nodo" y avisos de etapas atrasadas.
- **Sprint 7:** adjuntos de documentos firmados por nodo (si no se adelantan antes).

## Pendiente por confirmar

1. **Orden de las etapas 1 a 7:** mientras no se diga lo contrario, el sistema **avisa** cuando una
   etapa se registra fuera de orden, pero **no la bloquea**. ¿Está bien, o alguna debe bloquear?
2. **Cruces americanos:** ¿se registran en el sistema (por ejemplo, cantidad por nodo) o solo se
   marca "ODN finalizada" cuando ya están puestos?
3. **Troncal:** ¿el estado "troncal lista" es uno solo por proyecto, o uno por cada troncal/ruta?
